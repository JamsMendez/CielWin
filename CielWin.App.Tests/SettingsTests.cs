using CielWin.Interop;

namespace CielWin.App.Tests;

/// <summary>The settings file's text format, tested as a pure function with no disk involved.</summary>
public sealed class SettingsTests
{
    [Fact]
    public void EmptyContent_LeavesEveryDefaultInPlace()
    {
        var settings = Settings.Parse(string.Empty);

        Assert.Equal(Settings.Default, settings);
        Assert.Equal(WallpaperMode.Scene, settings.WallpaperMode);
        Assert.True(settings.HttpServerEnabled);
        Assert.Equal(AlertHttpProtocol.DefaultPort, settings.HttpServerPort);
        Assert.Equal(47811, settings.HttpServerPort);
        Assert.Equal(WallpaperScene.Processing, settings.WallpaperScene);
        Assert.Equal(MiniPosition.TopRight, settings.MiniPosition);
    }

    [Theory]
    [InlineData("wallpaper-mode = scene", WallpaperMode.Scene)]
    [InlineData("wallpaper-mode=scene-mini", WallpaperMode.SceneMini)]
    [InlineData("  WALLPAPER-MODE   =   Scene-Mini  ", WallpaperMode.SceneMini)]
    [InlineData("wallpaper-mode = SCENE", WallpaperMode.Scene)]
    public void WallpaperModeIsRead_HoweverTheLineIsSpelled(string line, WallpaperMode expected)
    {
        Assert.Equal(expected, Settings.Parse(line).WallpaperMode);
    }

    [Theory]
    [InlineData("wallpaper-mode = html", WallpaperMode.Scene)]
    [InlineData("wallpaper-mode = HTML", WallpaperMode.Scene)]
    [InlineData("wallpaper-mode = html-mini", WallpaperMode.SceneMini)]
    [InlineData("wallpaper-mode=mini", WallpaperMode.SceneMini)]
    public void LegacyCosmicWinModeNames_AreStillRead(string line, WallpaperMode expected)
    {
        Assert.Equal(expected, Settings.Parse(line).WallpaperMode);
    }

    [Theory]
    [InlineData("wallpaper-mode = video")]
    [InlineData("wallpaper-mode = blue")]
    [InlineData("wallpaper-mode =")]
    [InlineData("wallpaper-mode")]
    public void AnUnreadableOrRemovedWallpaperMode_KeepsTheDefault(string line)
    {
        Assert.Equal(WallpaperMode.Scene, Settings.Parse(line).WallpaperMode);
    }

    [Fact]
    public void AnUnreadableModeValue_DoesNotOverrideAnEarlierValidOne()
    {
        var content = "wallpaper-mode = scene-mini\nwallpaper-mode = video\n";

        Assert.Equal(WallpaperMode.SceneMini, Settings.Parse(content).WallpaperMode);
    }

    [Theory]
    [InlineData(WallpaperMode.Scene, "wallpaper-mode = scene")]
    [InlineData(WallpaperMode.SceneMini, "wallpaper-mode = scene-mini")]
    public void Serialize_WritesTheNewModeNames_AndRoundTrips(WallpaperMode mode, string expectedLine)
    {
        var original = Settings.Default with { WallpaperMode = mode };
        var text = original.Serialize();

        Assert.Contains(expectedLine, text, StringComparison.Ordinal);
        Assert.DoesNotContain("html", text, StringComparison.Ordinal);
        Assert.Equal(original, Settings.Parse(text));
    }

    [Theory]
    [InlineData("http-server = off")]
    [InlineData("http-server=false")]
    [InlineData("HTTP-SERVER = 0")]
    public void HttpServerIsTurnedOff_HoweverTheLineIsSpelled(string line)
    {
        Assert.False(Settings.Parse(line).HttpServerEnabled);
    }

    [Theory]
    [InlineData("http-server = on")]
    [InlineData("http-server=true")]
    [InlineData("  HTTP-SERVER  =  1 ")]
    public void HttpServerIsTurnedOn_HoweverTheLineIsSpelled(string line)
    {
        Assert.True(Settings.Parse(line).HttpServerEnabled);
    }

    [Theory]
    [InlineData("http-server = perhaps")]
    [InlineData("http-server =")]
    [InlineData("http-server")]
    public void AnUnreadableHttpServerValue_KeepsTheDefault(string line)
    {
        Assert.True(Settings.Parse(line).HttpServerEnabled);
    }

