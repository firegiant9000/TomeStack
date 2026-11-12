using System.Text.Json;
using System.Text.Json.Serialization;

namespace TomeStack.RulesCore;

/// <summary>Content schema v13 (ADR-024): names one piece of content by its id (any revision of it), as a guide line, a pack line or a guide's subject.</summary>
public sealed record GuideTarget(Guid ContentId)
{
    /// <summary>Unknown members, kept so a later build's additions round-trip.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>
/// Content schema v13 (ADR-024): the effect of a <see cref="ContentKind.CreationGuide"/> revision that names the class or
/// background the guide advises on. Typed only in a v13 revision: below v13 it stays an <see cref="UnknownEffect"/>, byte for byte.
/// </summary>
public sealed record GuideForEffect : Effect
{
    public const string TypeName = "guideFor";

    public const int SchemaVersion = ContentRevision.CreationGuideSchemaVersion;

    public override string Type => TypeName;

    [property: JsonRequired]
    public required GuideTarget Content { get; init; }

    internal static Effect FromUnknown(UnknownEffect unknown) => VersionedEffects.Typed<GuideForEffect>(unknown);

    /// <summary>The content id a creation guide revision guides, or null when it is not a guide or has no readable guideFor.</summary>
    public static Guid? TargetOf(ContentRevision revision) =>
        revision.Kind == ContentKind.CreationGuide ? revision.Effects.OfType<GuideForEffect>().FirstOrDefault()?.Content.ContentId : null;
}

/// <summary>Content schema v13 (ADR-024): the abilities a guide recommends putting the highest scores in, by ability key (<c>str</c> to <c>cha</c>). Typed only in a v13 revision.</summary>
public sealed record PrimaryAbilitiesEffect : Effect
{
    public const string TypeName = "primaryAbilities";

    public const int SchemaVersion = ContentRevision.CreationGuideSchemaVersion;

    public override string Type => TypeName;

    [property: JsonRequired]
    public required IReadOnlyList<string> Abilities { get; init; }

    internal static Effect FromUnknown(UnknownEffect unknown) => VersionedEffects.Typed<PrimaryAbilitiesEffect>(unknown);
}

/// <summary>Content schema v13 (ADR-024): a guide's starting equipment, as picks that each offer options. Typed only in a v13 revision.</summary>
public sealed record StartingEquipmentEffect : Effect
{
    public const string TypeName = "startingEquipment";

    public const int SchemaVersion = ContentRevision.CreationGuideSchemaVersion;

    public override string Type => TypeName;

    [property: JsonRequired]
    public required IReadOnlyList<KitPick> Picks { get; init; }

    internal static Effect FromUnknown(UnknownEffect unknown) => VersionedEffects.Typed<StartingEquipmentEffect>(unknown);
}

/// <summary>One choice in a kit: pick one of its <see cref="Options"/>.</summary>
public sealed record KitPick(string Id, string? Label, IReadOnlyList<KitOption> Options)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>One option of a <see cref="KitPick"/>: its <see cref="Lines"/>, offered only to a character with <see cref="RequiresProficiency"/> when that is set.</summary>
public sealed record KitOption(string Id, IReadOnlyList<KitLine> Lines, string? RequiresProficiency = null)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>
/// One line of a kit option; exactly one of <see cref="Item"/> (with an optional <see cref="Quantity"/>), <see cref="AnyOf"/> (a
/// proficiency key such as <c>weapon.simple</c>), <see cref="Coins"/> or <see cref="Pack"/> is set (validated by a later task).
/// </summary>
public sealed record KitLine(GuideTarget? Item = null, int? Quantity = null, string? AnyOf = null, KitCoins? Coins = null, GuideTarget? Pack = null)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Content schema v13 (ADR-024): the coins of a kit line. Each denomination is written only when present, so what was written (including an explicit zero) round-trips.</summary>
public sealed record KitCoins
{
    public int? Cp { get; init; }

    public int? Sp { get; init; }

    public int? Ep { get; init; }

    public int? Gp { get; init; }

    public int? Pp { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }

