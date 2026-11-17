using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>A creation guide for the builder (content v13): what a class or background advises and offers as starting equipment.</summary>
public sealed record GuideView(Guid GuideContentId, Guid TargetContentId, IReadOnlyList<string> PrimaryAbilities, IReadOnlyList<GuidePickView> Picks);

/// <summary>One choice of a guide, with the label the builder shows.</summary>
public sealed record GuidePickView(string Id, string Label, IReadOnlyList<GuideOptionView> Options);

/// <summary>One option of a pick; <see cref="Text"/> is the sentence the builder shows for it.</summary>
public sealed record GuideOptionView(string Id, string Text, string? RequiresProficiency, IReadOnlyList<GuideLineView> Lines);

/// <summary>
/// One line of an option. <see cref="Kind"/> is item, anyOf, coins, pack or missing. A missing line is one whose item is not published
/// or not in the rules family: <see cref="ItemContentId"/> is the id the guide names and <see cref="Name"/> is the item's name when known
/// (null otherwise); the builder shows it as unavailable and cannot take it.
/// </summary>
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
public sealed record KitView(IReadOnlyList<EquipmentEntry> Equipment, Currency Coins, IReadOnlyList<Diagnostic> Problems, CharacterView Preview, IReadOnlyList<string> Trained);

public sealed partial class TomeStackApp
{
    public const string KitGuideUnresolved = "kit.guide-unresolved";

    /// <summary>A kit's guide is not for the draft's rules family.</summary>
    public const string KitGuideWrongRules = "kit.guide-wrong-rules";

    /// <summary>A kit gives an item that is not in the draft's rules family; the item is left out (it would block the save).</summary>
    public const string KitItemWrongRules = "kit.item-wrong-rules";

    /// <summary>An item chosen for an "any of" line comes from a source the draft's campaign does not allow; the choice is left out.</summary>
    public const string KitItemNotInCampaign = "kit.item-not-in-campaign";

    /// <summary>The player's wording for a RulesCore kit problem (the code is kept); the RulesCore text carries ids and category keys.</summary>
    private Diagnostic PlayerKitProblem(Diagnostic problem, string? guideName)
    {
        var name = problem.Content is { } reference ? _store.FindRevision(reference)?.Name : null;
        var text = problem.Code switch
        {
            KitExpansion.ItemUnresolved => name is null ? "An item in a starting kit isn't available, so it was left out." : $"{name} can't be given from a starting kit, so it was left out.",
            KitExpansion.ChoiceMissing => guideName is null ? "A choice in a starting kit wasn't made, so it was left out." : $"A choice in the {guideName} starting kit wasn't made, so it was left out.",
            KitExpansion.AnyOfMismatch => $"{name ?? "That item"} isn't the right kind of item for this choice, so it was left out.",
            KitExpansion.QuantityCapped => $"{name ?? "An item"} adds up to more than {EquipmentEntry.MaxQuantity:N0}, so it was capped there.",
            KitExpansion.CoinsCapped => $"The coins add up to more than {Currency.MaxCoins:N0} of one kind, so each kind was capped there.",
            _ => "Part of a starting kit couldn't be given, so it was left out.",
        };
        return problem with { Message = text };
    }

    /// <summary>A chosen option needs training the draft does not have; its lines are left out.</summary>
    public const string KitOptionUntrained = "kit.option-untrained";