    [Fact]
    public void HttpServerOff_IsTheOnlySwitch_ThereIsNoSeparateAlertsKey()
    {
        var settings = Settings.Parse("http-server = off\nalerts = on\n");

        Assert.False(settings.HttpServerEnabled);
        Assert.DoesNotContain("alerts =", Settings.Default.Serialize(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http-server-port = 8080", 8080)]
    [InlineData("http-server-port=1", 1)]
    [InlineData("  HTTP-SERVER-PORT  =  65535 ", 65535)]
    public void HttpServerPortIsRead_HoweverTheLineIsSpelled(string line, int expected)
    {
        Assert.Equal(expected, Settings.Parse(line).HttpServerPort);
    }

    [Theory]
    [InlineData("http-server-port = 0")]
    [InlineData("http-server-port = 65536")]
    [InlineData("http-server-port = -5")]
    [InlineData("http-server-port = abc")]
    [InlineData("http-server-port =")]
    public void AnInvalidHttpServerPort_KeepsTheDefault(string line)
    {
        Assert.Equal(AlertHttpProtocol.DefaultPort, Settings.Parse(line).HttpServerPort);
    }

    [Fact]
    public void LegacyAlertHttpKeys_AreStillRead_ButTheNewKeysWinInEitherOrder()
    {
        var legacy = Settings.Parse("alert-http = off\nalert-http-port = 9000\n");
        Assert.False(legacy.HttpServerEnabled);
        Assert.Equal(9000, legacy.HttpServerPort);

        Assert.True(Settings.Parse("http-server = on\nalert-http = off\n").HttpServerEnabled);
        Assert.True(Settings.Parse("alert-http = off\nhttp-server = on\n").HttpServerEnabled);
        Assert.Equal(8000, Settings.Parse("alert-http-port = 9000\nhttp-server-port = 8000\n").HttpServerPort);
        Assert.Equal(8000, Settings.Parse("http-server-port = 8000\nalert-http-port = 9000\n").HttpServerPort);
    }

    [Theory]
    [InlineData("scene = processing", WallpaperScene.Processing)]
    [InlineData("scene=explorer", WallpaperScene.Explorer)]
    [InlineData("  SCENE  =  Idle ", WallpaperScene.Idle)]
    [InlineData("scene = raphael", WallpaperScene.Raphael)]
    [InlineData("wallpaper-scene = raphael", WallpaperScene.Raphael)]
    [InlineData("WALLPAPER-SCENE = Explorer", WallpaperScene.Explorer)]
    public void SceneIsRead_FromTheNewKeyAndTheLegacyAlias(string line, WallpaperScene expected)
    {
        Assert.Equal(expected, Settings.Parse(line).WallpaperScene);
    }

    [Theory]
    [InlineData("scene = sunset")]
    [InlineData("scene =")]
    [InlineData("scene")]
    [InlineData("wallpaper-scene = sunset")]
    public void AnUnreadableScene_KeepsTheDefault(string line)
    {
        Assert.Equal(WallpaperScene.Processing, Settings.Parse(line).WallpaperScene);
    }

    [Fact]
    public void TheNewSceneKeyWins_InEitherLineOrder()
    {
        Assert.Equal(WallpaperScene.Idle, Settings.Parse("scene = idle\nwallpaper-scene = explorer\n").WallpaperScene);
        Assert.Equal(WallpaperScene.Idle, Settings.Parse("wallpaper-scene = explorer\nscene = idle\n").WallpaperScene);
    }

    [Theory]
    [InlineData("top-left", MiniPosition.TopLeft)]
    [InlineData("top-center", MiniPosition.TopCenter)]
    [InlineData("top-right", MiniPosition.TopRight)]
    [InlineData("right-center", MiniPosition.RightCenter)]
    [InlineData("bottom-right", MiniPosition.BottomRight)]
    [InlineData("bottom-center", MiniPosition.BottomCenter)]
    [InlineData("bottom-left", MiniPosition.BottomLeft)]
    [InlineData("left-center", MiniPosition.LeftCenter)]
    public void MiniPositionIsRead_AndLegacyMiniCornerAliasToo(string value, MiniPosition expected)
    {
        Assert.Equal(expected, Settings.Parse($"mini-position = {value}").MiniPosition);
        Assert.Equal(expected, Settings.Parse($"  MINI-POSITION=  {value.ToUpperInvariant()} ").MiniPosition);
        Assert.Equal(expected, Settings.Parse($"mini-corner = {value}").MiniPosition);
    }

    [Theory]
    [InlineData("mini-position = middle")]
    [InlineData("mini-position =")]
    [InlineData("mini-corner = middle")]
    public void AnUnreadableMiniPosition_KeepsTheDefault(string line)
    {
        Assert.Equal(MiniPosition.TopRight, Settings.Parse(line).MiniPosition);
    }

    [Fact]
    public void TheNewMiniPositionKeyWins_InEitherLineOrder()
    {
        Assert.Equal(MiniPosition.TopLeft, Settings.Parse("mini-position = top-left\nmini-corner = bottom-right\n").MiniPosition);
        Assert.Equal(MiniPosition.TopLeft, Settings.Parse("mini-corner = bottom-right\nmini-position = top-left\n").MiniPosition);
    }

    [Fact]
    public void MiniPositions_AreOrderedClockwise()
    {
        Assert.Equal(
            [MiniPosition.TopLeft, MiniPosition.TopCenter, MiniPosition.TopRight, MiniPosition.RightCenter,
             MiniPosition.BottomRight, MiniPosition.BottomCenter, MiniPosition.BottomLeft, MiniPosition.LeftCenter],
            Enum.GetValues<MiniPosition>());
    }

    [Fact]
    public void CommentsBlankLinesAndUnknownKeys_AreIgnored()
    {
        var content = """
            # a comment
            unknown-key = whatever

            this line has no separator
            http-server-port = 9000
            """;

        Assert.Equal(Settings.Default with { HttpServerPort = 9000 }, Settings.Parse(content));
    }

    [Fact]
    public void RemovedCosmicWinKeys_AreIgnoredWithoutError()
    {
        var content = """
            focus-border = off
            border-color = #FF8800
            tiling = off
            gap = 12
            video-wallpaper-path = C:\clips\a.mp4
            wallpaper-fps = 30
            alerts = off
            alerts-enabled = off
            video-wallpaper-http = off
            wallpaper-scene-http = off
            """;

        Assert.Equal(Settings.Default, Settings.Parse(content));
    }

    [Fact]
    public void TheLastAssignmentWins()
    {
        Assert.Equal(
            WallpaperMode.SceneMini,
            Settings.Parse("wallpaper-mode = scene\nwallpaper-mode = scene-mini\n").WallpaperMode);
    }

    [Fact]
    public void CarriageReturnsAreNotPartOfTheValue()
    {
        var settings = Settings.Parse("http-server = off\r\nwallpaper-mode = scene-mini\r\n");

        Assert.False(settings.HttpServerEnabled);
        Assert.Equal(WallpaperMode.SceneMini, settings.WallpaperMode);
    }

    [Fact]
    public void SerializeThenParse_RoundTripsEveryField()
    {
        var original = new Settings(
            HttpServerEnabled: false,
            HttpServerPort: 12345,
            WallpaperMode: WallpaperMode.SceneMini,
            WallpaperScene: WallpaperScene.Raphael,
            MiniPosition: MiniPosition.LeftCenter);

        Assert.Equal(original, Settings.Parse(original.Serialize()));
    }

    [Theory]
    [InlineData(WallpaperScene.Processing)]
    [InlineData(WallpaperScene.Explorer)]
    [InlineData(WallpaperScene.Idle)]
    [InlineData(WallpaperScene.Raphael)]
    public void SerializeThenParse_RoundTripsEveryScene(WallpaperScene scene)
    {
        var original = Settings.Default with { WallpaperScene = scene };

        Assert.Equal(original, Settings.Parse(original.Serialize()));
    }

    [Fact]
    public void SerializeThenParse_RoundTripsEveryMiniPosition()
    {
        foreach (var position in Enum.GetValues<MiniPosition>())
        {
            var original = Settings.Default with { MiniPosition = position };

            Assert.Equal(original, Settings.Parse(original.Serialize()));
        }
    }

    [Fact]
    public void Serialize_WritesOnlyTheKeptKeys_WithComments()
    {
        var text = Settings.Default.Serialize();

        var keys = text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line[..line.IndexOf('=')].Trim())
            .ToArray();

        Assert.Equal(["wallpaper-mode", "http-server", "http-server-port", "scene", "mini-position"], keys);
        Assert.StartsWith("# CielWin settings", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CosmicWin", text, StringComparison.Ordinal);
        Assert.DoesNotContain("wallpaper-scene", text, StringComparison.Ordinal);
        Assert.DoesNotContain("mini-corner", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ALegacyCosmicWinFile_ParsesToTheEquivalentCielWinSettings()
    {
        var legacy = """
            focus-border = on
            tiling = on
            alerts = on
            alert-http = on
            alert-http-port = 5000
            gap = 8
            wallpaper-mode = html-mini
            wallpaper-scene = idle
            wallpaper-fps = 60
            mini-corner = bottom-left
            """;

        var settings = Settings.Parse(legacy);

        Assert.Equal(
            new Settings(true, 5000, WallpaperMode.SceneMini, WallpaperScene.Idle, MiniPosition.BottomLeft),
            settings);
        Assert.Equal(settings, Settings.Parse(settings.Serialize()));
    }
}
