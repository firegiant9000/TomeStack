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

## Consequences

- There are no port conflicts, firewall prompts or tokens in production. Closing the window ends the process and the service with it.
- Commands run off the UI thread (`Task.Run`) and are serialized with a semaphore. Long-running jobs (M4 imports) will need progress messages. The current protocol is request/response only.
- Package bytes cross the bridge as base64 JSON. That is acceptable up to the 50 MB package limit. Revisit with native file dialogs in the shell, or WebView2 shared buffers, if packages grow or attachments are added.
- The app requires the Evergreen WebView2 Runtime. It is preinstalled on Windows 11, and the Evergreen Standalone Installer covers offline installs. The shell shows a clear message if the runtime is missing.
- Export currently uses WebView2's download flow, which saves to the Downloads folder. A native Save dialog belongs to M2.

## Not yet proven (keep open in M0)

- Installer technology (MSIX vs. Velopack vs. WiX), per-user install, upgrade, and uninstall-preserves-data.
- A clean-machine install, and a run with the network adapter disabled. The request filter and CSP are evidence, not a substitute.
- Windows 10 and a missing-WebView2-runtime path on real hardware.
- The smoke on GitHub-hosted runners. It is wired into CI as non-blocking until it passes there.

Supersedes: none. Updates LIVING_SPECS D06.
