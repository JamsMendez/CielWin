using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CielWin.App.Wallpaper;

namespace CielWin.App.Tests.Wallpaper;

/// <summary>
/// The pure parts of <see cref="SceneWebServer"/>: how a shared scene page is adapted to WebView2 and which
/// files the scene host may serve. The WebView2 wiring itself is source-guarded in
/// <c>WebViewAlertLayerControllerTests</c> and <see cref="WebView2MiniSceneBrowserSourceGuardTests"/>.
/// </summary>
public sealed class SceneWebServerTests
{
    private const string QrcPolicy =
        "default-src 'none'; script-src qrc:; style-src qrc:; font-src qrc:; connect-src 'none'; base-uri 'none'; form-action 'none'; frame-src 'none'";

    private static readonly string Page =
        "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\" />\n" +
        $"<meta http-equiv=\"Content-Security-Policy\" content=\"{QrcPolicy}\" />\n" +
        "<link rel=\"stylesheet\" href=\"styles.css\" />\n</head>\n<body>\n<canvas id=\"scene\"></canvas>\n" +
        "<script src=\"js/main.js\"></script>\n</body>\n</html>\n";

    private static readonly string WebDirectory = Path.Combine(AppContext.BaseDirectory, "Wallpaper", "Web");

    [Fact]
    public void Rewrite_MapsEveryQrcSourceToSelf_AndKeepsTheOtherDirectives()
    {
        var policy = Policy(SceneWebServer.RewriteSceneDocument(Page));

        Assert.DoesNotContain("qrc:", policy);
        Assert.StartsWith("script-src 'self' ", Directive(policy, "script-src"));
        Assert.StartsWith("style-src 'self' ", Directive(policy, "style-src"));
        Assert.Equal("font-src 'self'", Directive(policy, "font-src"));
        Assert.Equal("default-src 'none'", Directive(policy, "default-src"));
        Assert.Equal("connect-src 'none'", Directive(policy, "connect-src"));
        Assert.Equal("frame-src 'none'", Directive(policy, "frame-src"));
    }

    [Fact]
    public void Rewrite_InjectsTheHostStyleInHead_AndAllowsExactlyItByHash()
    {
        var html = SceneWebServer.RewriteSceneDocument(Page);
        var style = $"<style>{SceneWebServer.HostStyle}</style>";

        Assert.Contains(style + "</head>", html);
        Assert.Equal($"style-src 'self' {Sha256Source(SceneWebServer.HostStyle)}", Directive(Policy(html), "style-src"));
        Assert.Contains("html.scene-mini", SceneWebServer.HostStyle);
        Assert.Contains("transparent", SceneWebServer.HostStyle);
    }

    [Fact]
    public void Rewrite_InjectsTheHostScriptAfterTheSceneScripts_AndAllowsExactlyItByHash()
    {
        var html = SceneWebServer.RewriteSceneDocument(Page);
        var script = $"<script>{SceneWebServer.HostScript}</script>";

        Assert.Contains(script + "</body>", html);
        Assert.True(html.IndexOf("js/main.js", StringComparison.Ordinal) < html.IndexOf(script, StringComparison.Ordinal));
        Assert.Equal($"script-src 'self' {Sha256Source(SceneWebServer.HostScript)}", Directive(Policy(html), "script-src"));
    }

    [Fact]
    public void Rewrite_LeavesEverythingOutsideThePolicyAndTheTwoInsertionsUntouched()
    {
        var html = SceneWebServer.RewriteSceneDocument(Page);
        var restored = html
            .Replace($"<style>{SceneWebServer.HostStyle}</style>", "")
            .Replace($"<script>{SceneWebServer.HostScript}</script>", "");
        restored = Regex.Replace(restored, "content=\"default-src[^\"]*\"", $"content=\"{QrcPolicy}\"");

        Assert.Equal(Page, restored);
    }

    [Fact]
    public void Rewrite_APageWithoutAPolicyStillGetsTheHostAdaptations()
    {
        var html = SceneWebServer.RewriteSceneDocument("<html><head></head><body></body></html>");

        Assert.Equal(
            $"<html><head><style>{SceneWebServer.HostStyle}</style></head><body><script>{SceneWebServer.HostScript}</script></body></html>",
            html);
    }

    [Theory]
    [InlineData("processing")]
    [InlineData("explorer")]
    [InlineData("idle")]
    [InlineData("raphael")]
    public void Rewrite_EveryShippedScenePageEndsUpWithASelfOnlyPolicy(string scene)
    {
        var path = Path.Combine(WebDirectory, scene, "index.html");
        Assert.True(File.Exists(path), $"Expected the shipped '{path}' (CielScenes submodule; clone with --recurse-submodules).");

        var html = SceneWebServer.RewriteSceneDocument(File.ReadAllText(path));
        var policy = Policy(html);

        Assert.DoesNotContain("qrc:", html);
        Assert.Contains("script-src 'self' 'sha256-", policy);
        Assert.Contains("style-src 'self' 'sha256-", policy);
        Assert.Contains($"<script>{SceneWebServer.HostScript}</script></body>", html);
    }

