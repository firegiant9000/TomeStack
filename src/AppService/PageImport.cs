using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <param name="End">The last page, or null for a single page. A whole document is <c>Start = 1</c> with <paramref name="WholeDocument"/>.</param>
/// <param name="Title">The entry's name; by default the source title and the pages.</param>
public sealed record PageImportRequest(Guid SourceId, int Start = 1, int? End = null, string? Title = null, bool WholeDocument = false);

public sealed partial class TomeStackApp
{
    /// <summary>A bound on page numbers (untrusted input); TomeStack does not read the PDF, so it cannot check the real last page.</summary>
    public const int MaxPage = 100_000;

    /// <summary>
    /// <c>source.importPages</c> (SPEC I-01, I-03; MVP "Sources"; owner decision 2026-09-27: no text extraction in M2): a
    /// page range, or the whole document, of a source's attached PDF becomes a **draft** reference-only entry. The entry
    /// has no effects: it cites the pages, and its "Open page" link opens them. Nothing is read from the PDF, and the draft
    /// is inactive until the player reviews and publishes it in the studio (ADR-004). Only the user's own sources (made in
    /// TomeStack) take imports; a bundled source such as an SRD pack is refused.
    /// </summary>
    public ContentRevision ImportPages(PageImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = _store.FindSource(request.SourceId)
            ?? throw new AppValidationException([new("source.not-found", $"Source {request.SourceId} does not exist.")]);
        if (source.EditionVersion != "homebrew")
            throw new AppValidationException([new("source.not-editable", $"'{source.Title}' is a bundled source. Create your own source for the book in the studio, attach the PDF to it, then import pages there.")]);
        if (GetAttachment(source.Id) is not { Status: "available" or "changed" })
            throw new AppValidationException([new("source.no-pdf", $"'{source.Title}' has no available PDF. Attach it first; imported pages open in it.")]);
        var end = request.WholeDocument ? null : request.End;
        if (request.Start is < 1 or > MaxPage || end is < 1 or > MaxPage || end < request.Start)
            throw new AppValidationException([new("source.page-range-invalid", $"Pages must be between 1 and {MaxPage}, and the last page cannot come before the first.")]);
        var pages = request.WholeDocument ? new PageRef(1) : new PageRef(request.Start, end == request.Start ? null : end);
        var what = request.WholeDocument ? "the whole document" : pages.ToString();
        var title = string.IsNullOrWhiteSpace(request.Title) ? $"{source.Title}, {(request.WholeDocument ? "whole document" : pages.ToString())}" : request.Title.Trim();
        if (title.Length > MaxSourceTitleLength)
            throw new AppValidationException([new("content.name-too-long", $"The title can have at most {MaxSourceTitleLength} characters.")]);

        var draft = new ContentRevision
        {
            ContentId = Guid.NewGuid(),
            RevisionId = Guid.NewGuid(),
            Kind = ContentKind.Feature,
            Name = title,
            RulesFamilies = source.RulesFamilies,
            Provenance = new(source.Id, pages),
            Status = RevisionStatus.Draft,
            Summary = $"Reference: {what} of {source.Title}. Imported as a page reference; the text is in the PDF, and nothing is calculated from it.",
        };
        _store.InTransaction(() =>
        {
            MarkImportDerived(source.Id); // M6 slice 1: the entry cites the PDF's pages
            SaveDraft(draft);
        });
        return draft;
    }
}
