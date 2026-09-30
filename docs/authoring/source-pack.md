# Sharing your homebrew as a source pack

For homebrew authors. A **source pack** is a file that shares one or more of your homebrew sources with their published content, so a friend can use your class at their table. The tests (`AuthoringGuideTests`) follow these steps with the Example Lanternkeeper from [class.md](class.md): mark the source as shareable, save a pack, and import it into a clean TomeStack, where it must arrive unchanged.

## What a pack may hold

Only your own work. TomeStack enforces this, so a pack can never carry:

- a source that ever had a PDF attached, pages imported, a PDF candidate accepted, or content from an extension import (it is **import-derived** for good, even after the PDF is removed);
- a source you received from someone else (you cannot mark it as your own);
- the SRD sources that come with TomeStack (everyone has them already);
- drafts, characters, gap notes, PDFs or text read from PDFs.

The full rules are in [features/package-format.md](../features/package-format.md#source-packs-and-mark-as-shareable-m6-slice-1).

## 1. Mark the source as shareable

On the **Sources** screen, find your source and choose **Mark as shareable…**. TomeStack asks you to confirm that "the source is my own work, and it holds no text, tables or rules copied from a book, PDF or other material I did not write". Tick it and choose **Mark as shareable**. The source now says "Marked as shareable"; **Stop sharing** undoes it (files you already sent stay sent).


## 2. Publish what you want to share

A pack carries only **published** revisions. Drafts stay on your machine; the pack preview says how many.

## 3. Save the pack

Under **Share sources as a pack**, tick the sources and choose **Preview pack**. The preview names the file, counts the published revisions and drafts, and warns when your content refers to content from another source that the pack does not carry (whoever imports it needs that source too). Choose **Save pack…** and pick where to save `<title>-source-pack.tomestack.zip`.

The pack records your statement that the sources are your own work, and when you confirmed it; no user or machine name.

## 4. What your friend does

They choose **Import package…** and pick the file. The preview shows your sources and content, your statement (which TomeStack cannot verify), and the license. A copy of their library is saved before anything changes. After the import, the sources are recorded as **received**: they can use the content, share characters that use it, but never mark it as their own work or put it in their own source packs.

## When something is refused

The Sources screen says what is wrong in words; these are the codes behind the messages.

- `pack.source-not-shareable`: mark the source as shareable first.
- `pack.source-import-derived`: the source holds imported material; move your own work to a new source (write it again yourself; copied text is still copied).
- `pack.source-received`: someone else's source; only its author can share it.
- `pack.source-empty`: publish something first.
- `pack.content-spans-sources`: one entry has revisions in two sources; keep each entry in one source.

## Sharing a whole campaign

A **campaign pack** carries a campaign's rules, allowed sources and house rules, with the shareable content of its allowed sources, and only names the rest. Use **Share …** on the Campaigns screen ([features/campaigns.md](../features/campaigns.md#campaign-packs-m6-slice-2-b13)).