    /// <summary>
    /// Command <c>character.kit</c>: expands each kit (a guide id listed twice counts once), merges the entries of the same item, and equips one
    /// body armour and one shield the draft is trained in plus every weapon. When at least one kit resolves and gives something (an entry or coins),
    /// the result replaces the draft's equipment and coins in <see cref="KitView.Preview"/> (the gear step starts from the kit); when none does, or
    /// every line of the resolved kits was dropped, the draft's own equipment and coins are returned unchanged and the draft is previewed as is
    /// (the Problems still list what was dropped). Nothing is stored.
    /// A guide that is not a published creation guide, is for another rules family, or a line that cannot be given, is a problem; the rest is still
    /// returned. A trial that the checks refuse leaves that candidate unequipped; the final preview throws for an invalid draft, as <c>character.preview</c> does.
    /// </summary>
    public KitView Kit(KitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var draft = request.Draft ?? throw new AppValidationException([new("character.draft-required", "Send the draft character to give a kit to.")]);
        var problems = new List<Diagnostic>();
        var parts = new List<KitResult>();
        var resolved = 0;
        var seen = new HashSet<Guid>();
        // The same effects the calculation uses decide what the draft is trained in; the UI only reflects this list.
        var trained = CharacterCalculator.TrainedCategories(draft, _store);
        // A draft in a campaign: item choices from a source it does not allow are not given (fixed lines stay; the sheet flags those).
        var allowedSources = draft.CampaignId is { } campaignId ? _store.FindCampaign(campaignId)?.AllowedSources.ToHashSet() : null;
        foreach (var kit in request.Kits ?? [])
        {
            if (kit is not null && !seen.Add(kit.GuideContentId))
                continue; // the same guide twice: the first wins
            var guide = kit?.GuideContentId is { } id && _store.NewestPublished(id) is { Kind: ContentKind.CreationGuide, Status: RevisionStatus.Published } found && found.ContentId == id ? found : null;
            if (guide?.Effects.OfType<StartingEquipmentEffect>().FirstOrDefault() is not { } effect)
            {
                problems.Add(new(KitGuideUnresolved, "A starting kit isn't available any more, so it was left out."));
                continue;
            }
            // The same field ListGuides filters on: a guide of another rules family would equip that family's items.
            if (!guide.RulesFamilies.Contains(draft.RulesFamily))
            {
                problems.Add(new(KitGuideWrongRules, $"{guide.Name} isn't for these rules, so its starting kit was left out."));
                continue;
            }
            resolved++;
            var choices = WithoutDisallowed(kit!.Choices ?? [], allowedSources, problems);
            var result = KitExpansion.Expand(WithoutUntrained(effect, choices, trained, problems), choices, _store);
            var kept = new List<EquipmentEntry>();
            var kitProblems = new List<Diagnostic>(result.Problems.Select(p => PlayerKitProblem(p, guide.Name)));
            foreach (var entry in result.Equipment)
            {
                // The same family check as ListGuides (Usable) and CreateCharacter (Unusable): an item of another family would block the save.
                var item = _store.FindRevision(entry.Item);
                if (item is null || !Guides.InFamily(item, draft.RulesFamily))
                {
                    kitProblems.Add(new(KitItemWrongRules, $"{item?.Name ?? "An item"} isn't available for these rules, so it was left out.", entry.Item));
                    kitProblems.RemoveAll(p => p.Code == KitExpansion.QuantityCapped && p.Content == entry.Item); // a cap on an item that is left out is no news
                }
                else
                {
                    kept.Add(entry);
                }
            }
            parts.Add(new KitResult(kept, result.Coins, kitProblems));
        }

        // Equal items merge and both sums are capped (quantity and coins), with a problem for each cap, so the final preview cannot throw on size.
        var merged = KitExpansion.Merge([.. parts]);
        // A part already capped in Expand carries its own coins problem and Merge adds another: the player is told once.
        problems.AddRange(merged.Problems.Where(p => p.Code != KitExpansion.CoinsCapped || !problems.Any(q => q.Code == KitExpansion.CoinsCapped))
            .Select(p => p.Code is KitExpansion.QuantityCapped or KitExpansion.CoinsCapped ? PlayerKitProblem(p, null) : p));
        var entries = merged.Equipment.ToList();
        var coins = merged.Coins;

        // No kit resolved, or the resolved ones gave nothing at all (every line dropped): that is "no kit", so the draft's gear stays.
        // A kit that gives only coins still replaces it.
        if (resolved == 0 || (entries.Count == 0 && coins == new Currency()))
        {
            var own = draft.Equipment ?? [];
            var ownCoins = draft.Currency ?? new Currency();
            return new(own, ownCoins, problems, Preview(draft), trained);
        }
        var equipped = Equip(draft, entries, coins);
        return new(equipped, coins, problems, Preview(draft with { Equipment = equipped, Currency = coins }), trained);
    }

    /// <summary>The choices with every item picked for an "any of" line left out when its source is not one the campaign allows (a problem for each).</summary>
    private IReadOnlyList<KitChoice> WithoutDisallowed(IReadOnlyList<KitChoice> choices, HashSet<Guid>? allowedSources, List<Diagnostic> problems)
    {
        if (allowedSources is null)
            return choices;
        var result = new List<KitChoice>();
        foreach (var choice in choices)
        {
            if (choice?.AnyOf is null)
            {
                result.Add(choice!);
                continue;
            }
            var kept = new Dictionary<int, Guid>();
            foreach (var (line, id) in choice.AnyOf)
            {
                var item = _store.NewestPublished(id);
                if (item is { Kind: ContentKind.Item } && !allowedSources.Contains(item.Provenance.SourceId))
                    problems.Add(new(KitItemNotInCampaign, $"{item.Name} isn't allowed in this campaign, so it was left out.", item.Reference));
                else
                    kept[line] = id;
            }
            result.Add(choice with { AnyOf = kept });
        }
        return result;
    }

