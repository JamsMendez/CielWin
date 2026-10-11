using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace CielWin.App.Wallpaper;

/// <summary>
/// Serves the shared scene pages (the <c>CielScenes</c> submodule at <c>Wallpaper\Web</c>) to a WebView2
/// under <c>https://cielwin-scene.example/</c>, adapting them to this host on the way out.
/// </summary>
/// <remarks>
/// The scenes are written for CieLinux first, and every platform difference stays in the host:
/// <list type="bullet">
/// <item>Each <c>index.html</c> carries a CSP whose sources are <c>qrc:</c> (Qt's resource scheme). Served
/// as-is it would block every script, style and font here, so <see cref="RewriteSceneDocument"/> maps
/// <c>qrc:</c> to <c>'self'</c> (the same rewrite CielDroid applies).</item>
/// <item>The mini variant's root is opaque black for CieLinux's luminance key. The CielWin mini window is
/// alpha-composited instead, so a host stylesheet makes that root transparent again.</item>
/// <item>Under the luminance key the alert letters are pre-brightened (<c>keyedLetters</c>). With real alpha
/// that would only make them brighter than designed, so a host script restores the unkeyed colors.</item>
/// </list>
/// The pages are served through <c>WebResourceRequested</c> rather than
/// <c>SetVirtualHostNameToFolderMapping</c> because WebView2 never raises <c>WebResourceRequested</c> for
/// a virtual-host-mapped URL, so a mapped page could not be rewritten.
/// </remarks>
internal static class SceneWebServer
{
    /// <summary>The scene origin's host, an RFC 6761 reserved name (never <c>.local</c>).</summary>
    public const string HostName = "cielwin-scene.example";

    /// <summary>Makes the mini variant's luminance-key root transparent (CielWin composites real alpha).</summary>
    internal const string HostStyle = "html.scene-mini{background:transparent!important}";

    /// <summary>Restores the unkeyed alert letter colors; runs after every scene script, before any frame.</summary>
    internal const string HostScript =
        "(function(){try{var t=FAILURE_OVERLAY_THEMES;for(var k in t){if(t[k]&&t[k].letters){t[k].keyedLetters=t[k].letters;}}}catch(_){}})();";

    private static readonly Regex CspMeta = new(
        "(<meta\\s+http-equiv=\"Content-Security-Policy\"\\s+content=\")([^\"]*)(\")",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex QrcSource = new("(?<![\\w-])qrc:", RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".png"] = "image/png",
        [".svg"] = "image/svg+xml",
    };

    /// <summary>The scene files' folder next to the executable (the submodule's build output).</summary>
    public static string WebRoot => Path.Combine(AppContext.BaseDirectory, "Wallpaper", "Web");

    /// <summary>
    /// Routes every request to the scene host through this server. Call once per CoreWebView2, before
    /// the first navigation.
    /// </summary>
    public static void Attach(CoreWebView2 webView, string webRoot)
    {
        webView.AddWebResourceRequestedFilter($"https://{HostName}/*", CoreWebView2WebResourceContext.All);
        webView.WebResourceRequested += (_, args) => Respond(webView, webRoot, args);
    }

    private static void Respond(CoreWebView2 webView, string webRoot, CoreWebView2WebResourceRequestedEventArgs args)
    {
        try
        {
            var path = ResolveFilePath(webRoot, args.Request.Uri);
            if (path is null || !File.Exists(path))
            {
                args.Response = webView.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
                return;
            }

            var body = Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase)
                ? Encoding.UTF8.GetBytes(RewriteSceneDocument(File.ReadAllText(path, Encoding.UTF8)))
                : File.ReadAllBytes(path);
            args.Response = webView.Environment.CreateWebResourceResponse(
                new MemoryStream(body, writable: false), 200, "OK",
                $"Content-Type: {ContentTypeFor(path)}\r\nCache-Control: no-cache");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"scene request failed: {ex}");
            try { args.Response = webView.Environment.CreateWebResourceResponse(null, 500, "Error", ""); }
            catch (Exception inner) { Debug.WriteLine(inner); }
        }
    }

    /// <summary>
    /// Maps a scene URL to a file under <paramref name="webRoot"/>, or <see langword="null"/> for anything
    /// that is not a plain, servable scene file: another scheme or host, a dot segment or dot file (the
    /// submodule's <c>.git</c>), a backslash, an escape outside the root, or an unknown file type.
    /// </summary>
    internal static string? ResolveFilePath(string webRoot, string requestUri)
    {
        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals(HostName, StringComparison.OrdinalIgnoreCase)) return null;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString).ToArray();
        if (segments.Length == 0) return null;
        foreach (var segment in segments)
        {
            if (segment.StartsWith('.') || segment.Contains('\\') || segment.Contains('/') || segment.Contains(':') ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        }

        if (!ContentTypes.ContainsKey(Path.GetExtension(segments[^1]))) return null;

        var root = Path.GetFullPath(webRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine([root, .. segments]));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>The Content-Type for a servable scene file (see <see cref="ResolveFilePath"/>).</summary>
    internal static string ContentTypeFor(string path) =>
        ContentTypes.TryGetValue(Path.GetExtension(path), out var type) ? type : "application/octet-stream";

    /// <summary>
    /// Adapts a scene <c>index.html</c> to this host: the CSP's <c>qrc:</c> sources become <c>'self'</c>, the
    /// host style and script are injected inline, and the CSP allows exactly those two by hash. Every other
    /// directive and every byte outside the CSP and the two insertions is left untouched.
    /// </summary>
    internal static string RewriteSceneDocument(string html)
    {
        var rewritten = CspMeta.Replace(html, match =>
            match.Groups[1].Value + RewritePolicy(match.Groups[2].Value) + match.Groups[3].Value, 1);

        var style = $"<style>{HostStyle}</style>";
        var headEnd = rewritten.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        rewritten = headEnd >= 0 ? rewritten.Insert(headEnd, style) : style + rewritten;

        var script = $"<script>{HostScript}</script>";
        var bodyEnd = rewritten.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return bodyEnd >= 0 ? rewritten.Insert(bodyEnd, script) : rewritten + script;
    }

    private static string RewritePolicy(string policy)
    {
        var directives = QrcSource.Replace(policy, "'self'")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        AllowHash(directives, "style-src", HostStyle);
        AllowHash(directives, "script-src", HostScript);
        return string.Join("; ", directives);
    }

    private static void AllowHash(List<string> directives, string name, string inlineContent)
    {
        var source = $"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(inlineContent)))}'";
        var index = directives.FindIndex(d => d.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            d.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase));
        if (index < 0) directives.Add($"{name} {source}");
        else directives[index] = directives[index].Replace("'none'", "").TrimEnd() + " " + source;
    }
}
