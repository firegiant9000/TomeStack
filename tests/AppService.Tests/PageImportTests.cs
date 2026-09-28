using System.Text;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// SPEC I-01, I-03, MVP "Sources" (owner decision 2026-09-27: no text extraction in M2): importing a page range or a
/// whole document creates a draft reference-only entry that cites the pages. Nothing is read from the PDF, and the draft
/// is inactive until it is published (ADR-004). Published and pinned, it is a reference-only feature with its pages.
/// </summary>
public class PageImportTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% TomeStack test PDF\n%%EOF\n");

    private static (TempApp Temp, SourceRecord Source) BookWithPdf()
    {
        var temp = new TempApp();
        var source = temp.App.CreateHomebrewSource(new("Test Third-Party Book", [RulesFamilies.Srd521], "Test Publisher"));
        temp.App.AttachPdf(source.Id, "book.pdf", Pdf);
        return (temp, source);
    }

    [Fact]
    public void A_page_range_becomes_a_draft_reference_entry_that_is_inactive_until_published()
    {
        var (temp, source) = BookWithPdf();
        using var _ = temp;

        var draft = temp.App.ImportPages(new(source.Id, 12, 14, "Test Lore of the North"));

        Assert.Equal((RevisionStatus.Draft, ContentKind.Feature, new PageRef(12, 14)), (draft.Status, draft.Kind, draft.Provenance.Page!));
        Assert.Equal("Test Lore of the North", draft.Name);
        Assert.Empty(draft.Effects); // reference only: nothing is calculated from imported pages
        Assert.Equal([RulesFamilies.Srd521], draft.RulesFamilies);
        Assert.Contains(temp.App.ContentBySource(source.Id), e => e.ContentId == draft.ContentId && e.Latest.Status == RevisionStatus.Draft);

        // A draft is never active: a character pinning it gets content.unpublished.
        var character = new Character
        {
            Id = Guid.NewGuid(), Name = "Test Reader", RulesFamily = RulesFamilies.Srd521, BaseAbilities = new(10, 10, 10, 10, 10, 10), Pins = [draft.Reference],
        };
        var view = temp.App.SaveCharacter(character);
        Assert.Contains(view.Sheet.Diagnostics, d => d.Code == "content.unpublished");

        // Publishing makes a new, immutable revision (ADR-002); the character pins that one.
        var published = temp.App.Publish(draft.Reference).Published;
        var pinned = temp.App.SaveCharacter(view.Character with { Pins = [published] });
        var feature = Assert.Single(pinned.Sheet.Features!);
        Assert.Equal((AutomationStatus.Reference, new PageRef(12, 14), source.Id), (feature.Automation, feature.Origin.Page!, feature.Origin.SourceId!.Value));
    }

    [Fact]
    public void The_whole_document_and_a_single_page_can_be_imported_with_default_names()
    {
        var (temp, source) = BookWithPdf();
        using var _ = temp;

        var whole = temp.App.ImportPages(new(source.Id, WholeDocument: true));
        var single = temp.App.ImportPages(new(source.Id, 7, 7));

        Assert.Equal(("Test Third-Party Book, whole document", new PageRef(1)), (whole.Name, whole.Provenance.Page!));
        Assert.Contains("the whole document", whole.Summary, StringComparison.Ordinal);
        Assert.Equal(("Test Third-Party Book, p. 7", new PageRef(7)), (single.Name, single.Provenance.Page!));
    }

    [Fact]
    public void Imports_need_an_own_source_with_a_pdf_and_a_valid_range()
    {
        var (temp, source) = BookWithPdf();
        using var _ = temp;
        var noPdf = temp.App.CreateHomebrewSource(new("Test Notes", [RulesFamilies.Srd521]));
        var srd = temp.App.ListSources().First(s => s.License == "CC-BY-4.0");
        string Code(PageImportRequest request) => Assert.Throws<AppValidationException>(() => temp.App.ImportPages(request)).Problems[0].Code;

        Assert.Equal("source.no-pdf", Code(new(noPdf.Id, 1)));
        Assert.Equal("source.not-editable", Code(new(srd.Id, 1)));
        Assert.Equal("source.page-range-invalid", Code(new(source.Id, 5, 4)));
        Assert.Equal("source.page-range-invalid", Code(new(source.Id, 0)));
        Assert.Equal("source.not-found", Code(new(Guid.NewGuid(), 1)));
        Assert.Empty(temp.App.ContentBySource(source.Id));
    }
}
