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

public sealed partial class TomeStackApp
{
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
