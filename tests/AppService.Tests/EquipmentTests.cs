using TomeStack.RulesCore;

namespace TomeStack.AppService.Tests;

/// <summary>
/// M2 item 4: equipped armor and shields set Armor Class, Unarmored Defense applies only without armor, and bonuses to
/// ability scores stop at 20. Brenna (SRD 5.2.1 Barbarian 3; Dex +1, Con +2; Str 17) with the original M2 fixture equipment.
/// </summary>
public class EquipmentTests
{
    /// <param name="revision">The armor fixtures are content v4 revisions (armor is typed only from v4), numbered 11 to 14.</param>
    private static ContentReference Fixture(int n, int revision) => new(Guid.Parse($"5f4dc000-0000-4000-8000-{n:D12}"), Guid.Parse($"5f4de000-0000-4000-8000-{revision:D12}"));

    private static readonly ContentReference Jerkin = Fixture(1, 11);
    private static readonly ContentReference ScaleVest = Fixture(2, 12);
    private static readonly ContentReference IronHarness = Fixture(3, 13);
    private static readonly ContentReference KiteShield = Fixture(4, 14);
    private static readonly ContentReference TomeOfMight = Fixture(5, 5);

    private static CharacterSheet Brenna(TempApp temp, params EquipmentEntry[] equipment) =>
        temp.App.SaveCharacter(TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json") with { Equipment = equipment }).Sheet;

    [Theory]
    [InlineData(1, 12)] // light: 11 + Dex 1; Unarmored Defense (13) does not apply while armor is worn
    [InlineData(2, 15)] // medium: 14 + min(Dex 1, 2)
    [InlineData(3, 17)] // heavy: 17, no Dex
    public void Worn_armor_sets_the_base_and_Unarmored_Defense_is_traced_as_not_used(int item, int expected)
    {
        using var temp = new TempApp();

        var ac = Brenna(temp, new EquipmentEntry(Fixture(item, item + 10), Equipped: true)).Field(FieldIds.ArmorClass);

        Assert.Equal(expected, ac.Value);
        Assert.Equal(AutomationStatus.Automatic, ac.Automation);
        var armor = Assert.Single(ac.Trace, t => t.Operation == "replace");
        Assert.Equal("TomeStack Fixtures: Equipment", armor.Origin.SourceTitle);
        var unarmored = Assert.Single(ac.Trace, t => t.Operation == "ignored");
        Assert.Contains("Unarmored Defense", unarmored.Description, StringComparison.Ordinal);
        Assert.Contains("only while no armor is worn", unarmored.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shield_adds_to_Unarmored_Defense_and_to_armor()
    {
        using var temp = new TempApp();

        Assert.Equal(13 + 2, Brenna(temp, new EquipmentEntry(KiteShield, Equipped: true)).Field(FieldIds.ArmorClass).Value);
        Assert.Equal(15 + 2, Brenna(temp, new(ScaleVest, Equipped: true), new(KiteShield, Equipped: true)).Field(FieldIds.ArmorClass).Value);
    }

    [Fact]
    public void Carried_but_unequipped_items_do_not_apply_but_are_exported()
    {
        using var temp = new TempApp();
        var sheet = Brenna(temp, new EquipmentEntry(IronHarness, Equipped: false, Quantity: 1));

        Assert.Equal(13, sheet.Field(FieldIds.ArmorClass).Value);
        Assert.DoesNotContain(IronHarness, sheet.Active!);
        var export = temp.App.ExportCharacters([sheet.CharacterId]);
        Assert.Contains(export.Manifest.Entries, e => e.Path == $"content/{IronHarness.RevisionId:D}.json");
    }

    [Fact]
    public void Only_one_armor_counts_and_the_extra_one_is_reported()
    {
        using var temp = new TempApp();

        var sheet = Brenna(temp, new(ScaleVest, Equipped: true), new(IronHarness, Equipped: true));

        Assert.Equal(15, sheet.Field(FieldIds.ArmorClass).Value);
        Assert.Contains(sheet.Diagnostics, d => d.Code == "equipment.multiple-armor" && d.Content == IronHarness);
    }

    [Fact]
    public void Equipping_non_item_content_is_refused_with_a_diagnostic()
    {
        using var temp = new TempApp();
        var dwarf = new ContentReference(Guid.Parse("52c00000-0000-4000-8000-000000000025"), Guid.Parse("52e00000-0000-4000-8000-000000000025")); // Primal Knowledge (a feature)

        var sheet = Brenna(temp, new EquipmentEntry(dwarf, Equipped: true));

        Assert.Contains(sheet.Diagnostics, d => d.Code == "equipment.not-an-item");
    }

    [Fact]
    public void A_bonus_cannot_raise_an_ability_score_above_20_and_the_trace_says_so()
    {
        using var temp = new TempApp();

        var strength = Brenna(temp, new EquipmentEntry(TomeOfMight, Equipped: true)).Field(FieldIds.Score(Ability.Str));

        Assert.Equal(20, strength.Value); // 15 + 4 + 2 = 21, capped. Bonuses apply in content order, so the cap lands on
        // whichever bonus crosses 20 (here the Soldier +2, chosen after the equipped Tome is admitted).
        var capped = Assert.Single(strength.Trace, t => t.Description.Contains("capped", StringComparison.Ordinal));
        Assert.Equal(20, capped.Result);
        Assert.Contains("above 20", capped.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void A_v3_armor_revision_stored_by_0_2_0_re_adds_unchanged_and_stays_reference_only()
    {
        using var temp = new TempApp();
        // 0.2.0 stored "armor" as an unknown effect, verbatim (not in the typed writer's type-first order).
        var reference = new ContentReference(Guid.Parse("5f4dc000-0000-4000-8000-0000000000bb"), Guid.Parse("5f4de000-0000-4000-8000-0000000000bb"));
        var stored = $$"""{"contentId":"{{reference.ContentId:D}}","revisionId":"{{reference.RevisionId:D}}","schemaVersion":3,"kind":"item","name":"Old Robe","rulesFamilies":["srd-5.2.1"],"provenance":{"sourceId":"5f4d5000-0000-4000-8000-000000000001"},"status":"published","effects":[{"id":"robe","type":"armor","armorClass":18,"category":"heavy"}]}""";
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={temp.App.Store.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO content_revisions (revision_id, content_id, status, sha256, json) VALUES ($rid, $cid, 'Published', $hash, $json);";
            command.Parameters.AddWithValue("$rid", reference.RevisionId.ToString("D"));
            command.Parameters.AddWithValue("$cid", reference.ContentId.ToString("D"));
            command.Parameters.AddWithValue("$hash", Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stored))));
            command.Parameters.AddWithValue("$json", stored);
            command.ExecuteNonQuery();
        }

        // Re-importing the same package (or re-seeding) must be a no-op, not an ImmutableRevisionException.
        Assert.False(temp.App.Store.AddRevision(System.Text.Json.JsonSerializer.Deserialize<ContentRevision>(stored, RulesJson.Compact)!));
        // Its meaning does not change either: it is reference only, so Unarmored Defense still applies.
        Assert.Equal(13, Brenna(temp, new EquipmentEntry(reference, Equipped: true)).Field(FieldIds.ArmorClass).Value);
    }

    [Fact]
    public void Equipment_is_validated_on_save()
    {
        using var temp = new TempApp();
        var brenna = TempApp.LoadFixture<Character>("characters/m1-acceptance-srd521-brenna.json");

        var duplicate = Assert.Throws<AppValidationException>(() => temp.App.SaveCharacter(brenna with { Equipment = [new(Jerkin), new(Jerkin)] }));
        var quantity = Assert.Throws<AppValidationException>(() => temp.App.SaveCharacter(brenna with { Equipment = [new(Jerkin, Quantity: 0)] }));

        Assert.Contains(duplicate.Problems, p => p.Code == "character.equipment-duplicate");
        Assert.Contains(quantity.Problems, p => p.Code == "character.equipment-quantity");
    }
}
