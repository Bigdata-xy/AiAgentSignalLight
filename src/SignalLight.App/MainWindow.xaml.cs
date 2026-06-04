using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Controls;
using System.Windows.Threading;
using SignalLight.Core.Sessions;
using SignalLight.Core.State;
using SignalLight.Storage;
using SignalLight.Storage.Json;
using Ellipse = System.Windows.Shapes.Ellipse;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

namespace SignalLight.App;

public partial class MainWindow : Window
{
    private static readonly Duration LampTransitionDuration = new(TimeSpan.FromMilliseconds(200));
    private static readonly TimeSpan GreenConfirmationDelay = TimeSpan.FromMilliseconds(800);

    private readonly SignalLightPaths _paths = new();
    private readonly JsonSignalStore _store;
    private readonly SignalStateEngine _engine = new();
    private readonly FileSystemWatcher _snapshotWatcher;
    private readonly FileSystemWatcher _diagnosticsWatcher;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _displayHoldTimer;
    private readonly Forms.NotifyIcon _trayIcon;
    private IReadOnlyList<SignalSession> _visibleSessions = Array.Empty<SignalSession>();
    private SignalSessionState _currentState = SignalSessionState.Unknown;
    private DateTimeOffset _greenConfirmationUntil = DateTimeOffset.MinValue;
    private SignalSessionState _pendingGreenState = SignalSessionState.Unknown;

    public MainWindow()
    {
        InitializeComponent();

        _store = new JsonSignalStore(_paths);
        _paths.EnsureAll();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            RefreshView();
        };
        _displayHoldTimer = new DispatcherTimer();
        _displayHoldTimer.Tick += (_, _) =>
        {
            _displayHoldTimer.Stop();
            RefreshView();
        };

        _snapshotWatcher = CreateWatcher(_paths.RootDirectory, Path.GetFileName(_paths.SnapshotPath));
        _diagnosticsWatcher = CreateWatcher(_paths.DiagnosticsDirectory, "latest-hook-context.json");
        _trayIcon = CreateTrayIcon();

        Closed += (_, _) =>
        {
            _snapshotWatcher.Dispose();
            _diagnosticsWatcher.Dispose();
            _trayIcon.Dispose();
        };

        Left = SystemParameters.WorkArea.Right - Width - 24;
        Top = SystemParameters.WorkArea.Top + 80;

