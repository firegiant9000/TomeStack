using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>A creation guide for the builder (content v13): what a class or background advises and offers as starting equipment.</summary>
public sealed record GuideView(Guid GuideContentId, Guid TargetContentId, IReadOnlyList<string> PrimaryAbilities, IReadOnlyList<GuidePickView> Picks);

/// <summary>One choice of a guide, with the label the builder shows.</summary>
public sealed record GuidePickView(string Id, string Label, IReadOnlyList<GuideOptionView> Options);

/// <summary>One option of a pick; <see cref="Text"/> is the sentence the builder shows for it.</summary>
public sealed record GuideOptionView(string Id, string Text, string? RequiresProficiency, IReadOnlyList<GuideLineView> Lines);

/// <summary>One line of an option. <see cref="Kind"/> is item, anyOf, coins or pack.</summary>
public sealed record GuideLineView(
    string Kind,
    Guid? ItemContentId,
    string? Name,
    int Quantity,
    string? AnyOf,
    IReadOnlyList<ContentOption>? Choices,
    Currency? Coins,
    IReadOnlyList<GuideLineView>? Contents);

/// <summary>Command <c>character.kit</c>: the builder draft and the kits taken, one per guide.</summary>
public sealed record KitRequest(Character Draft, IReadOnlyList<GuideKit> Kits);

/// <summary>The choices made for one guide's starting equipment.</summary>
public sealed record GuideKit(Guid GuideContentId, IReadOnlyList<KitChoice> Choices);

/// <summary>What the kits give: the entries (armour, shield and weapons equipped where the draft is trained), the coins, what could not be given, and the draft calculated with them.</summary>
public sealed record KitView(IReadOnlyList<EquipmentEntry> Equipment, Currency Coins, IReadOnlyList<Diagnostic> Problems, CharacterView Preview);

public sealed partial class TomeStackApp
{
    public const string KitGuideUnresolved = "kit.guide-unresolved";

    /// <summary>
    /// Command <c>character.kit</c>: expands each kit, merges the entries of the same item, and equips one body armour and one shield the draft
    /// is trained in plus every weapon. The result replaces the draft's equipment and coins in <see cref="KitView.Preview"/>. Nothing is stored.
    /// A guide that is not a published creation guide, or a line that cannot be given, is a problem; the rest is still returned.
    /// </summary>
    public KitView Kit(KitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var draft = request.Draft ?? throw new AppValidationException([new("character.draft-required", "Send the draft character to give a kit to.")]);
        var problems = new List<Diagnostic>();
        var entries = new List<EquipmentEntry>();
        var coins = new Currency();
        foreach (var kit in request.Kits ?? [])
        {
            var guide = kit?.GuideContentId is { } id && _store.NewestPublished(id) is { Kind: ContentKind.CreationGuide, Status: RevisionStatus.Published } found && found.ContentId == id ? found : null;
            if (guide?.Effects.OfType<StartingEquipmentEffect>().FirstOrDefault() is not { } effect)
            {
                problems.Add(new(KitGuideUnresolved, $"{kit?.GuideContentId} is not a published creation guide with starting equipment."));
                continue;
            }
            var result = KitExpansion.Expand(effect, kit!.Choices ?? [], _store);
            problems.AddRange(result.Problems);
            foreach (var entry in result.Equipment)
            {
                var same = entries.FindIndex(e => e.Item == entry.Item && e.Mods is null);
                if (same < 0)
                    entries.Add(entry);
                else
                    entries[same] = entries[same] with { Quantity = entries[same].Quantity + entry.Quantity };
            }
            coins = coins with { Cp = coins.Cp + result.Coins.Cp, Sp = coins.Sp + result.Coins.Sp, Ep = coins.Ep + result.Coins.Ep, Gp = coins.Gp + result.Coins.Gp, Pp = coins.Pp + result.Coins.Pp };
        }

        var equipped = Equip(draft, entries, coins);
        return new(equipped, coins, problems, Preview(draft with { Equipment = equipped, Currency = coins }));
    }

