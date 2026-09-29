# Campaign profiles

SPEC P-01, S-03 · BACKLOG B12, B13 · MVP "Campaign" ("two profiles show different allowed content"), definition of done 5 · status: implemented (M2 item 7); campaign packs implemented and fixture-verified on the unmerged M6 slice 2 PR.

Service: `src/AppService/Campaigns.cs`, database migration v4, `campaigns/` package entries. UI: `src/Ui/src/components/CampaignsPanel.tsx`, the campaign picker in the builder, and campaign notes on the sheet. Acceptance: `tests/AppService.Tests/CampaignTests.cs` and the e2e test "shows different allowed content for two campaign profiles…".

## The profile

`{ id, name, rulesFamily, allowedSources[], houseRules? }` (`docs/schemas/campaign.v1.schema.json`), stored in the `campaigns` table. `campaign.save` creates (empty id) or updates it. Every allowed source must be installed (`campaign.source-missing`), each listed once, and the name is 1–200 characters. `campaign.delete` is refused while characters are in it (`campaign.in-use`). House rules are free text and never interpreted. The editor lists only the sources of the campaign's rules family. Changing the family drops checked sources that do not support the new one, so a hidden source is never saved (M2 review fix).

## What a campaign changes

**Never the calculation.** It changes what the builder offers, and it warns:

- `content.list { rulesFamily, campaignId }` marks each option `allowedInCampaign`. The builder lists options from other sources as "not allowed in this campaign" and disables them (SPEC S-02: the picker still shows them), so two profiles show different allowed content.
- **A deliberate exception (SPEC P-01):** "Use content from outside the campaign" plus a reason enables those options. On save, each picked revision the campaign does not allow gets a `campaignExceptions` entry with the reason and the time (character schema v4). An exception without a reason is refused (`character.exception-reason-required`).
- **Warnings on the character** (`CharacterView.campaign`): each active pin, class, chosen option or equipped item from a source the campaign does not allow gets `campaign.source-not-allowed`, or `campaign.exception` with the reason when one is recorded. Granted content follows what grants it, like the rules-family policy. A rules family other than the campaign's gets `campaign.rules-family-mismatch`, and a campaign that is not on this machine gets `campaign.missing`.
- Choosing a campaign in the builder sets the character's rules family to the campaign's.

## Packages (DoD 5)

A package includes the campaign of every exported character (`campaigns/<id>.json`, kind `campaign`, package format v4). The import preview lists it (add, unchanged or replace, with a warning when it differs), and apply stores it in the same transaction. A campaign holds no rules text, so a share includes it too. Since M6 slice 2, a character package that replaces a campaign copies the database first ([package-format.md](package-format.md) rule 10).

## Campaign packs (M6 slice 2, B13)

**Share … …** on the Campaigns screen previews and saves a **campaign pack**: the profile and the published content of the allowed homebrew you marked as your own work, with the SRD named rather than copied, and every other allowed source named in the pack's `omitted[]` with why it was left out. It never carries characters or gap notes. Importing one asks whether to keep your version of a campaign that differs or use the imported one, and lists the characters whose content the imported one would no longer allow. The rules are in [package-format.md](package-format.md#campaign-packs-m6-slice-2-b13).

**Pending sources.** An allowed source that a campaign pack left out, and that is not installed here, stays allowed (it is in `allowedSources`), and the campaign records the pack's title, publisher and license for it in `pendingSources` (`PendingSource`, campaign v1; older builds keep the field as an unknown property). The Campaigns screen says which sources a campaign is waiting for, and the editor lists them as "not installed" so you can untick them. `campaign.save` accepts a source that is not installed only when the stored campaign has it as pending, and drops the pending entry once the source is installed. Pending entries are this machine's record: a character package never adds or removes them, and a damaged list in any file is cleaned on read, never refused ([package-format.md](package-format.md#campaign-packs-m6-slice-2-b13)).

## Not in this slice

Campaign-level house-rule *mechanics* (they are notes), multiple rules families per campaign, and moving an existing character into a campaign from the sheet (set it in the builder when creating; `character.save` accepts `campaignId`).