    /// <summary>The kit's picks with the lines of a chosen option the draft is not trained for taken out (and a problem for each); the other picks are untouched.</summary>
    private static StartingEquipmentEffect WithoutUntrained(StartingEquipmentEffect effect, IReadOnlyList<KitChoice> choices, IReadOnlyList<string> trained, List<Diagnostic> problems)
    {
        var changed = false;
        var picks = new List<KitPick>();
        foreach (var pick in effect.Picks ?? [])
        {
            var options = pick?.Options ?? [];
            var choice = pick is null ? null : choices.FirstOrDefault(c => c is not null && string.Equals(c.PickId, pick.Id, StringComparison.Ordinal));
            var option = choice is not null
                ? options.FirstOrDefault(o => o is not null && string.Equals(o.Id, choice.OptionId, StringComparison.Ordinal))
                : options.Count == 1 ? options[0] : null;
            if (pick is null || option?.RequiresProficiency is not { } need || trained.Contains(need, StringComparer.Ordinal))
            {
                if (pick is not null)
                    picks.Add(pick);
                continue;
            }
            problems.Add(new(KitOptionUntrained, $"You aren't trained with {TrainedText(need)}, so this option was left out."));
            picks.Add(pick with { Options = [.. options.Select(o => ReferenceEquals(o, option) ? o with { Lines = [] } : o)] });
            changed = true;
        }
        return changed ? effect with { Picks = picks } : effect;
    }

    private static string TrainedText(string category) => category switch
    {
        "weapon.simple" => "simple weapons",
        "weapon.martial" => "martial weapons",
        "armor.light" => "light armor",
        "armor.medium" => "medium armor",
        "armor.heavy" => "heavy armor",
        "armor.shield" => "shields",
        _ => "that kind of equipment",
    };

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
            IReadOnlyList<Diagnostic> warnings;
            try
            {
                warnings = Preview(draft with { Equipment = trial, Currency = coins }).Sheet.Field(FieldIds.ArmorClass).Warnings;
            }
            catch (AppValidationException)
            {
                return false; // a trial the checks refuse is not equipped
            }
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
        var options = ListContent(rulesFamily, campaignId).Where(o => o.Kind == ContentKind.Item && o.Compatible && !o.Superseded && o.AllowedInCampaign != false).ToList();
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
            .. guides.Select(g => Guides.Build(g, rulesFamily, id => _store.NewestPublished(id) is { Kind: ContentKind.Item } item && item.ContentId == id && item.Status == RevisionStatus.Published ? item : null, ChoicesFor))
                .OrderBy(v => v.TargetContentId),
        ];
    }
}

/// <summary>Builds <see cref="GuideView"/>s from guide revisions; the texts are the service's, so the UI shows no content ids.</summary>
internal static class Guides
{
    /// <param name="resolve">The newest published item of a content id in any rules family, or null; the family check is made here.</param>
    public static GuideView Build(ContentRevision guide, string rulesFamily, Func<Guid, ContentRevision?> resolve, Func<string, IReadOnlyList<ContentOption>> choicesFor)
    {
        var kit = guide.Effects.OfType<StartingEquipmentEffect>().FirstOrDefault();
        var picks = (kit?.Picks ?? []).Where(p => p is not null).Select((p, i) => BuildPick(p, i + 1, rulesFamily, resolve, choicesFor)).ToList();
        var abilities = guide.Effects.OfType<PrimaryAbilitiesEffect>().FirstOrDefault()?.Abilities ?? [];
        return new(guide.ContentId, GuideForEffect.TargetOf(guide)!.Value, [.. abilities], picks);
    }

    /// <summary>A pick's own label; without one, "Always" for a single option, else "Choice N" (N counts from 1).</summary>
    public static string LabelOf(KitPick pick, int number) =>
        !string.IsNullOrWhiteSpace(pick.Label) ? pick.Label : (pick.Options?.Count ?? 0) == 1 ? "Always" : $"Choice {number}";

    private static GuidePickView BuildPick(KitPick pick, int number, string rulesFamily, Func<Guid, ContentRevision?> resolve, Func<string, IReadOnlyList<ContentOption>> choicesFor) =>
        new(pick.Id, LabelOf(pick, number), [.. (pick.Options ?? []).Where(o => o is not null).Select(o => BuildOption(o, rulesFamily, resolve, choicesFor))]);

    /// <summary>A line that cannot be shown: its item is not published or not in the rules family. Its text says so, with the name when it is known.</summary>
    private static string Unavailable(string? name) => $"(unavailable: {name ?? "unknown item"})";

    /// <summary>The one family check for items: used by the guide listing and by <c>character.kit</c>, and the same rule as the save's.</summary>
    internal static bool InFamily(ContentRevision revision, string rulesFamily) => revision.RulesFamilies.Contains(rulesFamily);