    private IReadOnlyList<EquipmentEntry> Equip(Character draft, List<EquipmentEntry> entries, Currency coins)
    {
        if (entries.Count == 0)
            return entries;
        bool IsArmor(EquipmentEntry e, bool shield) =>
            _store.FindRevision(e.Item)?.Effects.OfType<ArmorEffect>().FirstOrDefault() is { } armor && (armor.Category == ArmorCategory.Shield) == shield;
        var weapons = entries.Where(e => _store.FindRevision(e.Item)?.Effects.OfType<WeaponEffect>().Any() == true).ToList();

        // The calculated sheet is the one authority on armour training: it warns on the item when the draft is not trained in it.
        bool Trained(EquipmentEntry candidate, string untrainedCode)
        {
            var trial = entries.Select(e => e == candidate ? e with { Equipped = true } : e).ToList();
            var warnings = Preview(draft with { Equipment = trial, Currency = coins }).Sheet.Field(FieldIds.ArmorClass).Warnings;
            return !warnings.Any(w => w.Code == untrainedCode && w.Content == candidate.Item);
        }

        var chosen = new List<EquipmentEntry>(weapons);
        var body = entries.Where(e => IsArmor(e, false)).FirstOrDefault(e => Trained(e, "equipment.armor-untrained"));
        if (body is not null)
            chosen.Add(body);
        var shield = entries.Where(e => IsArmor(e, true)).FirstOrDefault(e => Trained(e, "equipment.shield-untrained"));
        if (shield is not null)
            chosen.Add(shield);
        return [.. entries.Select(e => chosen.Contains(e) ? e with { Equipped = true } : e)];
    }

    /// <summary>
    /// Command <c>content.guides</c>: the newest published creation guide of each class or background in <paramref name="rulesFamily"/>.
    /// An unknown family has no guides. <paramref name="campaignId"/> marks the item choices the campaign does not allow, as <see cref="ListContent"/> does.
    /// </summary>
    public IReadOnlyList<GuideView> ListGuides(string rulesFamily, Guid? campaignId = null)
    {
        if (!RulesFamilies.IsKnown(rulesFamily))
            return [];
        var options = ListContent(rulesFamily, campaignId).Where(o => o.Kind == ContentKind.Item && o.Compatible && !o.Superseded).ToList();
        var published = _store.ListRevisionsInOrder().Where(r => r.Status == RevisionStatus.Published).ToList();
        var items = published.Where(r => r.Kind == ContentKind.Item).ToLookup(r => r.RevisionId);
        var guides = published
            .Where(r => r.Kind == ContentKind.CreationGuide && r.RulesFamilies.Contains(rulesFamily))
            .GroupBy(r => r.ContentId)
            .Select(g => Packages.DownConversion.Newest(g)!)
            .Where(r => GuideForEffect.TargetOf(r) is not null);
        var byCategory = new Dictionary<string, IReadOnlyList<ContentOption>>(StringComparer.Ordinal);

        IReadOnlyList<ContentOption> ChoicesFor(string category)
        {
            if (!byCategory.TryGetValue(category, out var found))
            {
                found = [.. options.Where(o => items[o.Reference.RevisionId].FirstOrDefault() is { } item && KitExpansion.InCategory(item, category))];
                byCategory[category] = found;
            }
            return found;
        }

        return
        [
            .. guides.Select(g => Guides.Build(g, id => _store.NewestPublished(id) is { Kind: ContentKind.Item } item && item.ContentId == id && item.Status == RevisionStatus.Published ? item : null, ChoicesFor))
                .OrderBy(v => v.TargetContentId),
        ];
    }
}

/// <summary>Builds <see cref="GuideView"/>s from guide revisions; the texts are the service's, so the UI shows no content ids.</summary>
internal static class Guides
{
    public static GuideView Build(ContentRevision guide, Func<Guid, ContentRevision?> resolve, Func<string, IReadOnlyList<ContentOption>> choicesFor)
    {
        var kit = guide.Effects.OfType<StartingEquipmentEffect>().FirstOrDefault();
        var picks = (kit?.Picks ?? []).Where(p => p is not null).Select((p, i) => BuildPick(p, i + 1, resolve, choicesFor)).ToList();
        var abilities = guide.Effects.OfType<PrimaryAbilitiesEffect>().FirstOrDefault()?.Abilities ?? [];
        return new(guide.ContentId, GuideForEffect.TargetOf(guide)!.Value, [.. abilities], picks);
    }

    /// <summary>A pick's own label; without one, "Always" for a single option, else "Choice N" (N counts from 1).</summary>
    public static string LabelOf(KitPick pick, int number) =>
        !string.IsNullOrWhiteSpace(pick.Label) ? pick.Label : (pick.Options?.Count ?? 0) == 1 ? "Always" : $"Choice {number}";