    /// <summary>The coins as a <see cref="Currency"/>; an absent denomination counts as 0.</summary>
    public Currency ToCurrency() => new(Cp ?? 0, Sp ?? 0, Ep ?? 0, Gp ?? 0, Pp ?? 0);
}

/// <summary>Content schema v13 (ADR-024): one line of a pack, <see cref="ItemEffect.Contains"/>; an absent <see cref="Quantity"/> means 1.</summary>
public sealed record PackLine(GuideTarget Item, int? Quantity = null)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>The player's answer to one <see cref="KitPick"/>: the option taken, and for each <c>anyOf</c> line of it (by line index, from 0) the item chosen.</summary>
public sealed record KitChoice(string PickId, string OptionId, IReadOnlyDictionary<int, Guid>? AnyOf = null);

/// <summary>What a kit gives: new unequipped equipment entries, summed coins, and what could not be given (each problem skips only its own line).</summary>
public sealed record KitResult(IReadOnlyList<EquipmentEntry> Equipment, Currency Coins, IReadOnlyList<Diagnostic> Problems);

/// <summary>
/// Content schema v13 (ADR-024): turns a <see cref="StartingEquipmentEffect"/> and the player's <see cref="KitChoice"/>s into equipment
/// and coins. Pure: it reads published content from the catalogue and writes nothing. It may be called on unvalidated data, so no size,
/// quantity or reference is trusted.
/// </summary>
public static class KitExpansion
{
    public const string ItemUnresolved = "kit.item-unresolved";
    public const string ChoiceMissing = "kit.choice-missing";
    public const string AnyOfMismatch = "kit.any-of-mismatch";
    public const string CoinsCapped = "kit.coins-capped";

    public const string QuantityCapped = "kit.quantity-capped";

