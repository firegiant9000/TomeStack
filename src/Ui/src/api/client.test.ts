import { describe, expect, it } from 'vitest';
import { createClient } from './client';

function recorder() {
  const calls: { command: string; payload: unknown }[] = [];
  const client = createClient(async (command, payload) => {
    calls.push({ command, payload });
    return {};
  });
  return { client, calls };
}

describe('client: down-conversion target and changesHash', () => {
  it('packageTargets dispatches package.targets with an empty payload', async () => {
    const { client, calls } = recorder();
    await client.packageTargets();
    expect(calls).toStrictEqual([{ command: 'package.targets', payload: {} }]);
  });

  it('sends target and changesHash for character exports only when given', async () => {
    const { client, calls } = recorder();
    await client.previewExport(['a'], 'share', '0.6.0');
    await client.previewExport(['a'], 'share');
    await client.exportCharacters(['a'], 'share', '0.6.0', 'abc');
    await client.exportCharacters(['a']);
    await client.saveExportAs(['a'], 'share', '0.6.0', 'abc');
    await client.saveExportAs(['a']);
    expect(calls).toStrictEqual([
      { command: 'package.exportPreview', payload: { characterIds: ['a'], purpose: 'share', target: '0.6.0' } },
      { command: 'package.exportPreview', payload: { characterIds: ['a'], purpose: 'share' } },
      { command: 'package.export', payload: { characterIds: ['a'], purpose: 'share', target: '0.6.0', changesHash: 'abc' } },
      { command: 'package.export', payload: { characterIds: ['a'], purpose: 'backup' } },
      { command: 'package.saveAs', payload: { characterIds: ['a'], purpose: 'share', target: '0.6.0', changesHash: 'abc' } },
      { command: 'package.saveAs', payload: { characterIds: ['a'], purpose: 'backup' } },
    ]);
  });

  it('sends target and changesHash for source packs only when given', async () => {
    const { client, calls } = recorder();
    await client.sourcePackPreview(['s'], '0.6.0');
    await client.sourcePackPreview(['s']);
    await client.exportSourcePack(['s'], '0.6.0', 'abc');
    await client.saveSourcePackAs(['s'], '0.6.0', 'abc');
    await client.saveSourcePackAs(['s']);
    expect(calls).toStrictEqual([
      { command: 'package.sourcePackPreview', payload: { sourceIds: ['s'], target: '0.6.0' } },
      { command: 'package.sourcePackPreview', payload: { sourceIds: ['s'] } },
      { command: 'package.sourcePackExport', payload: { sourceIds: ['s'], target: '0.6.0', changesHash: 'abc' } },
      { command: 'package.sourcePackSaveAs', payload: { sourceIds: ['s'], target: '0.6.0', changesHash: 'abc' } },
      { command: 'package.sourcePackSaveAs', payload: { sourceIds: ['s'] } },
    ]);
  });

  it('sends target and changesHash for campaign packs only when given', async () => {
    const { client, calls } = recorder();
    await client.campaignPackPreview('c', '0.6.0');
    await client.campaignPackPreview('c');
    await client.exportCampaignPack('c', '0.6.0', 'abc');
    await client.saveCampaignPackAs('c', '0.6.0', 'abc');
    await client.saveCampaignPackAs('c');
    expect(calls).toStrictEqual([
      { command: 'package.campaignPackPreview', payload: { campaignId: 'c', target: '0.6.0' } },
      { command: 'package.campaignPackPreview', payload: { campaignId: 'c' } },
      { command: 'package.campaignPackExport', payload: { campaignId: 'c', target: '0.6.0', changesHash: 'abc' } },
      { command: 'package.campaignPackSaveAs', payload: { campaignId: 'c', target: '0.6.0', changesHash: 'abc' } },
      { command: 'package.campaignPackSaveAs', payload: { campaignId: 'c' } },
    ]);
  });

  it('sends library backup payloads only when a target is given', async () => {
    const { client, calls } = recorder();
    await client.libraryBackupPreview('0.6.0');
    await client.libraryBackupPreview();
    await client.libraryBackupSaveAs('0.6.0', 'abc');
    await client.libraryBackupSaveAs();
    expect(calls).toStrictEqual([
      { command: 'library.backupPreview', payload: { target: '0.6.0' } },
      { command: 'library.backupPreview', payload: undefined },
      { command: 'library.backupSaveAs', payload: { target: '0.6.0', changesHash: 'abc' } },
      { command: 'library.backupSaveAs', payload: undefined },
    ]);
  });
});
