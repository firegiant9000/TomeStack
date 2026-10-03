using TomeStack.RulesCore;

namespace TomeStack.AppService.CharacterImport;

/// <summary>Whether a value was on the sheet and could be read.</summary>
public enum ReadStatus
{
    Ok,

    /// <summary>The layout has no such field, or the sheet left it empty.</summary>
    Missing,

    /// <summary>The field was there, but its value could not be read (a level of 25, a score of "abc"). Never an error.</summary>
    Unreadable,
}

/// <summary>
/// One value read from a sheet. An <see cref="ReadStatus.Unreadable"/> read keeps nothing of the text it could not read; a
/// spell or item row may keep the parts it could read (its name), so the row can be listed as left out.
/// </summary>
public readonly record struct Read<T>(T? Value, ReadStatus Status)
{
    public static Read<T> Ok(T value) => new(value, ReadStatus.Ok);

    public static Read<T> Missing => new(default, ReadStatus.Missing);

    public static Read<T> Unreadable => new(default, ReadStatus.Unreadable);
}

/// <summary>One class of the class-and-level text, in the sheet's order.</summary>
public sealed record ClassText(string Name, int Level, string? Subclass);

/// <param name="Prepared">Null when the layout has no prepared mark.</param>
public sealed record SpellText(string Name, bool? Prepared);

/// <param name="Equipped">Null when the layout has no equipped mark.</param>
public sealed record ItemText(string Name, int Quantity, bool? Equipped);

public sealed record DieSpent(int Die, int Spent);

public sealed record SlotsSpent(int Level, int Spent);

/// <summary>The play state a layout can fill (D16d): only what <c>PlayState</c> has.</summary>
public sealed record DdbPlay(
    Read<int> CurrentHitPoints,
    Read<int> TemporaryHitPoints,
    IReadOnlyList<DieSpent> HitDiceSpent,
    Read<int> DeathSuccesses,
    Read<int> DeathFailures,
    Read<bool> Inspiration,
    IReadOnlyList<SlotsSpent> SpellSlotsSpent);

/// <summary>
/// What the parser read from a character sheet's form fields (<c>features/ddb-pdf-import.md</c>): semantic values only,
/// each typed or marked. Fields the layout map does not name never reach it. <see cref="Numbers"/> is keyed by calculated
/// field id (<c>proficiencyBonus</c>, <c>armorClass</c>, <c>save.str</c>, <c>skill.athletics</c>, <c>spellSlots.1</c>, …) and
/// holds only the ids the map names. <see cref="Abilities"/> and <see cref="SaveProficient"/> hold all six abilities and
/// <see cref="SkillProficient"/> all 18 skills, <see cref="ReadStatus.Missing"/> where the map has no field.
/// </summary>
public sealed record DdbSheet(
    string Layout,
    string? SuggestedFamily,
    Read<string> Name,
    Read<IReadOnlyList<ClassText>> Classes,
    Read<string> Species,
    Read<string> Background,
    IReadOnlyDictionary<Ability, Read<int>> Abilities,
    IReadOnlyDictionary<Ability, Read<bool>> SaveProficient,
    IReadOnlyDictionary<string, Read<bool>> SkillProficient,
    IReadOnlyList<Read<string>> Feats,
    IReadOnlyList<Read<SpellText>> Spells,
    IReadOnlyList<Read<ItemText>> Items,
    IReadOnlyList<Read<string>> Features,
    IReadOnlyDictionary<string, Read<int>> Numbers,
    DdbPlay Play);
