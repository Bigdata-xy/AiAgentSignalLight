using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SignalLight.Core.Sessions;
using SignalLight.Storage;
using SignalLight.Storage.Json;

namespace SignalLight.App;

public partial class MainWindow : Window
{
    private readonly SignalLightPaths _paths = new();
    private readonly JsonSignalStore _store;
    private readonly FileSystemWatcher _snapshotWatcher;
    private readonly DispatcherTimer _refreshTimer;

    public MainWindow()
    {
        InitializeComponent();

        _store = new JsonSignalStore(_paths);
        _paths.EnsureAll();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            ApplySnapshot();
        };

        _snapshotWatcher = new FileSystemWatcher(_paths.RootDirectory, Path.GetFileName(_paths.SnapshotPath))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _snapshotWatcher.Changed += OnSnapshotChanged;
        _snapshotWatcher.Created += OnSnapshotChanged;
        _snapshotWatcher.Renamed += OnSnapshotChanged;

        Closed += (_, _) => _snapshotWatcher.Dispose();

        ApplySnapshot();
    }

    private void OnSnapshotChanged(object sender, FileSystemEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _refreshTimer.Stop();
            _refreshTimer.Start();
        });
    }

    private void ApplySnapshot()
    {
        var snapshot = _store.LoadSnapshot();
        ApplyState(snapshot.AggregateState);
        ProgressText.Text = $"{snapshot.CompletedCount}/{snapshot.TotalCount}";
    }

    private void ApplyState(SignalSessionState state)
    {
        RedLamp.Fill = new SolidColorBrush(state == SignalSessionState.Thinking ? Color.FromRgb(255, 59, 48) : Color.FromRgb(74, 21, 21));
        YellowLamp.Fill = new SolidColorBrush(state == SignalSessionState.Waiting ? Color.FromRgb(255, 159, 10) : Color.FromRgb(74, 53, 16));
        GreenLamp.Fill = new SolidColorBrush(state is SignalSessionState.Completed or SignalSessionState.Idle ? Color.FromRgb(48, 209, 88) : Color.FromRgb(18, 63, 35));
    }
}
