import { describe, expect, it } from 'vitest';
import { createClient } from './client';
import { createBridgeTransport, TomeStackError, type WebViewBridge } from './transport';

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
