using SignalLight.Bridge;
using SignalLight.Storage;
using Xunit;

namespace SignalLight.Bridge.Tests;

public sealed class RemoteBridgeSettingsTests
{
    [Fact]
    public void Settings_are_created_with_token_when_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "SignalLightBridgeSettingsTests", Guid.NewGuid().ToString("N"));
        try
        {
            var settings = RemoteBridgeSettings.LoadOrCreate(new Dictionary<string, string>
            {
                ["root"] = root,
                ["port"] = "37632"
            });

            var path = Path.Combine(new SignalLightPaths(root).RootDirectory, "remote-bridge.json");
            Assert.Equal("127.0.0.1", settings.Bind);
            Assert.Equal(37632, settings.Port);
            Assert.False(string.IsNullOrWhiteSpace(settings.Token));
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