        RefreshView();
    }

    private FileSystemWatcher CreateWatcher(string directory, string filter)
    {
        var watcher = new FileSystemWatcher(directory, filter)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        watcher.Changed += OnWatchedFileChanged;
        watcher.Created += OnWatchedFileChanged;
        watcher.Renamed += OnWatchedFileChanged;
        watcher.Deleted += OnWatchedFileChanged;
        return watcher;
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show / Hide", null, (_, _) => Dispatcher.Invoke(ToggleWindowVisibility));
        menu.Items.Add(new Forms.ToolStripSeparator());

        var hooksMenu = new Forms.ToolStripMenuItem("Hooks");
        hooksMenu.DropDownItems.Add("Install", null, async (_, _) => await Dispatcher.InvokeAsync(() => RunHookScriptAsync("install-hooks.ps1")).Task.Unwrap());
        hooksMenu.DropDownItems.Add("Uninstall", null, async (_, _) => await Dispatcher.InvokeAsync(() => RunHookScriptAsync("uninstall-hooks.ps1")).Task.Unwrap());
        menu.Items.Add(hooksMenu);

        var diagnosticsMenu = new Forms.ToolStripMenuItem("Diagnostics");
        diagnosticsMenu.DropDownItems.Add("Open data", null, (_, _) => Dispatcher.Invoke(OpenDataDirectory));
        diagnosticsMenu.DropDownItems.Add("Export", null, (_, _) => Dispatcher.Invoke(() => ShowInfo("Diagnostics exported", ExportDiagnostics())));
        diagnosticsMenu.DropDownItems.Add("Clear done", null, (_, _) => Dispatcher.Invoke(ClearCompletedSessions));
        menu.Items.Add(diagnosticsMenu);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Close));

        var icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Information,
            Text = "SignalLight",
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => Dispatcher.Invoke(ToggleWindowVisibility);
        return icon;
    }

    private void OnWatchedFileChanged(object sender, FileSystemEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _refreshTimer.Stop();
            _refreshTimer.Start();
        });
    }

    private void RefreshView()
    {
        var snapshot = _engine.BuildSnapshot(_store.LoadSessions());
        var displayState = GetDisplayState(snapshot.AggregateState);
        _visibleSessions = snapshot.Sessions;
        ApplyState(displayState);
        RenderSessionRows(snapshot.Sessions);

        SessionCountBadge.Visibility = snapshot.TotalCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        SessionCountText.Text = $"{snapshot.CompletedCount}/{snapshot.TotalCount}";

        if (snapshot.TotalCount == 0)
        {
            SetDrawerOpen(false);
        }
    }

    private SignalSessionState GetDisplayState(SignalSessionState aggregateState)
    {
        var now = DateTimeOffset.Now;

        if (aggregateState is not (SignalSessionState.Completed or SignalSessionState.Idle))
        {
            ClearPendingGreen();
            return aggregateState;
        }

        if (_currentState is not (SignalSessionState.Thinking or SignalSessionState.Waiting or SignalSessionState.Failed))
        {
            ClearPendingGreen();
            return aggregateState;
        }

        if (_pendingGreenState != aggregateState || _greenConfirmationUntil == DateTimeOffset.MinValue)
        {
            _pendingGreenState = aggregateState;
            _greenConfirmationUntil = now + GreenConfirmationDelay;
        }

        if (now < _greenConfirmationUntil)
        {
            ScheduleDisplayHoldRefresh(_greenConfirmationUntil - now);
            return _currentState;
        }

        ClearPendingGreen();
        return aggregateState;
    }

    private void ClearPendingGreen()
    {
        _greenConfirmationUntil = DateTimeOffset.MinValue;
        _pendingGreenState = SignalSessionState.Unknown;
        _displayHoldTimer.Stop();
    }

    private void ScheduleDisplayHoldRefresh(TimeSpan delay)
    {
        _displayHoldTimer.Stop();
        _displayHoldTimer.Interval = delay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : delay;
        _displayHoldTimer.Start();
    }

    private void ApplyState(SignalSessionState state)
    {
        _currentState = state;
        StopAnimations();

        ApplyLampVisual(SignalSessionState.Thinking, RedLamp, RedWell, RedRing, state == SignalSessionState.Thinking);
        ApplyLampVisual(SignalSessionState.Waiting, YellowLamp, YellowWell, YellowRing, state == SignalSessionState.Waiting);
        ApplyLampVisual(SignalSessionState.Completed, GreenLamp, GreenWell, GreenRing, state is SignalSessionState.Completed or SignalSessionState.Idle);

        _trayIcon.Text = $"SignalLight: {state}";
    }

    private void ApplyLampVisual(SignalSessionState state, Ellipse lamp, Ellipse well, Ellipse ring, bool active)
    {
        var config = GetLampConfig(state);
        lamp.Fill = CreateLampBrush(config, active);
        lamp.BeginAnimation(OpacityProperty, CreateDoubleAnimation(1));

        well.Fill = CreateWellBrush(config, active);
        well.BeginAnimation(OpacityProperty, CreateDoubleAnimation(1));

        var lampGlow = CreateLampGlow(config.GlowColor, active ? 12 : 0, active ? 0.9 : 0);
        lamp.Effect = lampGlow;

        var wellGlow = CreateLampGlow(config.GlowColor, active ? 40 : 0, active ? 0.82 : 0);
        well.Effect = wellGlow;

        ring.Fill = new SolidColorBrush(config.ActiveColor);
        ring.Effect = CreateLampGlow(config.GlowColor, 28, 0.7);
        ring.Opacity = 0;
        ResetRingScale(ring);

        if (!active)
        {
            return;
        }

        StartBreath(lamp, lampGlow, wellGlow, TimeSpan.FromMilliseconds(900));
        StartPulse(ring, TimeSpan.FromMilliseconds(900));
    }

    private static LampVisualConfig GetLampConfig(SignalSessionState state)
    {
        return state switch
        {
            SignalSessionState.Thinking => new LampVisualConfig(
                MediaColor.FromRgb(255, 59, 48),
                MediaColor.FromRgb(75, 20, 20),
                MediaColor.FromArgb(140, 255, 59, 48)),
            SignalSessionState.Waiting => new LampVisualConfig(
                MediaColor.FromRgb(255, 159, 10),
                MediaColor.FromRgb(75, 55, 15),
                MediaColor.FromArgb(140, 255, 159, 10)),
            SignalSessionState.Completed or SignalSessionState.Idle => new LampVisualConfig(
                MediaColor.FromRgb(48, 209, 88),
                MediaColor.FromRgb(20, 75, 35),
                MediaColor.FromArgb(140, 48, 209, 88)),
            _ => new LampVisualConfig(
                MediaColor.FromRgb(96, 96, 96),
                MediaColor.FromRgb(36, 36, 36),
                MediaColor.FromArgb(0, 96, 96, 96))
        };
    }

    private static RadialGradientBrush CreateLampBrush(LampVisualConfig config, bool active)
    {
        var baseColor = active ? config.ActiveColor : config.DimColor;
        return new RadialGradientBrush
        {
            Center = new WpfPoint(0.42, 0.32),
            GradientOrigin = new WpfPoint(0.34, 0.24),
            RadiusX = 0.74,
            RadiusY = 0.74,
            GradientStops =
            {
                new GradientStop(WithAlpha(baseColor, active ? (byte)255 : (byte)204), 0.0),
                new GradientStop(WithAlpha(baseColor, active ? (byte)221 : (byte)160), 0.42),
                new GradientStop(WithAlpha(baseColor, active ? (byte)136 : (byte)105), 1.0)
            }
        };
    }

    private static RadialGradientBrush CreateWellBrush(LampVisualConfig config, bool active)
    {
        return new RadialGradientBrush
        {
            Center = new WpfPoint(0.5, 0.45),
            GradientOrigin = new WpfPoint(0.45, 0.32),
            RadiusX = 0.72,
            RadiusY = 0.72,
            GradientStops =
            {
                new GradientStop(active ? WithAlpha(config.DimColor, 210) : MediaColor.FromRgb(27, 27, 28), 0.0),
                new GradientStop(MediaColor.FromRgb(23, 23, 24), 0.58),
                new GradientStop(MediaColor.FromRgb(7, 7, 8), 1.0)
            }
        };
    }

    private static MediaColor WithAlpha(MediaColor color, byte alpha)
    {
        return MediaColor.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static DropShadowEffect CreateLampGlow(MediaColor color, double blurRadius, double opacity)
    {
        return new DropShadowEffect
        {
            Color = color,
            BlurRadius = blurRadius,
            ShadowDepth = 0,
            Opacity = opacity
        };
    }

    private static DoubleAnimation CreateDoubleAnimation(double targetValue)
    {
        return new DoubleAnimation
        {
            To = targetValue,
            Duration = LampTransitionDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
    }

    private static void StartBreath(Ellipse lamp, DropShadowEffect lampGlow, DropShadowEffect wellGlow, TimeSpan duration)
    {
        var opacityAnimation = new DoubleAnimation
        {
            From = 1,
            To = 0.35,
            Duration = duration,
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        var glowAnimation = new DoubleAnimation
        {
            From = 0.9,
            To = 0.38,
            Duration = duration,
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        lamp.BeginAnimation(OpacityProperty, opacityAnimation);
        lampGlow.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnimation);
        wellGlow.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnimation.Clone());
    }

    private static void StartPulse(Ellipse ring, TimeSpan duration)
    {
        var ringOpacityAnimation = new DoubleAnimation
        {
            From = 0.62,
            To = 0,
            Duration = duration,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var ringScaleAnimation = new DoubleAnimation
        {
            From = 1,
            To = 1.72,
            Duration = duration,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        ring.BeginAnimation(OpacityProperty, ringOpacityAnimation);
        if (ring.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, ringScaleAnimation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, ringScaleAnimation.Clone());
        }
    }

    private void StopAnimations()
    {
        foreach (var lamp in new[] { RedLamp, YellowLamp, GreenLamp })
        {
            lamp.BeginAnimation(OpacityProperty, null);
            lamp.Opacity = 1;
            if (lamp.Effect is DropShadowEffect glow)
            {
                glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
            }
        }

        foreach (var well in new[] { RedWell, YellowWell, GreenWell })
        {
            if (well.Effect is DropShadowEffect glow)
            {
                glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
            }
        }

        foreach (var ring in new[] { RedRing, YellowRing, GreenRing })
        {
            ring.BeginAnimation(OpacityProperty, null);
            ring.Opacity = 0;
            ResetRingScale(ring);
        }
    }

    private static void ResetRingScale(Ellipse ring)
    {
        if (ring.RenderTransform is not ScaleTransform scale)
        {
            return;
        }

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        scale.ScaleX = 1;
        scale.ScaleY = 1;
    }

    private void ToggleWindowVisibility()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        Show();
        Activate();
    }

    private async Task RunHookScriptAsync(string scriptName)
    {
        var scriptPath = FindToolScript(scriptName);
        if (scriptPath is null)
        {
            ShowInfo("SignalLight", $"{scriptName} not found.");
            return;
        }

        var result = await RunPowerShellAsync(scriptPath);
        ShowInfo(
            result.ExitCode == 0 ? "SignalLight" : "SignalLight error",
            result.ExitCode == 0 ? $"{scriptName} completed." : FirstLine(result.Error, result.Output));
    }

    private void OpenDataDirectory()
    {
        _paths.EnsureAll();
        Process.Start(new ProcessStartInfo
        {
            FileName = _paths.RootDirectory,
            UseShellExecute = true
        });
    }

    private string ExportDiagnostics()
    {
        _paths.EnsureAll();
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var exportRoot = Path.Combine(_paths.DiagnosticsDirectory, $"export-{timestamp}");
        var zipPath = Path.Combine(_paths.DiagnosticsDirectory, $"SignalLight-Diagnostics-{timestamp}.zip");

        if (Directory.Exists(exportRoot))
        {
            Directory.Delete(exportRoot, recursive: true);
        }

        Directory.CreateDirectory(exportRoot);
        try
        {
            CopyIfExists(_paths.SnapshotPath, Path.Combine(exportRoot, "snapshot.json"));
            CopyDirectoryIfExists(_paths.SessionsDirectory, Path.Combine(exportRoot, "sessions"));
            CopyDirectoryIfExists(_paths.EventsDirectory, Path.Combine(exportRoot, "events"));
            CopyDirectoryIfExists(_paths.DiagnosticsDirectory, Path.Combine(exportRoot, "diagnostics"), filePrefix: "latest-");

            var hooksJson = GetHooksJsonPath();
            if (File.Exists(hooksJson))
            {
                CopyIfExists(hooksJson, Path.Combine(exportRoot, "codex-hooks.json"));
            }

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            ZipFile.CreateFromDirectory(exportRoot, zipPath);
            return zipPath;
        }
        finally
        {
            Directory.Delete(exportRoot, recursive: true);
        }
    }

    private void ClearCompletedSessions()
    {
        var completed = _store.LoadSessions()
            .Where(session => session.State is SignalSessionState.Completed or SignalSessionState.Idle)
            .ToArray();

        foreach (var session in completed)
        {
            var path = Path.Combine(_paths.SessionsDirectory, Sanitize(session.SessionId) + ".json");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        _store.SaveSnapshot(_engine.BuildSnapshot(_store.LoadSessions()));
        RefreshView();
        ShowInfo("SignalLight", $"Cleared {completed.Length} completed sessions.");
    }

    private void RenderSessionRows(IReadOnlyList<SignalSession> sessions)
    {
        SessionListPanel.Children.Clear();

        if (sessions.Count == 0)
        {
            SessionListPanel.Children.Add(new TextBlock
            {
                Text = "暂无任务",
                Foreground = new SolidColorBrush(MediaColor.FromRgb(168, 168, 176)),
                FontSize = 12,
                Margin = new Thickness(4, 10, 0, 0)
            });
            return;
        }

        foreach (var session in sessions)
        {
            SessionListPanel.Children.Add(CreateSessionRow(session));
        }
    }

    private UIElement CreateSessionRow(SignalSession session)
    {
        var row = new Grid
        {
            Margin = new Thickness(4, 8, 0, 0),
            Opacity = session.State is SignalSessionState.Completed or SignalSessionState.Idle ? 0.62 : 1
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var config = GetLampConfig(session.State);
        var dot = new Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = new SolidColorBrush(config.ActiveColor),
            Effect = CreateLampGlow(config.GlowColor, 10, 0.75),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(dot, 0);
        row.Children.Add(dot);

        var textPanel = new StackPanel();
        textPanel.Children.Add(new TextBlock
        {
            Text = GetSessionDisplayName(session),
            Foreground = System.Windows.Media.Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = $"{GetPathTail(session.Workspace)} · {FormatSessionTime(session)}",
            Foreground = new SolidColorBrush(MediaColor.FromRgb(168, 168, 176)),
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 3, 0, 0)
        });
        Grid.SetColumn(textPanel, 1);
        row.Children.Add(textPanel);

        var badge = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(GetStateBadgeBackground(session.State)),
            Padding = new Thickness(6, 3, 6, 3),
            Child = new TextBlock
            {
                Text = GetStateText(session.State),
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 10
            }
        };
        Grid.SetColumn(badge, 2);
        row.Children.Add(badge);

        var deleteButton = new System.Windows.Controls.Button
        {
            Width = 18,
            Height = 18,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(168, 168, 176)),
            Content = "x",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "删除任务"
        };
        deleteButton.Click += (_, _) => DeleteSession(session);
        Grid.SetColumn(deleteButton, 3);
        row.Children.Add(deleteButton);

        return row;
    }

    private void DeleteSession(SignalSession session)
    {
        var path = Path.Combine(_paths.SessionsDirectory, Sanitize(session.SessionId) + ".json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        _store.SaveSnapshot(_engine.BuildSnapshot(_store.LoadSessions()));
        RefreshView();
    }

    private void SetDrawerOpen(bool open)
    {
        SessionDrawer.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        Width = open ? 374 : 100;
    }

    private void SessionCountBadge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_visibleSessions.Count == 0)
        {
            return;
        }

        SetDrawerOpen(SessionDrawer.Visibility != Visibility.Visible);
    }

    private void Body_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string GetSessionDisplayName(SignalSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.DisplayName))
        {
            return session.DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(session.Workspace))
        {
            return Path.GetFileName(session.Workspace.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return string.IsNullOrWhiteSpace(session.SessionId) ? "AI Task" : session.SessionId;
    }

    private static string GetPathTail(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "-";
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetFileName(Path.GetDirectoryName(trimmed));
        var leaf = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(parent) ? leaf : $@"{parent}\{leaf}";
    }

    private static string FormatAge(DateTimeOffset updatedAt)
    {
        var age = DateTimeOffset.Now - updatedAt;
        return FormatDuration(age);
    }

    private static string FormatSessionTime(SignalSession session)
    {
        var startedAt = session.StartedAt > DateTimeOffset.MinValue ? session.StartedAt : session.UpdatedAt;
        var endAt = session.State is SignalSessionState.Completed or SignalSessionState.Idle or SignalSessionState.Failed
            ? session.UpdatedAt
            : DateTimeOffset.Now;
        var duration = endAt - startedAt;
        var prefix = session.State is SignalSessionState.Completed or SignalSessionState.Idle ? "用时" : "运行";
        return $"{prefix} {FormatDuration(duration)}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 60)
        {
            return $"{Math.Max(0, (int)duration.TotalSeconds)}s";
        }

        if (duration.TotalMinutes < 60)
        {
            return $"{(int)duration.TotalMinutes}m";
        }

        return $"{(int)duration.TotalHours}h";
    }

    private static string GetStateText(SignalSessionState state)
    {
        return state switch
        {
            SignalSessionState.Waiting => "等待",
            SignalSessionState.Thinking => "处理",
            SignalSessionState.Completed => "完成",
            SignalSessionState.Idle => "空闲",
            SignalSessionState.Failed => "失败",
            SignalSessionState.Stale => "过期",
            _ => "未知"
        };
    }

    private static MediaColor GetStateBadgeBackground(SignalSessionState state)
    {
        return state switch
        {
            SignalSessionState.Waiting => MediaColor.FromArgb(76, 255, 159, 10),
            SignalSessionState.Thinking => MediaColor.FromArgb(70, 255, 59, 48),
            SignalSessionState.Completed or SignalSessionState.Idle => MediaColor.FromArgb(70, 48, 209, 88),
            SignalSessionState.Failed => MediaColor.FromArgb(80, 255, 69, 58),
            _ => MediaColor.FromRgb(43, 44, 49)
        };
    }

    private static async Task<ProcessResult> RunPowerShellAsync(string scriptPath)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            }
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
    }

    private static string? FindToolScript(string scriptName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "tools", scriptName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            candidate = Path.Combine(current.FullName, scriptName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string GetHooksJsonPath()
    {
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(codexHome))
        {
            codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        }

        return Path.Combine(codexHome, "hooks.json");
    }

    private static string FirstLine(params string[] values)
    {
        foreach (var value in values)
        {
            var line = value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        return "unknown error";
    }

    private static void CopyIfExists(string source, string destination)
    {
        if (!File.Exists(source))
        {
            return;
        }

        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(source, destination, overwrite: true);
    }

    private static void CopyDirectoryIfExists(string source, string destination, string? filePrefix = null)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        Directory.CreateDirectory(destination);
        foreach (var path in Directory.EnumerateFiles(source, "*.json"))
        {
            var fileName = Path.GetFileName(path);
            if (filePrefix is not null && !fileName.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Copy(path, Path.Combine(destination, fileName), overwrite: true);
        }
    }

    private static string Sanitize(string value)
    {
        var chars = value.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray();
        return chars.Length == 0 ? "unknown" : new string(chars);
    }

    private static void ShowInfo(string title, string message)
    {
        System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private sealed record LampVisualConfig(MediaColor ActiveColor, MediaColor DimColor, MediaColor GlowColor);

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
