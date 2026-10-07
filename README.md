# CielWin

CielWin shows an animated HTML scene as your Windows desktop wallpaper, or in a small always-on-top
window, and runs a loopback HTTP server that other tools use to switch the scene and show alerts.
It runs as a normal user: no administrator rights, no installer.

https://github.com/user-attachments/assets/e3471590-c9b3-4db1-8920-853eb182ff0f

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
| `scene` (default) | Scene Wallpaper | The scene composited over the desktop wallpaper, full screen |
| `scene-mini` | Scene Mini | A small square, always-on-top, click-through scene window in the work area; the desktop background stays as Windows has it |

Scenes (`scene`): `processing` (default), `explorer`, `idle`, `raphael`.

### Frame rate

Every scene, in both modes, runs at one global frame-rate cap (`frame-rate`): **60 FPS** (default)
or **30 FPS**. Choose it from the tray's **Frame rate** submenu.

- Changing it rebuilds the scene page (the wallpaper layer or the mini window) at the new cap, so
  the animation may restart. An alert that is showing comes back for its remaining time without
  playing its sound again; a held warning stays up.
- The new rate is saved at once. Choosing the current rate does nothing.
- It is a cap, not a guarantee: a heavy scene or a busy machine can draw fewer frames.

The mini window is `monitor height / 5` on a side and sits at one of eight positions of the work
area (`mini-position`).

### Cursor dodge

Clicks go through the mini window, but it still hides what is under it. When the mouse cursor comes
near (within 24 px of the window), it glides aside so you can see behind it, and glides back once the
cursor has stayed away for 400 ms.

- **Direction:** away from the cursor along the dominant axis: a cursor on the right moves it left,
  above moves it down, below moves it up, on the left moves it right. It moves one window plus 32 px
  (the 24 px margin and an 8 px gap), clear of the spot it left.
- **Edges:** the moved window must fit in its monitor's work area (never over the taskbar). If the
  preferred side does not fit (`top-right` with the cursor on its left), it takes a perpendicular side,
  the one farther from the cursor first. If none fits, it stays.
- **Following it:** if the cursor reaches the moved window, it takes another side; lingering over the
  spot it left keeps it aside.
- **Saved position:** a dodge never changes `mini-position`. Alt+M / Alt+Shift+M cancel a dodge and
  glide to the new position from wherever the window is.
- The cursor is read (`GetCursorPos`) every 100 ms while the mini window is shown; the polling stops
  with it (mode switch, frame-rate rebuild, exit). Pixels are physical, so the margins look smaller on
  a high-DPI monitor.

### Fullscreen pause

In `scene` mode the scene pauses while a fullscreen window covers the primary monitor and resumes
when it is uncovered. Alerts that arrive while covered wait and show once the desktop is visible
again (dropped after waiting more than 5 minutes). The mini window is never paused.

## Tray menu

| Item | Action |
|------|--------|
| Scene Mode | Switch live between **Scene Wallpaper** and **Scene Mini** (checked item = current) |
| Scene | Switch to Processing, Explorer, Idle or Raphael (checked item = current) |
| Frame rate | Switch the global cap between **30 FPS** and **60 FPS** for every scene and both modes (checked item = current); rebuilds the scene page (see *Frame rate*) |
| Import failed sound… / Import warning sound… | Pick a `.wav`, `.mp3` or `.m4a` file; it is copied into `%LOCALAPPDATA%\CielWin\sounds\` and played when an alert of that kind appears |
| Remove failed sound / Remove warning sound | Delete that kind's imported sound (shown only when it has one) |
| Alert sounds | Mute or unmute alert sounds (checked = on; shown only when at least one sound is imported) |
| Exit | Close CielWin |

Mode, scene, frame-rate and sound changes are saved to the settings file.

No sound ships with CielWin: alerts are silent until you import one. Each kind plays only its own
sound (an alert with any failed tile plays the failed sound); a kind without a sound stays silent.
A missing or unplayable sound file is skipped and noted in the trace log. An alert plays its sound once,
when it first shows; a held warning is the one exception: it repeats every 5 seconds while it shows
(see *Held warning*).

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
| `http-server-port` | `1`-`65535` | `43811` |
| `scene` | `processing`, `explorer`, `idle`, `raphael` | `processing` |
| `frame-rate` | `30`, `60` (global FPS cap for every scene and both modes) | `60` |
| `mini-position` | `top-left`, `top-center`, `top-right`, `right-center`, `bottom-right`, `bottom-center`, `bottom-left`, `left-center` | `top-right` |
| `alert-sounds` | `on`, `off` (also `true`/`false`, `1`/`0`) | `on` |
| `failed-sound` | A file name in `%LOCALAPPDATA%\CielWin\sounds\` ending in `.wav`, `.mp3` or `.m4a`; empty for none | empty |
| `warning-sound` | Same as `failed-sound` | empty |
| `alert-hold-max-seconds` | `10`-`3600` | `600` |

`scene` and `mini-position` are also written by CielWin whenever you change them from the tray, the
hotkeys or the HTTP API; `frame-rate` by the tray's **Frame rate** submenu; `alert-sounds`, `failed-sound` and `warning-sound` by the tray. A sound
value that is not a bare file name of a supported format reads as no sound. `http-server = off` closes the port and disables every HTTP route.
`alert-hold-max-seconds` is how long a held warning (see *Held warning* below) may stay up without
being cleared, counted from its request.

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
| Base URL | `http://127.0.0.1:43811` or `http://localhost:43811` (port = `http-server-port`) |
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
curl -X POST http://127.0.0.1:43811/v1/wallpaper/scene \
  -H "Authorization: Bearer $(cat "$LOCALAPPDATA/CielWin/http.token")" \
  -H "Content-Type: application/json" \
  -d '{"scene":"raphael"}'
