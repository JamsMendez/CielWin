# CielWin

CielWin shows an animated HTML scene as your Windows desktop wallpaper, or in a small always-on-top
window, and runs a loopback HTTP server that other tools use to switch the scene and show alerts.
It runs as a normal user: no administrator rights, no installer.

## Quick path

Prebuilt Windows builds are on the [Releases](https://github.com/JamsMendez/CielWin/releases) page:
download `CielWin-win-x64.zip`, extract it and run `CielWin.App.exe`. It includes the .NET runtime;
it still needs the WebView2 Runtime, which Windows 11 ships with. To build from source instead:

1. Install the [requirements](#requirements).
2. Build and start it:

   ```powershell
   dotnet build CielWin.sln
   dotnet run --project CielWin.App
   ```

3. The scene appears over the desktop wallpaper and a CielWin icon appears in the notification area.
   Right-click the icon to switch mode or scene, or to exit.

## Requirements

| Requirement | Detail |
|-------------|--------|
| Windows | Windows 10 version 2004 (build 19041) or later (`SupportedOSPlatformVersion` 10.0.19041.0) |
| .NET | .NET 10 SDK to build (target framework `net10.0-windows10.0.19041.0`, WPF + Windows Forms) |
| WebView2 | Microsoft Edge WebView2 Runtime (scenes render in WebView2) |
| Node.js | Only for the scene tests and the tray icon generator (see below) |

## Build, run, test

| Task | Command |
|------|---------|
| Build | `dotnet build CielWin.sln` |
| Run | `dotnet run --project CielWin.App` |
| Run the built exe | `CielWin.App\bin\Debug\net10.0-windows10.0.19041.0\CielWin.App.exe` |
| Test | `dotnet test CielWin.sln` |
| Release | `git tag vX.Y.Z` then `git push origin vX.Y.Z` |

Releases: pushing a `v*` tag runs `.github/workflows/release.yml`, which publishes a self-contained
`win-x64` build (no .NET runtime needed on the target machine), zips it and attaches
`CielWin-win-x64.zip` to a GitHub release for that tag. The build is not code-signed, so Windows
SmartScreen warns the first time it runs (**More info** → **Run anyway**).

Test notes:

- The scene tests run JavaScript harnesses with `node`. Without `node` on `PATH` they are skipped.
- Tests that touch the real desktop are skipped by default. To run them, set
  `CIELWIN_RUN_DESKTOP_TESTS=1` in an interactive desktop session and exit CielWin first (the
  wallpaper tests skip while CielWin is running).

```powershell
$env:CIELWIN_RUN_DESKTOP_TESTS = "1"
dotnet test CielWin.sln
```

## Modes and scenes

| Mode (`wallpaper-mode`) | Tray label | What you see |
|-------------------------|------------|--------------|
| `scene` (default) | Scene wallpaper | The scene composited over the desktop wallpaper, full screen |
| `scene-mini` | Mini window | A small square, always-on-top, click-through scene window in the work area; the desktop background stays as Windows has it |

Scenes (`scene`): `processing` (default), `explorer`, `idle`, `raphael`. Scenes render at a fixed 60 fps.

The mini window is `monitor height / 5` on a side and sits at one of eight positions of the work
area (`mini-position`).

### Fullscreen pause

In `scene` mode the scene pauses while a fullscreen window covers the primary monitor and resumes
when it is uncovered. Alerts that arrive while covered wait and show once the desktop is visible
again (dropped after waiting more than 5 minutes). The mini window is never paused.

## Tray menu

| Item | Action |
|------|--------|
| Wallpaper mode | Switch live between **Scene wallpaper** and **Mini window** (checked item = current) |
| Scene | Switch to Processing, Explorer, Idle or Raphael (checked item = current) |
| Exit | Close CielWin |

Mode and scene changes are saved to the settings file.

## Hotkeys

| Chord | Action |
|-------|--------|
| Alt+M | Move the mini window to the next position, clockwise |
| Alt+Shift+M | Move the mini window to the previous position, counter-clockwise |

- Registered with `RegisterHotKey` (no keyboard hook, no elevation). Holding the chord moves once.
- Active only in `scene-mini` mode; in `scene` mode a press is ignored.
- The new position is saved to `mini-position`.
- If another app already owns a chord, CielWin keeps running without it and writes a trace line.

Clockwise order: `top-left` → `top-center` → `top-right` → `right-center` → `bottom-right` →
`bottom-center` → `bottom-left` → `left-center`.

## Single instance

Only one CielWin runs per user session (named mutex `Local\CielWin.SingleInstance`). Starting a
second copy exits immediately. Another signed-in user can run their own copy.

## Settings

File: `%LOCALAPPDATA%\CielWin\settings.conf`

- Created with commented defaults on first run.
- Read once at startup: restart CielWin after editing it by hand.
- Format: `key = value`, one per line. Blank lines and `#` comments are ignored. Keys and values are
  case-insensitive. The last assignment of a key wins.
- An unknown key is skipped; an unrecognised value keeps the default for that key.

| Key | Allowed values | Default |
|-----|----------------|---------|
| `wallpaper-mode` | `scene`, `scene-mini` | `scene` |
| `http-server` | `on`, `off` (also `true`/`false`, `1`/`0`) | `on` |
| `http-server-port` | `1`-`65535` | `47811` |
| `scene` | `processing`, `explorer`, `idle`, `raphael` | `processing` |
| `mini-position` | `top-left`, `top-center`, `top-right`, `right-center`, `bottom-right`, `bottom-center`, `bottom-left`, `left-center` | `top-right` |

`scene` and `mini-position` are also written by CielWin whenever you change them from the tray, the
hotkeys or the HTTP API. `http-server = off` closes the port and disables both HTTP routes.

**Legacy keys (read, never written).** Settings files from CosmicWin keep working:
`wallpaper-mode = html | html-mini | mini`, `wallpaper-scene`, `mini-corner`, `alert-http` and
`alert-http-port`. When a file has both a legacy key and its new name, the new key wins regardless
of line order. CielWin only writes the new names.

**An unreadable file is never overwritten.** If `settings.conf` exists but cannot be read (for
example, locked by another program), CielWin starts with the defaults and skips every save for that
session, so your file is not replaced. Saves are atomic (temporary file, then replace).

**Trace log.** Diagnostics go to `%LOCALAPPDATA%\CielWin\trace.log` (rolled over to `trace.log.1`
past 1 MB). Lines never contain the HTTP token.

## HTTP API

| Property | Value |
|----------|-------|
| Base URL | `http://127.0.0.1:47811` or `http://localhost:47811` (port = `http-server-port`) |
| Reachability | Loopback only |
| Method | `POST` only |
| Auth | `Authorization: Bearer <token>` |
| Token file | `%LOCALAPPDATA%\CielWin\http.token`, created the first time the server starts |
| Content type | `application/json` (parameters such as `; charset=utf-8` are allowed) |
| Response body | Plain text: `ok`, or one line `error: <reason>` |

The token is 43 characters (base64url) and stays the same across restarts. Requests with an
`Origin` header (browsers) or a `Host` header other than `127.0.0.1:<port>` / `localhost:<port>` are
rejected.

### Switch scene: `POST /v1/wallpaper/scene`

Body: exactly one field, `scene`, one of `processing`, `explorer`, `idle`, `raphael`
(case-insensitive). Maximum body size: 256 bytes. Unknown fields are rejected.

```json
{ "scene": "raphael" }
```

```bash
curl -X POST http://127.0.0.1:47811/v1/wallpaper/scene \
  -H "Authorization: Bearer $(cat "$LOCALAPPDATA/CielWin/http.token")" \
  -H "Content-Type: application/json" \
  -d '{"scene":"raphael"}'
```

```powershell
$token = Get-Content "$env:LOCALAPPDATA\CielWin\http.token" -Raw
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:47811/v1/wallpaper/scene `
  -Headers @{ Authorization = "Bearer $($token.Trim())" } `
  -ContentType application/json -Body '{"scene":"raphael"}'
```

`202` means the switch was accepted and runs asynchronously; the new scene is saved to `scene`.

### Show an alert: `POST /v1/alerts`

Body fields (all optional, whole numbers; unknown fields are rejected). Maximum body size: 1024 bytes.

| Field | Range | Default | Meaning |
|-------|-------|---------|---------|
| `warning` | 1-16 | - | Number of warning tiles |
| `failed` | 1-16 | - | Number of failed tiles |
| `duration` | 1-60 | 5 | Seconds the alert stays on screen |

Rules: at least one of `warning` / `failed` is required, and `warning + failed` must not exceed 16.

```json
{ "warning": 2, "failed": 1, "duration": 5 }
```

```bash
curl -X POST http://127.0.0.1:47811/v1/alerts \
  -H "Authorization: Bearer $(cat "$LOCALAPPDATA/CielWin/http.token")" \
  -H "Content-Type: application/json" \
  -d '{"warning":2,"failed":1,"duration":5}'
```

```powershell
$token = Get-Content "$env:LOCALAPPDATA\CielWin\http.token" -Raw
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:47811/v1/alerts `
  -Headers @{ Authorization = "Bearer $($token.Trim())" } `
  -ContentType application/json -Body '{"warning":2,"failed":1,"duration":5}'
```

Only one alert exists at a time. A request that arrives while an alert is showing or waiting is
ignored, but still answered `202`.

### Response codes

| Code | Meaning |
|------|---------|
| `202` | Accepted (`ok`) |
| `400` | Invalid body: not JSON, not an object, unknown field, wrong type, value out of range, invalid UTF-8 |
| `401` | Missing or invalid bearer token (`WWW-Authenticate: Bearer`) |
| `403` | Non-loopback client, `Origin` header present, or unexpected `Host` header |
| `404` | Unknown path |
| `405` | Method other than `POST` (`Allow: POST`) |
| `413` | Body larger than the route's limit (256 or 1024 bytes) |
| `415` | Content type is not `application/json` |
| `500` | Internal error |
| `503` | Scene route only: scene switching is not available (for example, while CielWin shuts down) |

The server is not running at all when `http-server = off`, when the token file cannot be read or
created, or when the port is in use (the reason is written to the trace log).

## Tray icon

The tray and executable icon, `CielWin.App/Assets/raphael-mini.ico` (plus the 256x256
`raphael-mini.png` master), is the Raphael scene's mini variant on a transparent background. To
regenerate it from the repo root (requires Windows, Microsoft Edge and Node.js 22 or later):

```powershell
node tools/tray-icon/render-raphael-mini.mjs
```

Optional environment variables: `EDGE=<path to msedge.exe>`, `ICON_FRAME_MS=<scene clock in ms>`
(default 4000). The output is deterministic.

## License

MIT. See [LICENSE](LICENSE).
