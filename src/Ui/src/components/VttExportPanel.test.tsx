// @vitest-environment jsdom
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { client } from '../api/client';
import { TomeStackError } from '../api/transport';
import type { VttExportPreview } from '../api/types';
import { downloadBase64 } from '../files';
import { VttExportPanel } from './VttExportPanel';

vi.mock('../api/client', () => ({
  client: {
    previewVttExport: vi.fn(),
    saveVttExportAs: vi.fn(),
    downloadVttExport: vi.fn(),
    packagePreflight: vi.fn(),
  },
}));
vi.mock('../files', () => ({ downloadBase64: vi.fn() }));

afterEach(cleanup);

function preview(target: VttExportPreview['target'], fileName: string): VttExportPreview {
  return {
    token: 't1',
    target,
    fileName,
    bytes: 1,
    adapterVersion: '1',
    purpose: 'share',
    dropped: [],
    notices: [],
    differences: [],
    warnings: [],
  };
}

beforeEach(() => {
  vi.mocked(client.packagePreflight).mockResolvedValue({ included: [], omitted: [], blocking: [], notices: [] });
  vi.mocked(client.previewVttExport).mockResolvedValue(preview('html-sheet', 'x.tomestack-sheet.html'));
});

it('offers a phone or tablet sheet and previews it as html-sheet', async () => {
  const user = userEvent.setup();
  render(<VttExportPanel characterId="c1" onError={vi.fn()} onStatus={vi.fn()} />);
  await user.click(screen.getByRole('radio', { name: /Sheet for a phone or tablet/ }));
  await user.click(screen.getByRole('button', { name: 'Preview export' }));
  expect(client.previewVttExport).toHaveBeenCalledWith('c1', 'html-sheet', 'share');
});

it('heading names other tools and devices', () => {
  render(<VttExportPanel characterId="c1" onError={vi.fn()} onStatus={vi.fn()} />);
  expect(screen.getByRole('heading', { name: 'Export to another tool or device' })).toBeTruthy();
});

it('falls back to an html download in the browser', async () => {
  const user = userEvent.setup();
  const onStatus = vi.fn();
  vi.mocked(client.saveVttExportAs).mockRejectedValue(new TomeStackError({ code: 'unsupported', message: 'Fixture: no dialog.' }));
  vi.mocked(client.downloadVttExport).mockResolvedValue({ fileName: 'x.tomestack-sheet.html', base64: 'AA==' } as never);
  render(<VttExportPanel characterId="c1" onError={vi.fn()} onStatus={onStatus} />);
  await user.click(screen.getByRole('radio', { name: /Sheet for a phone or tablet/ }));
  await user.click(screen.getByRole('button', { name: 'Preview export' }));
  const save = await screen.findByRole('button', { name: 'Save export file…' });
  await waitFor(() => expect((save as HTMLButtonElement).disabled).toBe(false));
  await user.click(save);
  await waitFor(() => expect(downloadBase64).toHaveBeenCalledWith('x.tomestack-sheet.html', 'AA==', 'text/html'));
});

it('keeps application/json for the TomeStack sheet (JSON) download', async () => {
  const user = userEvent.setup();
  vi.mocked(client.previewVttExport).mockResolvedValue(preview('sheet-json', 'x.tomestack.json'));
  vi.mocked(client.saveVttExportAs).mockRejectedValue(new TomeStackError({ code: 'unsupported', message: 'Fixture: no dialog.' }));
  vi.mocked(client.downloadVttExport).mockResolvedValue({ fileName: 'x.tomestack.json', base64: 'e30=' } as never);
  render(<VttExportPanel characterId="c1" onError={vi.fn()} onStatus={vi.fn()} />);
  await user.click(screen.getByRole('radio', { name: /TomeStack sheet \(JSON\)/ }));
  await user.click(screen.getByRole('button', { name: 'Preview export' }));
  const save = await screen.findByRole('button', { name: 'Save export file…' });
  await waitFor(() => expect((save as HTMLButtonElement).disabled).toBe(false));
  await user.click(save);
  await waitFor(() => expect(downloadBase64).toHaveBeenCalledWith('x.tomestack.json', 'e30=', 'application/json'));
});
