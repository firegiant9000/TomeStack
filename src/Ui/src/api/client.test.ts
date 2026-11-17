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

describe('client: creation guides and kit (Wave 3 G8)', () => {
  it('listGuides dispatches content.guides with the family and campaign', async () => {
    const { client, calls } = recorder();
    await client.listGuides('srd-5.1', 'c1');
    await client.listGuides('srd-5.2.1');
    expect(calls).toStrictEqual([
      { command: 'content.guides', payload: { rulesFamily: 'srd-5.1', campaignId: 'c1' } },
      { command: 'content.guides', payload: { rulesFamily: 'srd-5.2.1', campaignId: undefined } },
    ]);
  });

  it('kit dispatches character.kit with the draft and the kits taken', async () => {
    const { client, calls } = recorder();
    const draft = { rulesFamily: 'srd-5.1' } as unknown as Parameters<typeof client.kit>[0];
    const kits = [
      { guideContentId: 'g1', choices: [{ pickId: 'p1', optionId: 'o1', anyOf: { 0: 'w1' } }] },
      { guideContentId: 'g2', choices: [] },
    ];
    await client.kit(draft, kits);
    expect(calls).toStrictEqual([{ command: 'character.kit', payload: { draft, kits } }]);
  });
});

describe('client: level-up options (Wave 3 G10)', () => {
  it('levelUpOptions dispatches character.levelUpOptions with the character id', async () => {
    const { client, calls } = recorder();
    await client.levelUpOptions('c1');
    expect(calls).toStrictEqual([{ command: 'character.levelUpOptions', payload: { characterId: 'c1' } }]);
  });
});

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

  it('sends no target key for a source or campaign pack export without a target', async () => {
    const { client, calls } = recorder();
    await client.exportSourcePack(['s']);
    await client.exportCampaignPack('c');
    expect(calls).toStrictEqual([
      { command: 'package.sourcePackExport', payload: { sourceIds: ['s'] } },
      { command: 'package.campaignPackExport', payload: { campaignId: 'c' } },
    ]);
  });

  it('treats each key on its own: a hash without a target is sent without one, and null sends nothing', async () => {
    const { client, calls } = recorder();
    await client.exportSourcePack(['s'], undefined, 'abc');
    await client.exportCampaignPack('c', null, 'abc');
    await client.libraryBackupSaveAs(null, 'abc');
    await client.exportSourcePack(['s'], null, null);
    expect(calls).toStrictEqual([
      { command: 'package.sourcePackExport', payload: { sourceIds: ['s'], changesHash: 'abc' } },
      { command: 'package.campaignPackExport', payload: { campaignId: 'c', changesHash: 'abc' } },
      { command: 'library.backupSaveAs', payload: { changesHash: 'abc' } },
      { command: 'package.sourcePackExport', payload: { sourceIds: ['s'] } },
    ]);
  });
});
