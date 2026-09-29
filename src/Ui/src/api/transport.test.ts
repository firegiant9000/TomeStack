import { afterEach, describe, expect, it, vi } from 'vitest';
import { createClient } from './client';
import { createBridgeTransport, createHttpTransport, TomeStackError, type WebViewBridge } from './transport';

function fakeBridge(reply: (message: { id: string; command: string }) => unknown) {
  let listener: ((event: { data: unknown }) => void) | undefined;
  const posted: unknown[] = [];
  const bridge: WebViewBridge = {
    postMessage(message) {
      posted.push(message);
      queueMicrotask(() => listener?.({ data: reply(message as { id: string; command: string }) }));
    },
    addEventListener(_type, handler) {
      listener = handler;
    },
  };
  return { bridge, posted };
}

describe('bridge transport', () => {
  it('correlates responses by id and returns the result', async () => {
    const { bridge, posted } = fakeBridge((m) => ({ id: m.id, ok: true, result: { echoed: m.command } }));
    const call = createBridgeTransport(bridge);

    const [a, b] = await Promise.all([call('app.info'), call('character.list')]);

    expect(a).toEqual({ echoed: 'app.info' });
    expect(b).toEqual({ echoed: 'character.list' });
    expect(posted).toEqual([
      { id: '1', command: 'app.info', payload: undefined },
      { id: '2', command: 'character.list', payload: undefined },
    ]);
  });

  it('turns error responses into TomeStackError with diagnostics', async () => {
    const { bridge } = fakeBridge((m) => ({
      id: m.id,
      ok: false,
      error: { code: 'validation', message: 'bad', diagnostics: [{ code: 'character.name-required', message: 'Name required.' }] },
    }));
    const call = createBridgeTransport(bridge);

    const error = await call('character.create', {}).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(TomeStackError);
    expect((error as TomeStackError).diagnostics[0]?.code).toBe('character.name-required');
  });

  it('ignores unrelated messages and times out', async () => {
    const { bridge } = fakeBridge(() => ({ id: 'someone-else', ok: true, result: 1 }));
    const call = createBridgeTransport(bridge, 20);

    await expect(call('app.info')).rejects.toMatchObject({ code: 'timeout' });
  });

  it('waits indefinitely when a call opts out of the timeout (a native dialog is open)', async () => {
    let answer: (() => void) | undefined;
    let listener: ((event: { data: unknown }) => void) | undefined;
    const bridge: WebViewBridge = {
      postMessage(message) {
        const { id } = message as { id: string };
        answer = () => listener?.({ data: { id, ok: true, result: { saved: true, fileName: 'a.tomestack.zip' } } });
      },
      addEventListener(_type, handler) {
        listener = handler;
      },
    };
    const call = createBridgeTransport(bridge, 20);

    const pending = call('package.saveAs', { characterIds: [] }, { timeoutMs: null });
    await new Promise((r) => setTimeout(r, 60));
    answer?.();

    await expect(pending).resolves.toEqual({ saved: true, fileName: 'a.tomestack.zip' });
  });
});

/** A DevHost stand-in: answers after `delayMs`, and rejects like the real fetch when the request is aborted. */
function stubFetch(delayMs: number, body: unknown = { id: '1', ok: true, result: 'done' }) {
  const stub = vi.fn(
    (_url: string, init: { signal: AbortSignal }) =>
      new Promise((resolve, reject) => {
        const timer = setTimeout(() => resolve({ ok: true, status: 200, json: async () => body }), delayMs);
        init.signal.addEventListener('abort', () => {
          clearTimeout(timer);
          reject(new DOMException('The operation was aborted.', 'AbortError'));
        });
      }),
  );
  vi.stubGlobal('fetch', stub);
  return stub;
}

describe('http transport', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('returns the result of a timely response', async () => {
    stubFetch(5);
    await expect(createHttpTransport('/api/command', {}, 200)('app.info')).resolves.toBe('done');
  });

  it('aborts a request that takes too long and fails with timeout, like the bridge', async () => {
    const stub = stubFetch(200);
    const call = createHttpTransport('/api/command', {}, 20);

    const error = await call('app.info').catch((e: unknown) => e);

    expect(error).toBeInstanceOf(TomeStackError);
    expect((error as TomeStackError).code).toBe('timeout');
    expect(stub.mock.calls[0]![1].signal.aborted).toBe(true);
  });

  it('honours a per-call timeout, and null waits indefinitely', async () => {
    stubFetch(60);
    const call = createHttpTransport('/api/command', {}, 20);

    await expect(call('package.saveAs', {}, { timeoutMs: null })).resolves.toBe('done');
    await expect(call('app.info', {}, { timeoutMs: 200 })).resolves.toBe('done');
    await expect(call('app.info', {}, { timeoutMs: 10 })).rejects.toMatchObject({ code: 'timeout' });
  });

  it('keeps other errors as they are', async () => {
    stubFetch(1, { id: '1', ok: false, error: { code: 'validation', message: 'bad' } });
    await expect(createHttpTransport('/api/command', {}, 200)('character.create')).rejects.toMatchObject({ code: 'validation' });
  });
});

describe('client', () => {
  it('sends package.saveAs without a timeout', async () => {
    const calls: unknown[] = [];
    const client = createClient(async (command, _payload, options) => {
      calls.push({ command, options });
      return { saved: false };
    });

    await client.saveExportAs(['x']);

    expect(calls).toEqual([{ command: 'package.saveAs', options: { timeoutMs: null } }]);
  });
});