    /// <summary>
    /// Expands <paramref name="kit"/> for the given choices. Validated content is the expected input; on anything else the work and the
    /// result are bounded only by the content given (nothing is trusted, but nothing is limited either). The first choice for a pick and
    /// the first option with an id win. A quantity below 1 skips its line with <see cref="ItemUnresolved"/>. <paramref name="newId"/>
    /// makes the new entry ids (default <see cref="Guid.NewGuid"/>); pass a counter to make them deterministic.
    /// </summary>
    public static KitResult Expand(StartingEquipmentEffect kit, IReadOnlyList<KitChoice> choices, IContentCatalog catalog, Func<Guid>? newId = null)
    {
        var resolved = new Dictionary<Guid, ContentRevision?>();
        var items = new ItemTally(newId ?? Guid.NewGuid);
        var coins = new long[5];
        var problems = new List<Diagnostic>();

        ContentRevision? Resolve(Guid id)
        {
            if (!resolved.TryGetValue(id, out var found))
            {
                // Like validation: the newest published revision of exactly this content id.
                found = catalog.NewestPublished(id) is { } newest && newest.ContentId == id && newest.Status == RevisionStatus.Published ? newest : null;
                resolved[id] = found;
            }
            return found;
        }

        ContentRevision? ResolveItem(Guid id, string where)
        {
            var item = Resolve(id);
            if (item is { Kind: ContentKind.Item })
                return item;
            problems.Add(new(ItemUnresolved, $"{where} names {id}, which is not a published item."));
            return null;
        }

        // A quantity below 1 can only come from unvalidated content: the line is skipped, not rounded up.
        bool Positive(int? quantity, string where, out long amount)
        {
            amount = quantity ?? 1;
            if (amount >= 1)
                return true;
            problems.Add(new(ItemUnresolved, $"{where} has a quantity of {amount}; it must be at least 1."));
            return false;
        }

        foreach (var pick in kit.Picks ?? [])
        {
            if (pick is null)
                continue;
            var options = pick.Options ?? [];
            if (options.Count == 0)
                continue;
            // requiresProficiency on an option is not enforced here: the builder decides what to offer.
            var choice = (choices ?? []).FirstOrDefault(c => c is not null && string.Equals(c.PickId, pick.Id, StringComparison.Ordinal));
            KitOption? option;
            if (choice is not null)
            {
                option = options.FirstOrDefault(o => o is not null && string.Equals(o.Id, choice.OptionId, StringComparison.Ordinal));
                if (option is null)
                {
                    problems.Add(new(ChoiceMissing, $"Pick '{pick.Id}' has no option '{choice.OptionId}'."));
                    continue;
                }
            }
            else if (options.Count == 1)
            {
                option = options[0];
            }
            else
            {
                problems.Add(new(ChoiceMissing, $"Pick '{pick.Id}' needs a choice of option."));
                continue;
            }

            if (option is null)
                continue;
            var lines = option.Lines ?? [];
            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];
                if (line is null)
                    continue;
                var where = $"Pick '{pick.Id}' option '{option.Id}' line {index + 1}";
                if (line.Item is { } target)
                {
                    if (Positive(line.Quantity, where, out var amount) && ResolveItem(target.ContentId, where) is { } item)
                        items.Add(item.Reference, amount);
                }
                else if (line.AnyOf is { } category)
                {
                    if (choice?.AnyOf is null || !choice.AnyOf.TryGetValue(index, out var chosen))
                    {
                        problems.Add(new(ChoiceMissing, $"{where} needs an item of category {category}."));
                        continue;
                    }
                    if (!Positive(line.Quantity, where, out var amount) || ResolveItem(chosen, where) is not { } item)
                        continue;
                    if (InCategory(item, category))
                        items.Add(item.Reference, amount);
                    else
                        problems.Add(new(AnyOfMismatch, $"{where}: {item.Name} is not in category {category}.", item.Reference));
                }
                else if (line.Coins is { } given)
                {
                    long[] parts = [given.Cp ?? 0, given.Sp ?? 0, given.Ep ?? 0, given.Gp ?? 0, given.Pp ?? 0];
                    for (var i = 0; i < parts.Length; i++)
                        coins[i] += Math.Max(0, parts[i]);
                }
                else if (line.Pack is { } packTarget)
                {
                    if (!Positive(line.Quantity, where, out var lineAmount))
                        continue;
                    var pack = Resolve(packTarget.ContentId);
                    var contains = pack is { Kind: ContentKind.Item } ? pack.Effects.OfType<ItemEffect>().Select(e => e.Contains).FirstOrDefault(c => c is { Count: > 0 }) : null;
                    if (pack is null || contains is null)
                    {
                        problems.Add(new(ItemUnresolved, $"{where} names {packTarget.ContentId}, which is not a published pack."));
                        continue;
                    }
                    foreach (var packLine in contains)
                    {
                        // A pack holds items only: the pack itself and any pack inside it are not expanded.
                        if (packLine?.Item is not { } innerTarget || innerTarget.ContentId == pack.ContentId)
                        {
                            problems.Add(new(ItemUnresolved, $"{where}: {pack.Name} lists an item that cannot be given."));
                            continue;
                        }
                        if (!Positive(packLine.Quantity, $"{where}: {pack.Name}", out var innerAmount))
                            continue;
                        if (ResolveItem(innerTarget.ContentId, where) is not { } inner)
                            continue;
                        if (IsPack(inner))
                            problems.Add(new(ItemUnresolved, $"{where}: {pack.Name} holds {inner.Name}, a pack; packs are not nested.", inner.Reference));
                        else
                            items.Add(inner.Reference, lineAmount * innerAmount); // both are int-sized, so the product fits a long
                    }
                }
            }
        }

