using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M3 B3: session gap notes are stored locally (DB schema v5), never change the character, travel only in a personal
/// backup, and are never quoted in an error message.
/// </summary>
public class GapNoteTests
{
    private const string Secret = "Fixture private note text 7c1e";

    private static (TempApp Temp, CharacterView View) Brenna()
    {
        var temp = new TempApp();
        var brenna = TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json");
        return (temp, temp.App.SaveCharacter(brenna));
    }

    private static GapTarget FeatureOf(CharacterView view) => new(GapTargetKind.Feature, ContentId: view.Sheet.Features![0].Content.ContentId);

    [Fact]
    public void A_note_on_a_feature_or_field_is_stored_with_its_label_and_leaves_the_character_alone()
    {
        var (temp, view) = Brenna();
        using var _ = temp;
        var before = TempApp.Json(temp.App.GetCharacter(view.Character.Id).Character);

        var feature = temp.App.AddGapNote(new(view.Character.Id, FeatureOf(view) with { Label = "ignored" }, "  Rage should end early here.  "));
        var field = temp.App.AddGapNote(new(view.Character.Id, new(GapTargetKind.Field, FieldId: FieldIds.ArmorClass), "AC misses a table bonus."));

        Assert.Equal(view.Sheet.Features![0].Name, feature.Target.Label);
        Assert.Equal("Rage should end early here.", feature.Text);
        Assert.Equal(view.Sheet.Field(FieldIds.ArmorClass).Label, field.Target.Label);
        Assert.Equal(GapNoteStatus.Open, feature.Status);
        Assert.Equal(before, TempApp.Json(temp.App.GetCharacter(view.Character.Id).Character));

        temp.Reopen();
        Assert.Equal(new[] { feature.Id, field.Id }.Order(), temp.App.ListGapNotes(view.Character.Id).Select(n => n.Id).Order());
    }

    [Fact]
    public void Notes_of_every_character_list_together_with_their_names_open_first_and_the_list_writes_nothing()
    {
        // M3 C5: gap.listAll, the list across characters.
        var (temp, brenna) = Brenna();
        using var _ = temp;
        var korga = temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/m1-acceptance-srd51-korga.json"));
        var resolved = temp.App.AddGapNote(new(brenna.Character.Id, new(GapTargetKind.Field, FieldId: FieldIds.ArmorClass), "First note."));
        temp.App.SetGapNoteStatus(new(resolved.Id, GapNoteStatus.Resolved));
        var open = temp.App.AddGapNote(new(korga.Character.Id, FeatureOf(korga), "Second note."));
        var before = TempApp.Json(temp.App.ListCharacters());

        var listed = temp.App.ListAllGapNotes();
        var response = JsonDocument.Parse(new CommandDispatcher(temp.App).Dispatch("""{"id":"1","command":"gap.listAll","payload":{}}""")).RootElement;

        Assert.Equal([(open.Id, korga.Character.Name), (resolved.Id, brenna.Character.Name)], listed.Select(l => (l.Note.Id, l.CharacterName)));
        Assert.Equal([GapNoteStatus.Open, GapNoteStatus.Resolved], listed.Select(l => l.Note.Status));
        Assert.True(response.GetProperty("ok").GetBoolean());
        Assert.Equal(korga.Character.Name, response.GetProperty("result")[0].GetProperty("characterName").GetString());
        Assert.Equal(before, TempApp.Json(temp.App.ListCharacters()));
    }

    [Fact]
    public void A_note_on_an_effect_names_the_feature_and_the_effect()
    {
        var (temp, view) = Brenna();
        using var _ = temp;
        var withEffect = view.Sheet.Features!.First(f => f.Effects.Count > 0);
        var effect = withEffect.Effects[0];

        var note = temp.App.AddGapNote(new(view.Character.Id, new(GapTargetKind.Feature, withEffect.Content.ContentId, effect.Id), "Needs a toggle."));

        Assert.Equal($"{withEffect.Name}: {effect.Label ?? effect.Id}", note.Target.Label);
    }

    [Fact]
    public void Bad_notes_are_refused_without_quoting_their_text()
    {
        var (temp, view) = Brenna();
        using var _ = temp;
        var id = view.Character.Id;

        string Code(AddGapNoteRequest request)
        {
            var refused = Assert.Throws<AppValidationException>(() => temp.App.AddGapNote(request));
            Assert.DoesNotContain(Secret, refused.Message, StringComparison.Ordinal);
            return refused.Problems[0].Code;
        }

        Assert.Equal("gap.text-required", Code(new(id, FeatureOf(view), "   ")));
        Assert.Equal("gap.text-required", Code(new(id, FeatureOf(view), new string('x', GapNote.MaxTextLength + 1))));
        Assert.Equal("gap.target-not-found", Code(new(id, new(GapTargetKind.Feature, Guid.NewGuid()), Secret)));
        Assert.Equal("gap.target-not-found", Code(new(id, new(GapTargetKind.Field, FieldId: "no-such-field"), Secret)));
        Assert.Equal("gap.target-not-found", Code(new(id, FeatureOf(view) with { EffectId = "no-such-effect" }, Secret)));
        Assert.Equal("gap.target-invalid", Code(new(id, new(GapTargetKind.Field, ContentId: Guid.NewGuid(), FieldId: FieldIds.ArmorClass), Secret)));
        Assert.Equal("gap.target-required", Code(new(id, null!, Secret)));
        Assert.Equal("character.not-found", Assert.Throws<AppValidationException>(() => temp.App.AddGapNote(new(Guid.NewGuid(), FeatureOf(view), Secret))).Problems[0].Code);
        Assert.Empty(temp.App.ListGapNotes(id));
    }

