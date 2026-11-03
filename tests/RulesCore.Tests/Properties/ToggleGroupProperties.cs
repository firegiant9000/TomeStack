using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace TomeStack.RulesCore.Tests.Properties;

/// <summary>
/// Play mechanics slice 1 (D43): whatever order and however often the forms toggles are stored, a form group has at most one
/// effective primary (the last stored), overlays follow their allowed primary, and disjoint overlays never apply together.
/// </summary>
public class ToggleGroupProperties
{
    private static readonly ActiveToggle[] Stored =
    [
        new(Fixtures.EmberForm.ContentId, "ember"), new(Fixtures.TideForm.ContentId, "tide"), new(Fixtures.SurgeOverlay.ContentId, "surge"),
        new(Fixtures.BoundOverlay.ContentId, "bound"), new(Fixtures.DeepForm.ContentId, "deep"), new(Fixtures.CalmOverlay.ContentId, "calm"),
        new(Fixtures.CalmTideOverlay.ContentId, "calm-tide"),
    ];

    private static Gen<ActiveToggle[]> StoredLists { get; } = Gen.Elements(Stored).ListOf().Select(l => l.ToArray());

    private static readonly CharacterSheet BaseSheet = ToggleGroupTests.Sheet(ToggleGroupTests.Base());

    private static bool On(CharacterSheet sheet, string id) => sheet.Toggles!.SingleOrDefault(t => t.ToggleId == id)?.On == true;

    [Property(MaxTest = 200)]
    public Property At_most_one_primary_per_group_is_effective_for_any_stored_list() =>
        Prop.ForAll(StoredLists.ToArbitrary(), stored =>
        {
            var sheet = ToggleGroupTests.Sheet(ToggleGroupTests.Forms(stored));
            var primaries = new[] { "ember", "tide", "deep" }.Where(id => On(sheet, id)).ToList();
            var lastPrimary = stored.LastOrDefault(t => t.ToggleId is "ember" or "tide"); // deep is below the character's level, so never listed or on
            var expected = lastPrimary?.ToggleId;
            // The oracle is the model, not the sheet's flags: the kept primary is the last stored; an overlay applies when it is stored
            // and its allowedWith holds the kept primary, or there is none and it is allowed in base (surge, calm).
            bool Applies(string overlay, string[] with, bool inBase) =>
                stored.Any(t => t.ToggleId == overlay) && (expected is null ? inBase : with.Contains(expected));
            var armor = (expected == "ember" ? 2 : 0) + (Applies("surge", ["tide"], true) ? 1 : 0) + (Applies("bound", ["tide"], false) ? 1 : 0);
            var initiative = (expected == "tide" ? 3 : 0) + (Applies("calm", [], true) ? 1 : 0) + (Applies("calm-tide", ["tide"], false) ? 2 : 0);
            return (primaries.Count <= 1
                    && primaries.SingleOrDefault() == expected
                    && sheet.Field(FieldIds.ArmorClass).Value - BaseSheet.Field(FieldIds.ArmorClass).Value == armor
                    && sheet.Field(FieldIds.Initiative).Value - BaseSheet.Field(FieldIds.Initiative).Value == initiative)
                .Label(string.Join(",", stored.Select(t => t.ToggleId)));
        });

    [Property(MaxTest = 200)]
    public Property Calm_and_calm_tide_are_never_both_effective() =>
        Prop.ForAll(StoredLists.ToArbitrary(), stored =>
        {
            var sheet = ToggleGroupTests.Sheet(ToggleGroupTests.Forms(stored));
            return (!(On(sheet, "calm") && On(sheet, "calm-tide"))).Label(string.Join(",", stored.Select(t => t.ToggleId)));
        });
}