        items.ReportCapped(problems);
        var total = Cap(coins, problems);
        return new(items.ToEntries(), total, problems);
    }

    /// <summary>Combines results (a class kit and a background kit): equal items merge (the first part's entry id is kept), coins sum, problems follow in order. Sums clamp like <see cref="Expand"/>; a null part is skipped.</summary>
    public static KitResult Merge(params KitResult[] parts)
    {
        var items = new ItemTally(Guid.NewGuid);
        var coins = new long[5];
        var problems = new List<Diagnostic>();
        foreach (var part in parts ?? [])
        {
            if (part is null)
                continue;
            foreach (var entry in part.Equipment ?? [])
            {
                if (entry is not null && entry.Quantity >= 1)
                    items.Add(entry.Item, entry.Quantity, entry.Id);
            }
            var c = part.Coins;
            long[] given = [c.Cp, c.Sp, c.Ep, c.Gp, c.Pp];
            for (var i = 0; i < coins.Length; i++)
                coins[i] += Math.Max(0, given[i]);
            problems.AddRange(part.Problems ?? []);
        }
        items.ReportCapped(problems);
        var total = Cap(coins, problems);
        return new(items.ToEntries(), total, problems);
    }

    private static Currency Cap(long[] coins, List<Diagnostic> problems)
    {
        if (coins.Any(c => c > Currency.MaxCoins))
            problems.Add(new(CoinsCapped, $"The coins add up to more than {Currency.MaxCoins} of one kind; each kind is capped there."));
        var capped = coins.Select(c => (int)Math.Min(c, Currency.MaxCoins)).ToArray();
        return new(capped[0], capped[1], capped[2], capped[3], capped[4]);
    }

    private static bool IsPack(ContentRevision item) => item.Effects.OfType<ItemEffect>().Any(i => i.Contains is { Count: > 0 });

    // A weapon's or armor's own category (the same enums the attack and armor calculations use). A key outside these matches nothing.
    // The keys must stay equal to ContentValidator.GuideCategories (a test enforces it).
    internal static readonly IReadOnlyDictionary<string, Func<ContentRevision, bool>> CategoryTests = new Dictionary<string, Func<ContentRevision, bool>>(StringComparer.Ordinal)
    {
        ["weapon.simple"] = item => item.Effects.OfType<WeaponEffect>().Any(w => w.Category == WeaponCategory.Simple),
        ["weapon.martial"] = item => item.Effects.OfType<WeaponEffect>().Any(w => w.Category == WeaponCategory.Martial),
        ["armor.light"] = item => item.Effects.OfType<ArmorEffect>().Any(a => a.Category == ArmorCategory.Light),
        ["armor.medium"] = item => item.Effects.OfType<ArmorEffect>().Any(a => a.Category == ArmorCategory.Medium),
        ["armor.heavy"] = item => item.Effects.OfType<ArmorEffect>().Any(a => a.Category == ArmorCategory.Heavy),
        ["armor.shield"] = item => item.Effects.OfType<ArmorEffect>().Any(a => a.Category == ArmorCategory.Shield),
    };

    internal static bool InCategory(ContentRevision item, string category) => CategoryTests.TryGetValue(category, out var test) && test(item);

    /// <summary>Equal items (same pinned revision) in first-seen order; quantities are summed in long arithmetic and clamped to <see cref="EquipmentEntry.MaxQuantity"/> (each capped item is reported once).</summary>
    private sealed class ItemTally(Func<Guid> newId)
    {
        private readonly List<(ContentReference Item, Guid Id, long Quantity)> _rows = [];
        private readonly Dictionary<ContentReference, int> _index = [];
        private readonly List<ContentReference> _capped = [];
        private readonly HashSet<ContentReference> _cappedSet = [];

        public void Add(ContentReference item, long quantity, Guid? id = null)
        {
            if (quantity < 1)
                return;
            if (quantity > EquipmentEntry.MaxQuantity)
                MarkCapped(item);
            var amount = Math.Min(quantity, EquipmentEntry.MaxQuantity);
            if (!_index.TryGetValue(item, out var at))
            {
                _index[item] = _rows.Count;
                _rows.Add((item, id ?? newId(), amount));
                return;
            }
            var sum = _rows[at].Quantity + amount;
            if (sum > EquipmentEntry.MaxQuantity)
                MarkCapped(item);
            _rows[at] = _rows[at] with { Quantity = Math.Min(sum, EquipmentEntry.MaxQuantity) };
        }

        private void MarkCapped(ContentReference item)
        {
            if (_cappedSet.Add(item))
                _capped.Add(item);
        }

        public void ReportCapped(List<Diagnostic> problems)
        {
            foreach (var item in _capped)
                problems.Add(new(QuantityCapped, $"An item's quantity adds up to more than {EquipmentEntry.MaxQuantity}; it is capped there.", item));
        }

        public IReadOnlyList<EquipmentEntry> ToEntries() => [.. _rows.Select(r => new EquipmentEntry(r.Item, false, (int)r.Quantity, r.Id))];
    }
}