    [Fact]
    public void Resolving_sorts_a_note_after_the_open_ones_and_deleting_needs_confirmation()
    {
        var (temp, view) = Brenna();
        using var _ = temp;
        var id = view.Character.Id;
        var first = temp.App.AddGapNote(new(id, FeatureOf(view), "First."));
        var second = temp.App.AddGapNote(new(id, FeatureOf(view), "Second."));

        temp.App.SetGapNoteStatus(new(first.Id, GapNoteStatus.Resolved));
        Assert.Equal([second.Id, first.Id], temp.App.ListGapNotes(id).Select(n => n.Id));

        Assert.Equal("gap.confirmation-required", Assert.Throws<AppValidationException>(() => temp.App.DeleteGapNote(new(first.Id))).Problems[0].Code);
        Assert.Equal(2, temp.App.ListGapNotes(id).Count);
        temp.App.DeleteGapNote(new(first.Id, Confirm: true));
        Assert.Equal([second.Id], temp.App.ListGapNotes(id).Select(n => n.Id));
        Assert.Equal("gap.not-found", Assert.Throws<AppValidationException>(() => temp.App.DeleteGapNote(new(first.Id, Confirm: true))).Problems[0].Code);
        Assert.Equal("gap.not-found", Assert.Throws<AppValidationException>(() => temp.App.SetGapNoteStatus(new(first.Id, GapNoteStatus.Open))).Problems[0].Code);
    }

    [Fact]
    public void A_backup_carries_the_notes_and_a_share_never_does()
    {
        var (temp, view) = Brenna();
        using var _ = temp;
        var id = view.Character.Id;
        var note = temp.App.AddGapNote(new(id, FeatureOf(view), Secret));

        Assert.Equal(1, temp.App.PreviewExport([id], ExportPurpose.Backup).GapNotes);
        Assert.Equal(0, temp.App.PreviewExport([id], ExportPurpose.Share).GapNotes);

        var share = temp.App.ExportCharacters([id], ExportPurpose.Share);
        Assert.DoesNotContain(share.Manifest.Entries, e => e.Path.StartsWith("gaps/", StringComparison.Ordinal));
        Assert.DoesNotContain(Secret, EntireText(share.Content), StringComparison.Ordinal);

        var backup = temp.App.ExportCharacters([id], ExportPurpose.Backup);
        Assert.Contains(backup.Manifest.Entries, e => e.Path == $"gaps/{note.Id:D}.json" && e.Kind == "gapNote");
        Assert.Equal(PackageManifest.CharacterFormatVersion, backup.Manifest.FormatVersion);

        using var clean = new TempApp();
        var preview = clean.App.PreviewImport(backup.Content);
        Assert.True(preview.CanApply);
        Assert.Contains(preview.Items, i => i.Kind == "gapNote" && i.Id == note.Id && i.Action == PackageItemAction.Add);
        clean.App.ApplyImport(backup.Content);
        Assert.Equal(TempApp.Json(note), TempApp.Json(clean.App.ListGapNotes(id).Single()));

        // Importing the same backup again changes nothing.
        Assert.Contains(clean.App.PreviewImport(backup.Content).Items, i => i.Kind == "gapNote" && i.Action == PackageItemAction.Unchanged);
    }

    [Fact]
    public void A_share_package_that_carries_notes_is_refused()
    {
        var (temp, view) = Brenna();
        using var _ = temp;
        temp.App.AddGapNote(new(view.Character.Id, FeatureOf(view), Secret));
        var relabelled = WithManifest(temp.App.ExportCharacters([view.Character.Id]).Content, m => m["purpose"] = JsonSerializer.SerializeToNode(ExportPurpose.Share, RulesJson.Options));

        using var clean = new TempApp();
        var preview = clean.App.PreviewImport(relabelled);

        Assert.False(preview.CanApply);
        Assert.Contains(preview.Errors, e => e.Code == "package.gap-notes-not-allowed");
        Assert.DoesNotContain(preview.Errors, e => e.Message.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public void Through_the_dispatcher_a_refused_note_is_not_echoed_or_logged()
    {
        var (temp, view) = Brenna();
        using var _ = temp;
        var dispatcher = new CommandDispatcher(temp.App);
        var request = JsonSerializer.Serialize(new
        {
            id = "1",
            command = "gap.add",
            payload = new { characterId = view.Character.Id, target = new { kind = "field", fieldId = "no-such-field" }, text = Secret },
        });

        var response = dispatcher.Dispatch(request);

        Assert.Contains("gap.target-not-found", response, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, response, StringComparison.Ordinal);
        var log = Path.Combine(temp.Directory, "logs", FileErrorLog.FileName);
        Assert.False(File.Exists(log) && File.ReadAllText(log).Contains(Secret, StringComparison.Ordinal));
    }

    private static string EntireText(byte[] package)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        return string.Concat(zip.Entries.Select(e =>
        {
            using var reader = new StreamReader(e.Open());
            return reader.ReadToEnd();
        }));
    }

    private static byte[] WithManifest(byte[] package, Action<JsonObject> change)
    {
        using var buffer = new MemoryStream();
        buffer.Write(package);
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("manifest.json")!;
            JsonObject manifest;
            using (var reader = new StreamReader(entry.Open()))
                manifest = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
            change(manifest);
            entry.Delete();
            using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            writer.Write(manifest.ToJsonString());
        }
        return buffer.ToArray();
    }
}
