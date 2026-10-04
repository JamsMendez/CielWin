using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace CielWin.Interop;

/// <summary>
/// A loopback-only <see cref="HttpListener"/> serving independent routes behind the SAME security
/// gates, port and bearer token: <see cref="AlertHttpProtocol.AlertsPath"/>, which hands an accepted
/// body (translated to command text) to the alert handler,
/// <see cref="AlertHttpProtocol.AlertsClearPath"/>, which hands a validated clear id to the clear
/// handler, and <see cref="WallpaperSceneHttpProtocol.ScenePath"/>, which validates the body as a
/// scene name and hands it to a separate switch delegate. Each route is independently enabled by whether its handler
/// delegate was supplied to the constructor: a route whose delegate is <see langword="null"/> is never
/// in the routing table at all, so a request to it is rejected the exact same way (404, same body) as
/// a request to a path that was never a route -- see <see cref="ResolveRoute"/>.
/// </summary>
/// <remarks>
/// <para>
/// Bound to two literal (never wildcard) prefixes: <c>http://127.0.0.1:{port}/</c> and
/// <c>http://localhost:{port}/</c> -- both start fine WITHOUT elevation (no urlacl reservation
/// needed), unlike a wildcard <c>+</c> or <c>*</c> prefix, which <c>HttpListener</c> refuses to
/// register for a non-admin process. Both are needed for a mundane reason, not a security one: a
/// caller that types the literal URL <c>http://localhost:{port}/...</c> resolves "localhost" through
/// DNS first, and that can resolve to the IPv6 loopback address -- a listener bound only to the IPv4
/// literal <c>127.0.0.1</c> prefix never even receives that connection at the socket level (it just
/// times out). If registering both together ever fails (e.g. IPv6 disabled on some machine),
/// <see cref="Start"/> falls back to the IPv4 literal alone rather than refusing to start at all.
/// </para>
/// <para>
/// Measured with raw <see cref="System.Net.Sockets.TcpClient"/> requests (see
/// <c>LocalHttpCommandServerTests</c>'s raw-socket Host-header region): for a connection that arrives
/// on one of THIS class's own registered local addresses (i.e. loopback, the only kind it ever
/// binds), http.sys performs NO <c>Host</c>-header filtering of its own -- it forwards the request to
/// this listener regardless of what <c>Host</c> says. So the <c>Host</c> check in
/// <see cref="HandleRequest"/> (gate step 3) is not defense in depth on top of an http.sys filter for
/// THAT case -- it is the entire defense against DNS rebinding from a loopback client. Separately,
/// once two literal hostname prefixes share a port, http.sys DOES validate a connection that arrives
/// on neither registered local address at all -- e.g. a LAN NIC -- and turns it away itself with its
/// own "400 Bad Request - Invalid Hostname" page before <see cref="HandleRequest"/> ever runs. The
/// <see cref="HttpListenerRequest.RemoteEndPoint"/> check (gate step 1, extracted as the pure,
/// unit-tested <see cref="IsLoopbackRemote"/>) is this class's OWN, independent line of defense for
/// the case a connection reaches here anyway, not a bet on http.sys always doing that filtering.
/// </para>
/// <para>
/// One background thread, calling the synchronous, blocking <see cref="HttpListener.GetContext"/> in
/// a loop, one connection at a time, never letting a single failure throw the loop down.
/// <see cref="Dispose"/> calling <see cref="HttpListener.Stop"/> is what makes the blocked
/// <c>GetContext</c> call return (with an exception caught by the <c>!listener.IsListening</c>
/// branch below). A repeating, non-shutdown <c>GetContext</c> failure backs off, though no black-box
/// test can currently force that path open (see <c>LocalHttpCommandServerTests</c>).
/// </para>
/// </remarks>
public sealed class LocalHttpCommandServer : IHttpCommandServer
{
    /// <summary>
    /// Bounds every per-request wait (<see cref="HttpListenerTimeoutManager"/>) so a slow or silent
    /// client cannot hold the single-threaded loop open indefinitely.
    /// </summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Starting delay before <see cref="RunLoop"/> retries a repeating, non-shutdown
    /// <see cref="HttpListener.GetContext"/> failure, doubled up to <see cref="MaxRetryBackoff"/> and
    /// reset the moment a context is actually accepted.
    /// </summary>
    public static readonly TimeSpan InitialRetryBackoff = TimeSpan.FromMilliseconds(100);

