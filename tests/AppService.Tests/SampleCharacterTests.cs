using System.Text.Json;
using TomeStack.AppService.Persistence;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>Wave 3 (Task 9.2, D95): the bundled sample character and its removal. Invented content only.</summary>
public class SampleCharacterTests
{
    private static string CodeOf(Action act) =>
        Assert.Throws<AppValidationException>(act).Problems[0].Code;

    private static Character Normal(TempApp temp) =>
        temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/srd521-courier.json") with { Id = Guid.NewGuid(), Name = "Test Courier" }).Character;

    [Fact]
    public void The_sample_saves_clean_with_every_choice_answered()
    {
        using var temp = new TempApp();

        var view = temp.App.CreateSample();

        Assert.Equal("Wren Ashdown", view.Character.Name);
        Assert.Equal(RulesFamilies.Srd51, view.Character.RulesFamily);
        Assert.Equal(3, view.Character.TotalLevel);
        var sheet = view.Sheet;
        Assert.Empty(sheet.Diagnostics);
        Assert.All(sheet.Choices!, c => Assert.True(c.Resolved, $"{c.SourceName} {c.ChoiceId}"));
        // Scale mail's Stealth disadvantage is a rules reminder the armour always carries, not a defect in the sample.
        Assert.DoesNotContain(sheet.Fields.SelectMany(f => f.Warnings), w => w.Code != "equipment.stealth-disadvantage");
        Assert.Empty(sheet.Spellcasting!.SelectMany(s => s.Warnings));
        Assert.Equal(16, sheet.Field(FieldIds.ArmorClass).Value);
        Assert.Contains(sheet.Attacks!, a => a.Name == "Mace");
        Assert.Contains(sheet.Spellcasting!.SelectMany(s => s.Spells), s => s.Level > 0 && s.Prepared);
        Assert.NotEmpty(sheet.SpellSlots!);
        Assert.Contains(sheet.Resources!, r => r.Label.Contains("Channel Divinity", StringComparison.OrdinalIgnoreCase) || r.ContentName.Contains("Channel Divinity", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(temp.App.GetCharacter(view.Character.Id).Character.Name, view.Character.Name);
    }

    [Fact]
    public void The_list_carries_the_origin_of_the_sample_only()
    {
        using var temp = new TempApp();
        var normal = Normal(temp);
        var sample = temp.App.CreateSample().Character;

        var list = temp.App.ListCharacters();

        Assert.Equal("sample", list.Single(c => c.Id == sample.Id).Origin);
        Assert.Null(list.Single(c => c.Id == normal.Id).Origin);
    }

    [Fact]
    public void A_second_sample_is_refused_even_when_the_first_is_archived()
    {
        using var temp = new TempApp();
        var sample = temp.App.CreateSample().Character;
        Assert.Equal("character.sample-exists", CodeOf(() => temp.App.CreateSample()));

        temp.App.Archive(new(sample.Id, true));

        Assert.Equal("character.sample-exists", CodeOf(() => temp.App.CreateSample()));
        Assert.Equal("sample", Assert.Single(temp.App.ListCharacters(), c => c.Id == sample.Id).Origin);
    }

    [Fact]
    public async Task Concurrent_creates_make_exactly_one_sample()
    {
        using var temp = new TempApp();
        using var barrier = new Barrier(4);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            try
            {
                temp.App.CreateSample();
                return "ok";
            }
            catch (AppValidationException e)
            {
                return e.Problems[0].Code;
            }
        })));

        Assert.Equal(1, results.Count(r => r == "ok"));
        Assert.Equal(3, results.Count(r => r == "character.sample-exists"));
        Assert.Single(temp.App.ListCharacters(), c => c.Origin == "sample");
    }

    [Fact]
    public void Deleting_a_normal_character_is_refused_and_it_survives()
    {
        using var temp = new TempApp();
        var normal = Normal(temp);

        Assert.Equal("character.not-sample", CodeOf(() => temp.App.DeleteSample(new(normal.Id, true))));

        Assert.NotNull(temp.App.Store.FindCharacter(normal.Id));
    }

    [Fact]
    public void Deleting_an_unknown_character_is_not_found()
    {
        using var temp = new TempApp();

        Assert.Equal("character.not-found", CodeOf(() => temp.App.DeleteSample(new(Guid.NewGuid(), true))));
    }

    [Fact]
    public void An_archived_sample_can_be_deleted_and_made_again()
    {
        using var temp = new TempApp();
        var sample = temp.App.CreateSample().Character;
        temp.App.Archive(new(sample.Id, true));

        temp.App.DeleteSample(new(sample.Id, true));

        Assert.Null(temp.App.Store.FindCharacter(sample.Id));
        Assert.Equal("Wren Ashdown", temp.App.CreateSample().Character.Name);
    }

    [Fact]
    public void Deleting_without_confirmation_removes_nothing()
    {
        using var temp = new TempApp();
        var sample = temp.App.CreateSample().Character;

        Assert.Equal("character.confirm-required", CodeOf(() => temp.App.DeleteSample(new(sample.Id, false))));

        Assert.NotNull(temp.App.Store.FindCharacter(sample.Id));
    }

    [Fact]
    public void A_sample_with_a_snapshot_cannot_be_deleted()
    {
        using var temp = new TempApp();
        var sample = temp.App.CreateSample().Character;
        var store = temp.App.Store;
        store.InTransaction(() => store.AddSnapshot(new CharacterSnapshot(Guid.NewGuid(), sample.Id, TempApp.Now, SnapshotReason.Manual, null, sample)));

        var error = Assert.Throws<AppValidationException>(() => temp.App.DeleteSample(new(sample.Id, true)));

        Assert.Equal("character.sample-has-snapshots", error.Problems[0].Code);
        Assert.Equal("This sample has snapshots, so it can only be archived", error.Problems[0].Message);
        Assert.NotNull(store.FindCharacter(sample.Id));
    }

    [Fact]
    public void The_store_refusal_maps_to_the_same_code()
    {
        // The window between the service's snapshot check and the delete: the store's own refusal is a dedicated exception.
        using var temp = new TempApp();
        var sample = temp.App.CreateSample().Character;
        var store = temp.App.Store;
        store.InTransaction(() => store.AddSnapshot(new CharacterSnapshot(Guid.NewGuid(), sample.Id, TempApp.Now, SnapshotReason.Manual, null, sample)));

        Assert.Throws<SampleHasSnapshotsException>(() => store.DeleteSampleCharacter(sample.Id));
    }

    [Fact]
    public void The_exported_package_has_no_origin()
    {
        using var temp = new TempApp();
        var sample = temp.App.CreateSample().Character;

        var content = temp.App.ExportCharacters([sample.Id]).Content;

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(content));
        var json = zip.Entries.Where(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(json);
        foreach (var entry in json)
        {
            using var stream = entry.Open();
            using var document = JsonDocument.Parse(stream);
            Assert.False(HasOrigin(document.RootElement), entry.FullName);
        }
    }

    private static bool HasOrigin(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Any(p => p.Name.Equals("origin", StringComparison.OrdinalIgnoreCase) || HasOrigin(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Any(HasOrigin),
        _ => false,
    };

    [Fact]
    public void Both_commands_are_transport_neutral_json()
    {
        using var temp = new TempApp();
        var dispatcher = new CommandDispatcher(temp.App);
        JsonElement Send(string command, object payload) =>
            JsonDocument.Parse(dispatcher.Dispatch(JsonSerializer.Serialize(new { id = "a", command, payload }, RulesJson.Compact))).RootElement;

        var created = Send("character.createSample", new { });
        Assert.True(created.GetProperty("ok").GetBoolean(), created.ToString());
        var id = created.GetProperty("result").GetProperty("character").GetProperty("id").GetString();
        Assert.Equal("sample", Send("character.list", new { }).GetProperty("result").EnumerateArray().Single().GetProperty("origin").GetString());

        var again = Send("character.createSample", new { });
        Assert.False(again.GetProperty("ok").GetBoolean());
        Assert.Equal("character.sample-exists", again.GetProperty("error").GetProperty("diagnostics")[0].GetProperty("code").GetString());

        Assert.False(Send("character.deleteSample", new { characterId = id }).GetProperty("ok").GetBoolean());
        var deleted = Send("character.deleteSample", new { characterId = id, confirm = true });
        Assert.True(deleted.GetProperty("ok").GetBoolean(), deleted.ToString());
        Assert.Empty(Send("character.list", new { }).GetProperty("result").EnumerateArray());
        Assert.Contains("character.createSample", CommandDispatcher.Commands);
        Assert.Contains("character.deleteSample", CommandDispatcher.Commands);
    }
}
