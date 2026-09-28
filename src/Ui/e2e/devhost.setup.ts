// Starts the real DevHost (Release build) on a free loopback port with a throwaway data folder, for the UI flow
// test. Run after `dotnet build -c Release` (see `npm run test:e2e`).
import { spawn, type ChildProcess } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { TestProject } from 'vitest/node';

declare module 'vitest' {
  export interface ProvidedContext {
    devHost: { endpoint: string; token: string; dataDir: string };
  }
}

const here = fileURLToPath(new URL('.', import.meta.url));
const devHostDll = resolve(here, '../../DevHost/bin/Release/net10.0/TomeStack.DevHost.dll');

function freePort(): Promise<number> {
  return new Promise((done, fail) => {
    const server = createServer();
    server.once('error', fail);
    server.listen(0, '127.0.0.1', () => {
      const address = server.address();
      server.close(() => (typeof address === 'object' && address ? done(address.port) : fail(new Error('no port'))));
    });
  });
}

async function waitForHealth(url: string, child: ChildProcess, timeoutMs = 30_000): Promise<void> {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    if (child.exitCode !== null) throw new Error(`DevHost exited with code ${child.exitCode}`);
    try {
      if ((await fetch(url)).ok) return;
    } catch {
      // not listening yet
    }
    await new Promise((r) => setTimeout(r, 200));
  }
  throw new Error(`DevHost did not become healthy at ${url}`);
}

export default async function setup(project: TestProject) {
  if (!existsSync(devHostDll)) {
    throw new Error(`DevHost build not found at ${devHostDll}. Run 'dotnet build TomeStack.slnx -c Release' first.`);
  }
  const dataDir = mkdtempSync(join(tmpdir(), 'tomestack-e2e-'));
  const port = await freePort();
  const child = spawn('dotnet', [devHostDll], {
    env: { ...process.env, TOMESTACK_DATA_DIR: dataDir, TOMESTACK_DEV_PORT: String(port), ASPNETCORE_ENVIRONMENT: 'Development' },
    stdio: 'ignore',
    windowsHide: true,
  });
  try {
    await waitForHealth(`http://127.0.0.1:${port}/api/health`, child);
  } catch (error) {
    child.kill();
    throw error;
  }
  const token = readFileSync(join(dataDir, 'devhost.token'), 'utf8').trim();
  project.provide('devHost', { endpoint: `http://127.0.0.1:${port}/api/command`, token, dataDir });

  return () => {
    child.kill();
    try {
      rmSync(dataDir, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
    } catch {
      // best effort; Windows may still hold the SQLite file for a moment
    }
  };
}