```

```powershell
$token = Get-Content "$env:LOCALAPPDATA\CielWin\http.token" -Raw
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:43811/v1/wallpaper/scene `
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
| `duration` | 0-60 | 5 | Seconds the alert stays on screen; `0` holds a warning until cleared |

Rules: at least one of `warning` / `failed` is required, and `warning + failed` must not exceed 16.

```json
{ "warning": 2, "failed": 1, "duration": 5 }
```

```bash
curl -X POST http://127.0.0.1:43811/v1/alerts \
  -H "Authorization: Bearer $(cat "$LOCALAPPDATA/CielWin/http.token")" \
  -H "Content-Type: application/json" \
  -d '{"warning":2,"failed":1,"duration":5}'
```

```powershell
$token = Get-Content "$env:LOCALAPPDATA\CielWin\http.token" -Raw
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:43811/v1/alerts `
  -Headers @{ Authorization = "Bearer $($token.Trim())" } `
  -ContentType application/json -Body '{"warning":2,"failed":1,"duration":5}'
```

An accepted alert is answered `202` with `ok id=<n>`: `n` starts at 1 and grows with every accepted
alert while CielWin runs. Only one alert exists at a time. A request that arrives while an alert is
showing or waiting is ignored, but still answered `202`, with a plain `ok` (no id). An alert waiting
for the desktop (under a fullscreen window) starts once it is visible, or is dropped after 5 minutes.

### Held warning

`"duration": 0` holds a warning on screen until it is cleared, for example while a question waits
for an answer. It is accepted with `warning` only; with any `failed` the answer is
`400 error: 'duration:0' requires warning only`.

```json
{ "warning": 1, "duration": 0 }
```

- It ends when cleared (`POST /v1/alerts/clear`), or by itself `alert-hold-max-seconds` (default
  600) after it was requested, not after it first showed. A held warning still waiting is dropped
  by that deadline or the 5-minute start limit, whichever comes first.
- A request with any failed tile while a held warning shows (or waits) is shown at once, for its
  own duration, with its sound. The held warning is suspended and comes back when the failed alert
  ends, for the rest of its hold, with the same id and without replaying its sound, however many
  times it is preempted. One preempted before it ever showed plays its sound when it first shows.
  If it was cleared or passed its hold max meanwhile, it does not come back.
- While a held warning shows, its warning sound (when alert sounds are on and a warning sound is
  imported) plays again every 5 seconds until it is cleared or its hold max passes. It does not
  repeat while suspended, waiting or covered by a fullscreen window; once it shows again, the next
  repeat comes 5 seconds later. Timed alerts play their sound once.
- A warning while a held warning shows is ignored as usual. A held warning sent while a timed alert
  shows waits and starts when that one ends.
- At most one held warning exists at a time: a held warning sent while another one shows, waits or
  is suspended is ignored (plain `202 ok`, no id).

### Clear an alert: `POST /v1/alerts/clear`

Body: `{}` clears the held warning, whatever its id (never a timed alert); `{ "id": n }` clears that
alert, held or timed, whether it is showing, suspended or waiting. `n` is the id from `ok id=<n>`, a
whole number of 1 or more; no other field is accepted. Maximum body size: 64 bytes. Always answered
`202 ok`, whether or not anything was cleared.

```bash
curl -X POST http://127.0.0.1:43811/v1/alerts/clear \
  -H "Authorization: Bearer $(cat "$LOCALAPPDATA/CielWin/http.token")" \
  -H "Content-Type: application/json" \
  -d '{}'
```

### Response codes

| Code | Meaning |
|------|---------|
| `202` | Accepted (`ok`, or `ok id=<n>` for an accepted alert) |
| `400` | Invalid body: not JSON, not an object, unknown field, wrong type, value out of range, invalid UTF-8 |
| `401` | Missing or invalid bearer token (`WWW-Authenticate: Bearer`) |
| `403` | Non-loopback client, `Origin` header present, or unexpected `Host` header |
| `404` | Unknown path |
| `405` | Method other than `POST` (`Allow: POST`) |
| `413` | Body larger than the route's limit (64, 256 or 1024 bytes) |
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

<p align="right">
  <a href="https://github.com/Gentleman-Programming/gentle-ai"><img src="https://raw.githubusercontent.com/Gentleman-Programming/gentle-ai/main/docs/assets/brand/built-with-gentle-ai.png" alt="Built with Gentle-AI" width="180"></a>
</p>