    private static GuideOptionView BuildOption(KitOption option, string rulesFamily, Func<Guid, ContentRevision?> resolve, Func<string, IReadOnlyList<ContentOption>> choicesFor)
    {
        var lines = new List<GuideLineView>();
        var parts = new List<string>();
        var sole = (option.Lines?.Count ?? 0) == 1;

        // The item in this family, or null; a wrong-family item still gives its name to the unavailable line.
        ContentRevision? Usable(Guid id, out ContentRevision? found)
        {
            found = resolve(id);
            return found is { } r && InFamily(r, rulesFamily) ? r : null;
        }

        foreach (var line in option.Lines ?? [])
        {
            if (line is null)
                continue;
            var quantity = Math.Max(1, line.Quantity ?? 1);
            if (line.Item is { } target)
            {
                if (Usable(target.ContentId, out var seen) is not { } item)
                {
                    lines.Add(new("missing", target.ContentId, seen?.Name, quantity, null, null, null, null));
                    parts.Add(Unavailable(seen?.Name));
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
                // Coins that are the whole option replace the choice ("Instead: 50 gp"); beside items they are one more thing given.
                parts.Add(sole ? "Instead: " + CoinText(given) : CoinText(given));
            }
            else if (line.Pack is { } packTarget)
            {
                if (Usable(packTarget.ContentId, out var seenPack) is not { } pack)
                {
                    lines.Add(new("missing", packTarget.ContentId, seenPack?.Name, quantity, null, null, null, null));
                    parts.Add(Unavailable(seenPack?.Name));
                    continue;
                }
                var contents = new List<GuideLineView>();
                var innerMissing = 0;
                foreach (var inner in pack.Effects.OfType<ItemEffect>().SelectMany(e => e.Contains ?? []))
                {
                    var innerQuantity = Math.Max(1, inner?.Quantity ?? 1);
                    if (inner?.Item is { } innerTarget && Usable(innerTarget.ContentId, out _) is { } innerItem)
                    {
                        contents.Add(new("item", innerItem.ContentId, innerItem.Name, innerQuantity, null, null, null, null));
                    }
                    else
                    {
                        innerMissing++;
                        contents.Add(new("missing", inner?.Item?.ContentId, null, innerQuantity, null, null, null, null));
                    }
                }
                lines.Add(new("pack", pack.ContentId, pack.Name, quantity, null, null, null, contents));
                var packText = quantity > 1 ? $"{quantity} × {pack.Name}" : pack.Name;
                parts.Add(innerMissing > 0 ? $"{packText} ({innerMissing} pack item{(innerMissing == 1 ? "" : "s")} unavailable)" : packText);
            }
        }
        return new(option.Id, string.Join(" and ", parts), option.RequiresProficiency, lines);
    }

    /// <summary>
    /// "Mace"; "Crossbow, light" reads "Light crossbow"; several of an item keep the whole name with its head word (the last word before any
    /// "(...)") in the plural: "20 crossbow bolts", "5 rations (1 day)". When the plural is not sure (a name already plural, "Vial of acid")
    /// it reads "2 × Name".
    /// </summary>
    internal static string ItemText(string name, int quantity)
    {
        var plain = name.Trim();
        var comma = plain.IndexOf(", ", StringComparison.Ordinal);
        if (comma > 0 && plain.IndexOf(", ", comma + 2, StringComparison.Ordinal) < 0)
            plain = Capitalize(plain[(comma + 2)..] + " " + plain[..comma].ToLowerInvariant());
        if (quantity <= 1)
            return plain;

        var tail = "";
        var body = plain;
        var paren = body.IndexOf(" (", StringComparison.Ordinal);
        if (paren > 0 && body.EndsWith(')'))
        {
            tail = body[paren..];
            body = body[..paren];
        }
        var head = body[(body.LastIndexOf(' ') + 1)..];
        var sure = head.Length > 1 && !body.Contains(" of ", StringComparison.OrdinalIgnoreCase) && !body.Contains('\'') && !body.Contains('’') && !body.Contains(',')
            && head.All(char.IsLetter) && !(head.EndsWith('s') && !head.EndsWith("ss", StringComparison.OrdinalIgnoreCase));
        if (!sure)
            return $"{quantity} × {plain}";
        var plural = Pluralize(head);
        var named = body[..^head.Length] + plural;
        // Lower-case the first letter unless the name carries other capitals (proper names stay as written).
        if (named.Skip(1).All(c => !char.IsUpper(c)))
            named = char.ToLowerInvariant(named[0]) + named[1..];
        return $"{quantity} {named}{tail}";
    }

    private static string Pluralize(string word)
    {
        var lower = word.ToLowerInvariant();
        if (lower.EndsWith("ss", StringComparison.Ordinal) || lower.EndsWith('x') || lower.EndsWith('z') || lower.EndsWith("ch", StringComparison.Ordinal) || lower.EndsWith("sh", StringComparison.Ordinal))
            return word + "es";
        if (lower.Length > 1 && lower.EndsWith('y') && !"aeiou".Contains(lower[^2]))
            return word[..^1] + "ies";
        return word + "s";
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
