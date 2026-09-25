# ADR-006: Desktop host and UI ↔ service transport

Status: accepted (transport and hosting). Installer choice remains open; see "Not yet proven".
Date: 2026-09-24

## Context

The architecture proposed a WPF + WebView2 Windows shell that hosts the React UI, plus a local ASP.NET Core application service (LIVING_SPECS D06). Its open questions were: will it package offline, what owns process lifetime, and which local transport to use. The architecture asked for a private in-process bridge, or a loopback endpoint with a per-launch token and strict origin binding. It must never listen on all interfaces.

## Decision

- **Shell:** WPF (`net10.0-windows`) with the WebView2 control. The React bundle ships in the app folder. WebView2 serves it from the virtual host `https://app.tomestack.localhost/` (`SetVirtualHostNameToFolderMapping`), so there is no HTTP server.
- **Service:** the .NET application service (`TomeStack.AppService`) runs **in-process** in the shell. It is not a separate ASP.NET Core host.
- **Transport:** the **WebView2 message bridge** (`chrome.webview.postMessage` ↔ `PostWebMessageAsJson`) carries a transport-neutral JSON command protocol (`CommandDispatcher`). The shipped app opens **no listening socket**.
- **Development:** `TomeStack.DevHost` exposes the same `CommandDispatcher` over `127.0.0.1` with a per-launch random token and an origin allowlist, so the UI can run under Vite with hot reload. It is not shipped.

## Alternatives considered

| Option | Result |
| --- | --- |
| A. In-process service + WebView2 message bridge | **Chosen.** Works offline; nothing to bind; the service lives and dies with the window. |
| B. In-process or sidecar ASP.NET Core on loopback + token + origin check | Works (implemented as DevHost and exercised below). It adds a port, a token to hand to the page, firewall or port-conflict edge cases, and a second process lifetime if run as a sidecar. It offers no benefit to a single-user local app. |
| C. WebView2 host objects (`AddHostObjectToScript`) | Not pursued. They expose COM-visible .NET objects to page script, which is a wider attack surface than a single JSON message channel. They are disabled (`AreHostObjectsAllowed = false`). |

## Evidence (spike run 2026-09-24, Windows 11 26200, WebView2 Runtime 153.0.4234.48, .NET SDK 10.0.301)

| Check | Result |
| --- | --- |
| `TomeStack.exe --smoke` (Debug build) | Pass. UI loaded from the virtual host; `app.info` and `character.list` round-tripped over the bridge; 1.6 s from window creation. |
| Same, `dotnet publish -c Release -r win-x64 --self-contained` | Pass. Ready 1.5–2.8 s after process start. 0 non-app network requests attempted. |
| Negative control: UI bundle removed | First run crashed with an unhandled exception. **Fixed:** the shell now shows an error and the smoke exits 2 (`ui-bundle-missing`). |
| Self-contained publish size | 145 MB, untrimmed (WPF does not support trimming). A framework-dependent build is an option for the installer decision. |
| Loopback DevHost: binding | `netstat` shows `127.0.0.1:5178` only. |
| DevHost: missing token / foreign `Origin` / valid token | 401 / 403 / 200 as designed. |
| Vite dev proxy → DevHost | `character.create` over the proxy returned a full sheet and trace. |

## Security measures in the shell

- Web messages are accepted only when `e.Source` is the app origin.
- Navigation outside the app origin is cancelled, and new windows are suppressed.
- A `WebResourceRequested` filter refuses every http(s) request outside the app origin. This makes "offline by default" a shell guarantee that does not depend on the page. The smoke fails if the UI tries.
- The production bundle carries a CSP (`default-src 'self'`, no remote origins, `object-src 'none'`).
- DevTools and the default context menu are off unless `--devtools` is passed. Autofill and password save are off.
- The WebView2 user-data folder lives inside the TomeStack data directory.
- Error responses carry only messages written for the UI. An unexpected exception becomes `internal` with a correlation id, and its details are written to the local `logs/errors.log` only, never across the bridge.

## Consequences

- There are no port conflicts, firewall prompts or tokens in production. Closing the window ends the process and the service with it.
- Commands run off the UI thread (`Task.Run`) and are serialized with a semaphore. Long-running jobs (M4 imports) will need progress messages. The current protocol is request/response only.
- Imported package bytes still cross the bridge as base64 JSON (the UI reads them from `<input type=file>`). That is acceptable up to the 50 MB package limit. Revisit with a native Open dialog, or WebView2 shared buffers, if packages grow or attachments are added.
- The app requires the Evergreen WebView2 Runtime. It is preinstalled on Windows 11, and the Evergreen Standalone Installer covers offline installs. The shell shows a clear message if the runtime is missing.
- **Export uses a native Save dialog (2026-09-25).** The shell passes an `IHostServices` to the dispatcher. `package.saveAs` exports in-process, asks the user for a location with the WPF `SaveFileDialog` (marshalled to the UI thread), writes through a `.partial` file, and returns only the file name. The page never supplies a path, and the bytes never cross the bridge. Hosts without dialogs (DevHost) answer `unsupported`, and the UI then falls back to `package.export` plus a browser download. The dialog itself is covered by unit tests with a fake host. It is not driven automatically by the smoke.

## Offline evidence (2026-09-25, same machine as above)

| Check | Command | Result |
| --- | --- | --- |
| Smoke also proves the M0 exit gate | `scripts/smoke.ps1 -Exe <TomeStack.exe>` | Pass. Bridge round trip, then `character.create` with fixture content (initiative 3), `package.export` and `package.preview` (`canApply: true`); `blockedRequests: []` |
| Persistence across restarts | `smoke.ps1 -DataDir <dir> -ExpectCharactersAtStart 0`, then again with `1` | Pass. The second run found the character saved by the first |
| Missing WebView2 runtime (simulated) | `scripts/offline-check.ps1 -Mode MissingRuntime` | Pass. `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER` pointing at an empty folder gives `webview2-runtime-missing`, exit 2, the in-window message, and no crash |
| Network disabled | `scripts/offline-check.ps1 -Mode AssumeOffline` (turn on airplane mode first) | **Not run.** Running it would have disconnected the machine used for this work. The owner has to run it |

An earlier draft of `offline-check.ps1` also disabled network adapters from the script. Windows Defender (AMSI) blocked it as malicious, so that mode was removed. Airplane mode plus `-Mode AssumeOffline` is the supported procedure.

## Not yet proven (keep open in M0)

- Installer technology, per-user install, upgrade, and uninstall-preserves-data. These are blocked on the owner's installer decision (ADR-008).
- A run with the network actually disabled (procedure above; not yet executed).
- **Clean VM only:**
  - first launch on a machine that never had TomeStack or its data directory;
  - a truly absent WebView2 Runtime. The simulation above swaps the loader path but does not remove the runtime, so it does not exercise the installer's runtime bootstrap;
  - Windows 10 (support level is an open owner decision);
  - a standard (non-admin) user account;
  - behavior with the Evergreen Standalone Installer offline.
- The smoke on GitHub-hosted runners. It is wired into CI as non-blocking until it passes there.

Supersedes: none. Updates LIVING_SPECS D06.
