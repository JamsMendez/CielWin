using CielWin.App.Alerts;
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
        Assert.Equal(43811, settings.HttpServerPort);
        Assert.Equal(WallpaperScene.Processing, settings.WallpaperScene);
        Assert.Equal(MiniPosition.TopRight, settings.MiniPosition);
        Assert.True(settings.AlertSoundsEnabled);
    }

    [Theory]
    [InlineData("alert-sounds = off", false)]
    [InlineData("alert-sounds = 0", false)]
    [InlineData("ALERT-SOUNDS = On", true)]
    [InlineData("alert-sounds = true", true)]
    public void AlertSoundsIsRead_AsAFlag(string line, bool expected)
    {
        Assert.Equal(expected, Settings.Parse(line).AlertSoundsEnabled);
    }

    [Theory]
    [InlineData("alert-sounds = muted")]
    [InlineData("alert-sounds =")]
    public void AnUnreadableAlertSoundsValue_KeepsSoundsOn(string line)
    {
        Assert.True(Settings.Parse(line).AlertSoundsEnabled);
    }

    [Fact]
    public void AFileWrittenBeforeAlertSoundsExisted_LoadsWithSoundsOn()
    {
        var older = """
            wallpaper-mode = scene-mini
            http-server = on
            http-server-port = 47811
            scene = idle
            mini-position = bottom-left
            """;

        var settings = Settings.Parse(older);

        Assert.True(settings.AlertSoundsEnabled);
        Assert.Equal(WallpaperScene.Idle, settings.WallpaperScene);
    }

    [Theory]
    [InlineData(true, "alert-sounds = on")]
    [InlineData(false, "alert-sounds = off")]
    public void Serialize_WritesAlertSounds_AndRoundTrips(bool enabled, string expectedLine)
    {
        var original = Settings.Default with { AlertSoundsEnabled = enabled };
        var text = original.Serialize();

        Assert.Contains(expectedLine, text.Split('\n').Select(line => line.Trim()));
        Assert.Equal(original, Settings.Parse(text));
    }

    [Fact]
    public void AFileWrittenBeforeImportedSoundsExisted_LoadsWithNoSound()
    {
        var older = """
            scene = idle
            alert-sounds = on
            """;

        var settings = Settings.Parse(older);

        Assert.Null(settings.FailedSound);
        Assert.Null(settings.WarningSound);
        Assert.Null(settings.SoundFor(AlertKind.Failed));
        Assert.False(settings.HasAnySound);
    }

    [Theory]
    [InlineData("failed-sound = failed.m4a", "failed.m4a", null)]
    [InlineData("WARNING-SOUND = Warning.WAV", null, "Warning.WAV")]
    [InlineData("failed-sound = failed.mp3\nwarning-sound = warning.wav", "failed.mp3", "warning.wav")]
    public void ImportedSoundsAreRead_PerKind(string content, string? failed, string? warning)
    {
        var settings = Settings.Parse(content);

        Assert.Equal(failed, settings.SoundFor(AlertKind.Failed));
        Assert.Equal(warning, settings.SoundFor(AlertKind.Warning));
    }

    [Theory]
    [InlineData(@"failed-sound = ..\..\secret.wav")]
    [InlineData(@"failed-sound = C:\Windows\Media\chord.wav")]
    [InlineData("failed-sound = failed.ogg")]
    [InlineData("failed-sound =")]
    public void AnUnusableOrEmptySoundValue_MeansNoSound(string line)
    {
        Assert.Null(Settings.Parse(line).FailedSound);
    }

    [Fact]
    public void AnEmptySoundLine_ClearsAnEarlierOne_TheLastAssignmentWins()
    {
        Assert.Null(Settings.Parse("warning-sound = warning.wav\nwarning-sound =").WarningSound);
    }

    [Theory]
    [InlineData("failed.m4a", null)]
    [InlineData(null, "warning.mp3")]
    [InlineData("failed.wav", "warning.wav")]
    [InlineData(null, null)]
    public void Serialize_WritesTheImportedSounds_AndRoundTrips(string? failed, string? warning)
    {
        var original = Settings.Default with { FailedSound = failed, WarningSound = warning };
        var lines = original.Serialize().Split('\n').Select(line => line.Trim()).ToArray();

        Assert.Contains(failed is null ? "failed-sound =" : $"failed-sound = {failed}", lines);
        Assert.Contains(warning is null ? "warning-sound =" : $"warning-sound = {warning}", lines);
        Assert.Equal(original, Settings.Parse(original.Serialize()));
    }

    [Fact]
    public void WithSound_SetsAndClearsOneKind_AndHasAnySoundFollows()
    {
        var failedOnly = Settings.Default.WithSound(AlertKind.Failed, "failed.wav");

        Assert.Equal("failed.wav", failedOnly.FailedSound);
        Assert.Null(failedOnly.WarningSound);
        Assert.True(failedOnly.HasAnySound);
        Assert.False(failedOnly.WithSound(AlertKind.Failed, null).HasAnySound);
        Assert.Equal("warning.mp3", failedOnly.WithSound(AlertKind.Warning, "warning.mp3").SoundFor(AlertKind.Warning));
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
    public void AlertHoldMaxSeconds_DefaultsToTenMinutes() =>
        Assert.Equal(600, Settings.Parse(string.Empty).AlertHoldMaxSeconds);

    [Theory]
    [InlineData("alert-hold-max-seconds = 10", 10)]
    [InlineData("alert-hold-max-seconds=3600", 3600)]
    [InlineData("  ALERT-HOLD-MAX-SECONDS  =  90 ", 90)]
    public void AlertHoldMaxSecondsIsRead_HoweverTheLineIsSpelled(string line, int expected)
    {
        Assert.Equal(expected, Settings.Parse(line).AlertHoldMaxSeconds);
    }

    [Theory]
    [InlineData("alert-hold-max-seconds = 9")]
    [InlineData("alert-hold-max-seconds = 3601")]
    [InlineData("alert-hold-max-seconds = -60")]
    [InlineData("alert-hold-max-seconds = 1.5")]
    [InlineData("alert-hold-max-seconds = 10m")]
    [InlineData("alert-hold-max-seconds =")]
    public void AnInvalidAlertHoldMaxSeconds_KeepsTheDefault(string line)
    {
        Assert.Equal(600, Settings.Parse(line).AlertHoldMaxSeconds);
    }

    [Fact]
    public void FrameRate_DefaultsToSixty() =>
        Assert.Equal(60, Settings.Parse(string.Empty).FrameRate);

    [Theory]
    [InlineData("frame-rate = 30", 30)]
    [InlineData("frame-rate=60", 60)]
    [InlineData("  FRAME-RATE  =  30 ", 30)]
    public void FrameRateIsRead_HoweverTheLineIsSpelled(string line, int expected)
    {
        Assert.Equal(expected, Settings.Parse(line).FrameRate);
    }

    [Theory]
    [InlineData("frame-rate = 0")]
    [InlineData("frame-rate = 45")]
    [InlineData("frame-rate = 120")]
    [InlineData("frame-rate = -30")]
    [InlineData("frame-rate = 30.0")]
    [InlineData("frame-rate = 30fps")]
    [InlineData("frame-rate =")]
    public void AnInvalidFrameRate_KeepsTheDefault(string line)
    {
        Assert.Equal(60, Settings.Parse(line).FrameRate);
    }

    [Fact]
    public void AnInvalidFrameRate_KeepsAnEarlierValidOne()
    {
        Assert.Equal(30, Settings.Parse("frame-rate = 30\nframe-rate = 45\n").FrameRate);
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(60, true)]
    [InlineData(0, false)]
    [InlineData(59, false)]
    [InlineData(120, false)]
    public void OnlyThirtyAndSixtyAreFrameRates(int fps, bool expected)
    {
        Assert.Equal(expected, Settings.IsFrameRate(fps));
        Assert.Equal([30, 60], Settings.FrameRates);
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
            MiniPosition: MiniPosition.LeftCenter,
            AlertSoundsEnabled: false,
            AlertHoldMaxSeconds: 90,
            FrameRate: 30);

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

        Assert.Equal(["wallpaper-mode", "http-server", "http-server-port", "scene", "frame-rate", "mini-position", "alert-sounds", "failed-sound", "warning-sound", "alert-hold-max-seconds"], keys);
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

    [Fact]
    public void Serialize_NamesTheTokenFileTheServerActuallyUses()
    {
        var tokenFile = Path.GetFileName(CielWin.App.Alerts.AlertHttpTokenFile.ResolvePath());

        Assert.Contains($@"%LOCALAPPDATA%\CielWin\{tokenFile}", Settings.Default.Serialize(), StringComparison.Ordinal);
    }
}