    private static GuidePickView BuildPick(KitPick pick, int number, Func<Guid, ContentRevision?> resolve, Func<string, IReadOnlyList<ContentOption>> choicesFor) =>
        new(pick.Id, LabelOf(pick, number), [.. (pick.Options ?? []).Where(o => o is not null).Select(o => BuildOption(o, resolve, choicesFor))]);

    private static GuideOptionView BuildOption(KitOption option, Func<Guid, ContentRevision?> resolve, Func<string, IReadOnlyList<ContentOption>> choicesFor)
    {
        var lines = new List<GuideLineView>();
        var parts = new List<string>();
        var missing = 0;
        foreach (var line in option.Lines ?? [])
        {
            if (line is null)
                continue;
            var quantity = Math.Max(1, line.Quantity ?? 1);
            if (line.Item is { } target)
            {
                if (resolve(target.ContentId) is not { } item)
                {
                    missing++;
                    continue;
                }
                lines.Add(new("item", item.ContentId, item.Name, quantity, null, null, null, null));
                parts.Add(ItemText(item.Name, quantity));
            }
            else if (line.AnyOf is { } category)
            {
                lines.Add(new("anyOf", null, null, quantity, category, choicesFor(category), null, null));
                parts.Add(AnyOfText(category, quantity));
            }
            else if (line.Coins is { } coins)
            {
                var given = coins.ToCurrency();
                lines.Add(new("coins", null, null, 1, null, null, given, null));
                parts.Add("Instead: " + CoinText(given));
            }
            else if (line.Pack is { } packTarget)
            {
                if (resolve(packTarget.ContentId) is not { } pack)
                {
                    missing++;
                    continue;
                }
                var contents = new List<GuideLineView>();
                foreach (var inner in pack.Effects.OfType<ItemEffect>().SelectMany(e => e.Contains ?? []))
                {
                    if (inner?.Item is { } innerTarget && resolve(innerTarget.ContentId) is { } innerItem)
                        contents.Add(new("item", innerItem.ContentId, innerItem.Name, Math.Max(1, inner.Quantity ?? 1), null, null, null, null));
                }
                lines.Add(new("pack", pack.ContentId, pack.Name, quantity, null, null, null, contents));
                parts.Add(quantity > 1 ? $"{quantity} x {pack.Name}" : pack.Name);
            }
        }
        var text = string.Join(" and ", parts);
        if (missing > 0)
            text = (text.Length == 0 ? "" : text + " ") + $"({missing} item{(missing == 1 ? "" : "s")} unavailable)";
        return new(option.Id, text, option.RequiresProficiency, lines);
    }

    /// <summary>"Mace"; "Crossbow, light" reads "Light crossbow"; several of an item read "20 bolts" (the last word of the name, in the plural).</summary>
    private static string ItemText(string name, int quantity)
    {
        var plain = name;
        var comma = name.IndexOf(", ", StringComparison.Ordinal);
        if (comma > 0 && name.IndexOf(", ", comma + 2, StringComparison.Ordinal) < 0)
            plain = Capitalize(name[(comma + 2)..] + " " + name[..comma].ToLowerInvariant());
        if (quantity <= 1)
            return plain;
        var words = plain.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var noun = words.Length > 1 && !plain.Contains('\'') ? words[^1] : plain;
        return $"{quantity} {noun.ToLowerInvariant()}s";
    }

    private static string AnyOfText(string category, int quantity)
    {
        var kind = category switch
        {
            "weapon.simple" => "simple weapon",
            "weapon.martial" => "martial weapon",
            "armor.light" => "light armor",
            "armor.medium" => "medium armor",
            "armor.heavy" => "heavy armor",
            "armor.shield" => "shield",
            _ => category,
        };
        return quantity > 1 ? $"Any {quantity} {kind}s" : $"Any {kind}";
    }

    private static string CoinText(Currency coins)
    {
        var parts = new List<string>();
        void Add(int amount, string unit)
        {
            if (amount > 0)
                parts.Add($"{amount} {unit}");
        }
        Add(coins.Pp, "pp");
        Add(coins.Gp, "gp");
        Add(coins.Ep, "ep");
        Add(coins.Sp, "sp");
        Add(coins.Cp, "cp");
        return parts.Count == 0 ? "0 gp" : string.Join(", ", parts);
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