    /// <summary>Ceiling for the backoff above.</summary>
    public static readonly TimeSpan MaxRetryBackoff = TimeSpan.FromMilliseconds(500);

    /// <summary>Bound on <see cref="Dispose"/> joining the background thread.</summary>
    private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(5);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>One entry in the routing table built once at construction time.</summary>
    private sealed record Route(string Path, int MaxBodyBytes, Action<string, HttpListenerResponse> Handle);

    private readonly int _port;
    private readonly string _token;
    private readonly byte[] _tokenBytes;
    private readonly Func<string, string>? _handleCommand;
    private readonly Func<string, bool>? _handleWallpaperSceneSwitch;
    private readonly Func<int?, string>? _handleAlertClear;
    private readonly Action<string> _onDiagnostic;
    private readonly IReadOnlyList<Route> _routes;
    private readonly CancellationTokenSource _stopping = new();

    private HttpListener? _listener;
    private Thread? _thread;
    private bool _disposed;

    /// <param name="port">The loopback TCP port to listen on, 1-65535.</param>
    /// <param name="token">
    /// The already-loaded bearer token every request must present. Loading/creating it is
    /// <c>AlertHttpTokenFile</c>'s job; this type only compares against whatever it is handed.
    /// </param>
    /// <param name="handleCommand">
    /// Called with the translated command text for <see cref="AlertHttpProtocol.AlertsPath"/>. Never
    /// called for a request the gate already rejected. A throw from it is caught and reported as
    /// <c>"error: internal error"</c>. <see langword="null"/> means the alert route is off: it answers
    /// exactly like an unknown path.
    /// </param>
    /// <param name="onDiagnostic">
    /// Told about every start failure, rejection and per-request error. Defaults to a no-op. Never
    /// told the token or the raw <c>Authorization</c> header.
    /// </param>
    /// <param name="handleWallpaperSceneSwitch">
    /// Called with the already-validated, canonical lowercase scene name once a
    /// <see cref="WallpaperSceneHttpProtocol.ScenePath"/> body passes
    /// <see cref="WallpaperSceneHttpProtocol.TryValidate"/>. Returns whether the switch was accepted
    /// for dispatch; <see langword="false"/> answers
    /// <see cref="WallpaperSceneHttpProtocol.NotAvailableStatusCode"/>. MUST be non-blocking: it is
    /// called on this server's single request-handling thread, so it may only post work elsewhere,
    /// never wait for the switch to finish. A throw from it is caught and reported like a throw from
    /// <paramref name="handleCommand"/>. <see langword="null"/> means the scene route is off: it
    /// answers exactly like an unknown path.
    /// </param>
    /// <param name="handleAlertClear">
    /// Called with the id from a <see cref="AlertHttpProtocol.AlertsClearPath"/> body that passed
    /// <see cref="AlertHttpProtocol.TryParseClear"/> (<see langword="null"/>: the held warning); its
    /// reply is answered like the alert handler's. A throw from it is caught and reported like a throw
    /// from <paramref name="handleCommand"/>. <see langword="null"/> means the clear route is off: it
    /// answers exactly like an unknown path.
    /// </param>
    public LocalHttpCommandServer(
        int port,
        string token,
        Func<string, string>? handleCommand,
        Action<string>? onDiagnostic = null,
        Func<string, bool>? handleWallpaperSceneSwitch = null,
        Func<int?, string>? handleAlertClear = null)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "The port must be between 1 and 65535.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        _port = port;
        _token = token;
        _tokenBytes = Encoding.UTF8.GetBytes(token);
        _handleCommand = handleCommand;
        _handleWallpaperSceneSwitch = handleWallpaperSceneSwitch;
        _handleAlertClear = handleAlertClear;
        _onDiagnostic = onDiagnostic ?? (_ => { });
        _routes = BuildRoutes();
    }

    /// <summary>
    /// The routing table: one entry per route whose handler delegate was actually supplied. Built
    /// once, since the delegates never change after construction.
    /// </summary>
    private List<Route> BuildRoutes()
    {
        var routes = new List<Route>();

        if (_handleCommand is not null)
        {
            routes.Add(new Route(AlertHttpProtocol.AlertsPath, AlertHttpProtocol.MaxBodyBytes, HandleAlertBody));
        }

        if (_handleAlertClear is not null)
        {
            routes.Add(new Route(AlertHttpProtocol.AlertsClearPath, AlertHttpProtocol.ClearMaxBodyBytes, HandleAlertClearBody));
        }

        if (_handleWallpaperSceneSwitch is not null)
        {
            routes.Add(new Route(WallpaperSceneHttpProtocol.ScenePath, WallpaperSceneHttpProtocol.MaxBodyBytes, HandleWallpaperSceneBody));
        }

        return routes;
    }

    /// <summary>
    /// Starts listening on a background thread. Idempotent: a second call while already listening is
    /// a no-op, and so is any call after <see cref="Dispose"/> (a disposed instance stays
    /// disposed -- it never quietly re-binds the port). Never throws: a failure to bind the port
    /// (e.g. already in use) is reported through <see cref="_onDiagnostic"/> and this instance stays
    /// inert.
    /// </summary>
    public void Start()
    {
        if (_disposed || _thread is not null)
        {
            return;
        }

        var listener = TryCreateListener(includeLocalhostPrefix: true)
            ?? TryCreateListener(includeLocalhostPrefix: false);

        if (listener is null)
        {
            return;
        }

        _listener = listener;
        _thread = new Thread(() => RunLoop(listener)) { IsBackground = true, Name = "CielWin.Http" };
        _thread.Start();
    }

    /// <summary>
    /// Builds and starts one <see cref="HttpListener"/> attempt. <see langword="null"/> on failure,
    /// with a diagnostic already reported -- the caller decides whether to retry with a narrower set
    /// of prefixes or give up for good.
    /// </summary>
    private HttpListener? TryCreateListener(bool includeLocalhostPrefix)
    {
        HttpListener? listener = null;
        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            if (includeLocalhostPrefix)
            {
                listener.Prefixes.Add($"http://localhost:{_port}/");
            }

            listener.TimeoutManager.EntityBody = RequestTimeout;
            listener.TimeoutManager.HeaderWait = RequestTimeout;
            listener.TimeoutManager.IdleConnection = RequestTimeout;
            listener.TimeoutManager.DrainEntityBody = RequestTimeout;
            listener.Start();
            return listener;
        }
        catch (Exception error)
        {
            var prefixes = includeLocalhostPrefix ? "127.0.0.1 and localhost" : "127.0.0.1";
            _onDiagnostic($"alert http: failed to start listening on port {_port} ({prefixes}): {error.GetType().Name}: {error.Message}");

            // A failed attempt must not linger until finalization, and the fallback
            // attempt that may follow creates a second listener on the same port.
            try
            {
                listener?.Close();
            }
            catch
            {
                // Best-effort: the attempt already failed and was reported.
            }

            return null;
        }
    }

    /// <summary>
    /// Runs until <see cref="Dispose"/> stops <paramref name="listener"/>, which is what makes the
    /// blocking <see cref="HttpListener.GetContext"/> call below return with an exception instead of
    /// waiting forever. One request at a time, by construction: the next <c>GetContext</c> call is
    /// not made until <see cref="HandleRequest"/> has fully replied to the previous one. A single
    /// request's failure is caught and reported, never allowed to end the loop.
    /// </summary>
    private void RunLoop(HttpListener listener)
    {
        var backoff = InitialRetryBackoff;

        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = listener.GetContext();
                backoff = InitialRetryBackoff; // reset the moment a context is actually accepted
            }
            catch (Exception) when (!listener.IsListening)
            {
                // Dispose already stopped/closed the listener -- an ordinary shutdown, not a fact
                // worth a diagnostic.
                return;
            }
            catch (Exception error)
            {
                _onDiagnostic($"alert http: {error.GetType().Name}: {error.Message}");
                _stopping.Token.WaitHandle.WaitOne(backoff);
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxRetryBackoff.Ticks));
                continue;
            }

            try
            {
                HandleRequest(context);
            }
            catch (Exception error)
            {
                _onDiagnostic($"alert http: {error.GetType().Name}: {error.Message}");
                try
                {
                    context.Response.Close();
                }
                catch
                {
                    // Best-effort: the connection is being abandoned anyway.
                }
            }
        }
    }

    /// <summary>
    /// The request gate, run in a fixed order -- first failing check wins.
    /// </summary>
    private void HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // 1. Loopback RemoteEndPoint -- see IsLoopbackRemote and the class remarks for exactly what
        // this does and does not add on top of binding only loopback prefixes.
        if (!IsLoopbackRemote(request.RemoteEndPoint))
        {
            Reject(response, 403, "remote endpoint is not loopback");
            return;
        }

        // 2. Any Origin header at all means a browser sent this.
        if (!string.IsNullOrEmpty(request.Headers["Origin"]))
        {
            Reject(response, 403, "browser requests are not accepted");
            return;
        }

        // 3. Host header must be exactly 127.0.0.1:port or localhost:port (DNS rebinding).
        if (!IsAllowedHost(request.UserHostName))
        {
            Reject(response, 403, "unexpected host header");
            return;
        }

        // 4. Path -- resolved against the routing table of ENABLED routes only. A route whose
        // handler delegate was never supplied is not in this table, so it is indistinguishable from
        // a path that was never a route: both fall through to the exact same 404 below.
        var route = ResolveRoute(request.Url?.AbsolutePath);
        if (route is null)
        {
            Reject(response, 404, "no such route");
            return;
        }

        // 5. Method. OPTIONS (a CORS preflight) is rejected here too, and never answered with any
        // Access-Control-* header. Every route only ever accepts POST.
        if (!string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            response.Headers["Allow"] = "POST";
            Reject(response, 405, "method not allowed");
            return;
        }

        // 6. Bearer token, constant-time. Shared by every route.
        if (!HasValidToken(request))
        {
            response.Headers["WWW-Authenticate"] = "Bearer";
            Reject(response, 401, "missing or invalid bearer token");
            return;
        }

        // 7. Content-Type, ignoring parameters like ";charset=utf-8". Shared by every route.
        if (!IsJsonMediaType(request.ContentType))
        {
            Reject(response, 415, "content type must be application/json");
            return;
        }

        // 8/9. Body size cap (chosen from the route BEFORE a single byte is read) and UTF-8 validity.
        if (!TryReadBody(request, route.MaxBodyBytes, out var body, out var readStatus, out var readReason))
        {
            Reject(response, readStatus, readReason!);
            return;
        }

        // 10/11. Route-specific translation and dispatch.
        route.Handle(body!, response);
    }

    /// <summary>
    /// The route whose <see cref="Route.Path"/> exactly matches <paramref name="path"/>, or
    /// <see langword="null"/> when none does -- including a route that exists but was never
    /// enabled for THIS instance, and a genuinely unknown path.
    /// </summary>
    private Route? ResolveRoute(string? path) =>
        _routes.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.Ordinal));

    /// <summary>Finishes gate 10/11 for <see cref="AlertHttpProtocol.AlertsPath"/>.</summary>
    private void HandleAlertBody(string body, HttpListenerResponse response)
    {
        // 10. Translate to the command grammar.
        if (!AlertHttpProtocol.TryTranslate(body, out var command, out var translateError))
        {
            Reject(response, 400, translateError!);
            return;
        }

        // 11. Hand the command to the handler.
        string reply;
        try
        {
            reply = _handleCommand!(command!);
        }
        catch (Exception error)
        {
            _onDiagnostic($"alert http: the command handler threw {error.GetType().Name}: {error.Message}");
            WriteReply(response, 500, AlertReplyProtocol.FormatError("internal error"));
            return;
        }

        WriteReply(response, AlertHttpProtocol.StatusCodeFor(reply), reply);
    }

    /// <summary>Finishes gate 10/11 for <see cref="AlertHttpProtocol.AlertsClearPath"/>.</summary>
    private void HandleAlertClearBody(string body, HttpListenerResponse response)
    {
        // 10. {} or { "id": n }.
        if (!AlertHttpProtocol.TryParseClear(body, out var id, out var parseError))
        {
            Reject(response, 400, parseError!);
            return;
        }

        // 11. Hand the id to the handler.
        string reply;
        try
        {
            reply = _handleAlertClear!(id);
        }
        catch (Exception error)
        {
            _onDiagnostic($"alert http: the alert clear handler threw {error.GetType().Name}: {error.Message}");
            WriteReply(response, 500, AlertReplyProtocol.FormatError("internal error"));
            return;
        }

        WriteReply(response, AlertHttpProtocol.StatusCodeFor(reply), reply);
    }

    /// <summary>
    /// Finishes gate 10/11 for <see cref="WallpaperSceneHttpProtocol.ScenePath"/>. A validation failure
    /// answers with the SAME "error: ..." body format every other rejection uses, so a caller sees one
    /// consistent error shape regardless of route.
    /// </summary>
    private void HandleWallpaperSceneBody(string body, HttpListenerResponse response)
    {
        var outcome = WallpaperSceneHttpProtocol.TryValidate(body, out var scene, out var error);
        if (outcome != WallpaperSceneRequestOutcome.Accepted)
        {
            Reject(response, WallpaperSceneHttpProtocol.StatusCodeFor(outcome), error!);
            return;
        }

        // The delegate is documented (constructor) as non-blocking: it only posts the switch
        // elsewhere and reports back whether that dispatch was accepted, never waits for the switch
        // itself to finish.
        bool accepted;
        try
        {
            accepted = _handleWallpaperSceneSwitch!(scene!);
        }
        catch (Exception handlerError)
        {
            _onDiagnostic($"alert http: the wallpaper scene switch handler threw {handlerError.GetType().Name}");
            WriteReply(response, 500, AlertReplyProtocol.FormatError("internal error"));
            return;
        }

        if (!accepted)
        {
            Reject(response, WallpaperSceneHttpProtocol.NotAvailableStatusCode, WallpaperSceneHttpProtocol.NotAvailableError);
            return;
        }

        WriteReply(response, 202, AlertReplyProtocol.OkReply);
    }

    /// <summary>
    /// The pure loopback test behind gate step 1 (extracted so it can be unit-tested
    /// deterministically, without a real socket). <see langword="null"/> -- no remote endpoint at all
    /// -- is never loopback. Recognises IPv4 127.0.0.0/8, IPv6 <c>::1</c>, and an IPv4 address mapped
    /// into IPv6 (<c>::ffff:127.x.x.x</c>) -- the shape a dual-stack socket can hand back for an IPv4
    /// peer even though this listener's own prefixes are literal IPv4/hostname, never <c>::</c> or
    /// <c>+</c>.
    /// </summary>
    internal static bool IsLoopbackRemote(IPEndPoint? remote)
    {
        if (remote is null)
        {
            return false;
        }

        var address = remote.Address;
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return IPAddress.IsLoopback(address);
    }

    private bool IsAllowedHost(string? host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return false;
        }

        return string.Equals(host, $"127.0.0.1:{_port}", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, $"localhost:{_port}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsJsonMediaType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
        {
            return false;
        }

        var mediaType = contentType.Split(';')[0].Trim();
        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The <c>Bearer</c> scheme name is matched case-insensitively (RFC 7235's <c>auth-scheme</c>
    /// is explicitly case-insensitive, and real clients disagree on casing) -- but the token itself
    /// is compared with <see cref="CryptographicOperations.FixedTimeEquals"/> over UTF-8 bytes,
    /// case-SENSITIVE and constant-time, per the task file. <c>FixedTimeEquals</c> returns
    /// <see langword="false"/> immediately when the lengths differ (a documented, deliberate
    /// short-circuit -- a length mismatch is not a secret worth constant time over).
    /// </summary>
    private bool HasValidToken(HttpListenerRequest request)
    {
        var header = request.Headers["Authorization"];
        if (string.IsNullOrEmpty(header))
        {
            return false;
        }

        var spaceIndex = header.IndexOf(' ');
        if (spaceIndex < 0)
        {
            return false;
        }

        var scheme = header[..spaceIndex];
        if (!string.Equals(scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(header[(spaceIndex + 1)..]);
        return CryptographicOperations.FixedTimeEquals(providedBytes, _tokenBytes);
    }

    /// <summary>
    /// Reads the body, enforcing <paramref name="maxBodyBytes"/> -- the caller resolves this from
    /// the matched <see cref="Route"/> BEFORE this method reads a single byte, so the alerts route
    /// keeps <see cref="AlertHttpProtocol.MaxBodyBytes"/> while the scene route gets its own, smaller
    /// <see cref="WallpaperSceneHttpProtocol.MaxBodyBytes"/> -- two ways: a DECLARED <see
    /// cref="HttpListenerRequest.ContentLength64"/> past the cap is rejected before a single byte is
    /// read; an UNKNOWN length (chunked transfer) is read at most one byte past the cap, so the
    /// excess is detected without buffering an unbounded stream. Only once the size is known-good is
    /// the buffer decoded as strict UTF-8.
    /// </summary>
    private static bool TryReadBody(HttpListenerRequest request, int maxBodyBytes, out string? body, out int status, out string? reason)
    {
        body = null;

        if (request.ContentLength64 > maxBodyBytes)
        {
            status = 413;
            reason = "request body is too large";
            return false;
        }

        byte[] buffer;
        using (var memory = new MemoryStream())
        {
            var chunk = new byte[4096];
            int read;
            var total = 0;
            var overLimit = false;

            while ((read = request.InputStream.Read(chunk, 0, chunk.Length)) > 0)
            {
                total += read;
                if (total > maxBodyBytes)
                {
                    // Enough to know it is oversized; stop reading rather than draining an
                    // arbitrarily large stream.
                    overLimit = true;
                    break;
                }

                memory.Write(chunk, 0, read);
            }

            if (overLimit)
            {
                status = 413;
                reason = "request body is too large";
                return false;
            }

            buffer = memory.ToArray();
        }

        try
        {
            body = StrictUtf8.GetString(buffer);
        }
        catch (DecoderFallbackException)
        {
            status = 400;
            reason = "body is not valid UTF-8";
            return false;
        }

        status = 200;
        reason = null;
        return true;
    }

    private void Reject(HttpListenerResponse response, int status, string reason)
    {
        _onDiagnostic($"alert-http rejected {status} {reason}");
        WriteReply(response, status, AlertReplyProtocol.FormatError(reason));
    }

    private static void WriteReply(HttpListenerResponse response, int status, string body)
    {
        try
        {
            response.StatusCode = status;
            response.ContentType = "text/plain; charset=utf-8";
            var bytes = Encoding.UTF8.GetBytes(body);
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        finally
        {
            response.OutputStream.Close();
        }
    }

    /// <summary>
    /// Stops and closes the listener and joins the background thread. Idempotent, and safe to call
    /// before <see cref="Start"/> was ever called.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopping.Cancel(); // wakes RunLoop promptly even mid-backoff-wait

        try
        {
            _listener?.Stop();
        }
        catch
        {
            // Best-effort: the goal below is only to make the blocked GetContext call return.
        }

        var threadStopped = _thread?.Join(JoinTimeout) ?? true;

        try
        {
            _listener?.Close();
        }
        catch
        {
            // Best-effort cleanup on the way out.
        }

        // If the bounded join timed out, RunLoop may still reach its backoff wait,
        // which reads _stopping.Token -- disposing the source under it would throw on a background
        // thread and take the process down. Leaving one cancelled source to the GC is harmless.
        if (threadStopped)
        {
            _stopping.Dispose();
        }
    }
}