    [Fact]
    public void HostStyle_OverridesTheSharedOpaqueMiniRoot()
    {
        // The shared stylesheets paint the mini root black for CieLinux's luminance key; the host style must
        // be the rule that undoes exactly that on every scene.
        foreach (var scene in new[] { "processing", "explorer", "idle", "raphael" })
        {
            var css = File.ReadAllText(Path.Combine(WebDirectory, scene, "styles.css"));
            Assert.Contains("html.scene-mini", css);
        }

        Assert.Equal("html.scene-mini{background:transparent!important}", SceneWebServer.HostStyle);
    }

    [Fact]
    public void HostScript_RestoresTheUnkeyedLetterColorsTheSharedOverlayDefines()
    {
        var overlay = File.ReadAllText(Path.Combine(WebDirectory, "shared", "js", "alert-overlay.js"));

        Assert.Contains("const FAILURE_OVERLAY_THEMES", overlay);
        Assert.Contains(".keyedLetters", overlay);
        Assert.Contains("t[k].keyedLetters=t[k].letters", SceneWebServer.HostScript);
    }

    [Theory]
    [InlineData("https://cielwin-scene.example/processing/index.html?fps=60&variant=mini", "processing/index.html")]
    [InlineData("https://cielwin-scene.example/explorer/js/render-loop.js", "explorer/js/render-loop.js")]
    [InlineData("https://CIELWIN-SCENE.example/shared/fonts/ArchivoBlack-Regular.ttf", "shared/fonts/ArchivoBlack-Regular.ttf")]
    [InlineData("https://cielwin-scene.example/idle/index.html#tiles=failed", "idle/index.html")]
    public void ResolveFilePath_MapsSceneUrlsUnderTheRoot(string uri, string relative)
    {
        var root = Path.Combine(Path.GetTempPath(), "scene-root");

        Assert.Equal(
            Path.GetFullPath(Path.Combine([root, .. relative.Split('/')])),
            SceneWebServer.ResolveFilePath(root, uri));
    }

    [Theory]
    [InlineData("http://cielwin-scene.example/processing/index.html")]
    [InlineData("https://example.com/processing/index.html")]
    [InlineData("https://cielwin-scene.example/")]
    [InlineData("https://cielwin-scene.example/processing%2F..%2F..%2Fsecret.html")]
    [InlineData("https://cielwin-scene.example/processing%5C..%5Csecret.html")]
    [InlineData("https://cielwin-scene.example/.git")]
    [InlineData("https://cielwin-scene.example/.git/config.js")]
    [InlineData("https://cielwin-scene.example/README.md")]
    [InlineData("https://cielwin-scene.example/processing/C:secret.js")]
    [InlineData("not a uri")]
    public void ResolveFilePath_RefusesAnythingThatIsNotAPlainSceneFile(string uri)
    {
        Assert.Null(SceneWebServer.ResolveFilePath(Path.Combine(Path.GetTempPath(), "scene-root"), uri));
    }

    /// <summary>
    /// Dot segments, plain or percent-encoded, are collapsed by <see cref="Uri"/> before the path is read, so
    /// they can only ever land back inside the root.
    /// </summary>
    [Theory]
    [InlineData("https://cielwin-scene.example/../secret.html")]
    [InlineData("https://cielwin-scene.example/processing/%2E%2E/%2E%2E/secret.html")]
    [InlineData("https://cielwin-scene.example/processing/../../../../secret.js")]
    public void ResolveFilePath_NeverEscapesTheRoot(string uri)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "scene-root"));
        var resolved = SceneWebServer.ResolveFilePath(root, uri);

        Assert.True(resolved is null || resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            $"'{uri}' resolved outside the root: '{resolved}'.");
    }

    [Theory]
    [InlineData("index.html", "text/html; charset=utf-8")]
    [InlineData("main.js", "text/javascript; charset=utf-8")]
    [InlineData("styles.css", "text/css; charset=utf-8")]
    [InlineData("ArchivoBlack-Regular.ttf", "font/ttf")]
    public void ContentTypeFor_NamesTheServedTypes(string file, string expected)
    {
        Assert.Equal(expected, SceneWebServer.ContentTypeFor(file));
    }

    private static string Policy(string html)
    {
        var match = Regex.Match(html, "http-equiv=\"Content-Security-Policy\"\\s+content=\"([^\"]*)\"");
        Assert.True(match.Success, "Expected the page to keep its Content-Security-Policy meta.");
        return match.Groups[1].Value;
    }

    private static string Directive(string policy, string name) =>
        policy.Split(';', StringSplitOptions.TrimEntries).Single(d => d == name || d.StartsWith(name + " ", StringComparison.Ordinal));

    private static string Sha256Source(string content) =>
        $"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(content)))}'";
}
