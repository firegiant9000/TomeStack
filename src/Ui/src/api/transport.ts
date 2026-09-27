// Two transports for the same JSON command protocol (ADR-006):
//  - WebView2 message bridge inside the Windows shell (no network listener);
//  - HTTP to the loopback dev host when running under Vite in a browser.

export interface CommandError {
  code: string;
  message: string;
  diagnostics?: { code: string; message: string }[];
}

export class TomeStackError extends Error {
  readonly code: string;
  readonly diagnostics: { code: string; message: string }[];

  constructor(error: CommandError) {
    super(error.message);
    this.name = 'TomeStackError';
    this.code = error.code;
    this.diagnostics = error.diagnostics ?? [];
  }
}

type CommandResponse = { id: string | null; ok: true; result: unknown } | { id: string | null; ok: false; error: CommandError };

export type Transport = (command: string, payload?: unknown) => Promise<unknown>;

export interface WebViewBridge {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: { data: unknown }) => void): void;
}

function unwrap(response: CommandResponse): unknown {
  if (response.ok) return response.result;
  throw new TomeStackError(response.error);
}

export function createBridgeTransport(bridge: WebViewBridge, timeoutMs = 30_000): Transport {
  let nextId = 1;
  const pending = new Map<string, { resolve: (value: unknown) => void; reject: (error: unknown) => void; timer: ReturnType<typeof setTimeout> }>();

  bridge.addEventListener('message', (event) => {
    const response = event.data as CommandResponse;
    if (!response || typeof response !== 'object' || typeof response.id !== 'string') return;
    const entry = pending.get(response.id);
    if (!entry) return;
    pending.delete(response.id);
    clearTimeout(entry.timer);
    try {
      entry.resolve(unwrap(response));
    } catch (error) {
      entry.reject(error);
    }
  });

  return (command, payload) =>
    new Promise((resolve, reject) => {
      const id = String(nextId++);
      const timer = setTimeout(() => {
        pending.delete(id);
        reject(new TomeStackError({ code: 'timeout', message: `No response to ${command}.` }));
      }, timeoutMs);
      pending.set(id, { resolve, reject, timer });
      bridge.postMessage({ id, command, payload });
    });
}

export function createHttpTransport(endpoint = '/api/command'): Transport {
  let nextId = 1;
  return async (command, payload) => {
    const id = String(nextId++);
    const response = await fetch(endpoint, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ id, command, payload }),
    });
    if (!response.ok) {
      throw new TomeStackError({
        code: `http-${response.status}`,
        message: `Dev host returned ${response.status}. Is \`dotnet run --project src/DevHost\` running?`,
      });
    }
    return unwrap((await response.json()) as CommandResponse);
  };
}

declare global {
  interface Window {
    chrome?: { webview?: WebViewBridge };
  }
}

export function detectTransport(): Transport {
  const bridge = window.chrome?.webview;
  return bridge ? createBridgeTransport(bridge) : createHttpTransport();
}
