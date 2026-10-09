using System.Globalization;
using System.Security.Cryptography;

namespace TomeStack.RulesCore;

/// <summary>Bounds for untrusted dice expressions (SPEC Q-02).</summary>
public static class DiceLimits
{
    public const int MaxLength = 40;
    public const int MaxTerms = 8;
    public const int MaxDice = 100;
    public const int MinSides = 2;
    public const int MaxSides = 1000;
    public const int MaxConstant = 1000;
}

public enum RollMode { Normal, Advantage, Disadvantage }

/// <summary>Source of die results. Implementations return a uniform value in 1..<paramref name="sides"/>.</summary>
public interface IRandomSource
{
    int Next(int sides);
}

/// <summary>Unbiased dice from the OS CSPRNG, for real play.</summary>
public sealed class SystemRandomSource : IRandomSource
{
    public static SystemRandomSource Instance { get; } = new();

    public int Next(int sides) => RandomNumberGenerator.GetInt32(1, sides + 1);
}

/// <summary>
/// Deterministic dice for tests and reproducible examples. SplitMix64 is used instead of <see cref="Random"/>, whose
/// seeded sequence is not guaranteed across .NET versions. Rejection sampling avoids modulo bias.
/// </summary>
public sealed class SeededRandomSource(ulong seed) : IRandomSource
{
    private ulong _state = seed;

    public int Next(int sides)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sides, 1);
        var bound = (ulong)sides;
        var limit = ulong.MaxValue - (ulong.MaxValue % bound);
        ulong value;
        do
        {
            value = NextUInt64();
        }
        while (value >= limit);
        return (int)(value % bound) + 1;
    }

    private ulong NextUInt64()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}

/// <summary>One term of a dice expression: <c>NdM</c> (Sides &gt; 0) or a signed constant (Sides == 0).</summary>
public sealed record DiceTerm(int Sign, int Count, int Sides, int Constant)
{
    public bool IsDice => Sides > 0;
}

/// <summary>A parsed <c>NdM+K</c>-style expression, e.g. <c>1d20+5</c>, <c>2d6</c>, <c>1d8+1d6+2</c>, <c>d20-1</c>.</summary>
public sealed class DiceExpression
{
    private DiceExpression(string source, IReadOnlyList<DiceTerm> terms)
    {
        Source = source;
        Terms = terms;
    }

    public string Source { get; }

    public IReadOnlyList<DiceTerm> Terms { get; }

    /// <summary>True for a single <c>1d20</c> plus constants: the only shape advantage/disadvantage applies to.</summary>
    public bool IsD20Test => Terms.Count(t => t.IsDice) == 1 && Terms.Single(t => t.IsDice) is { Count: 1, Sides: 20, Sign: 1 };

    public static bool TryParse(string? source, out DiceExpression? expression, out FormulaError? error)
    {
        expression = null;
        if (string.IsNullOrWhiteSpace(source))
            return Fail("dice.empty", "The dice expression is empty.", out error);
        if (source.Length > DiceLimits.MaxLength)
            return Fail("dice.too-long", $"The dice expression is {source.Length} characters; the limit is {DiceLimits.MaxLength}.", out error);

        var text = string.Concat(source.Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();
        var terms = new List<DiceTerm>();
        var i = 0;
        var sign = 1;
        if (text.Length > 0 && text[0] is '+' or '-')
        {
            sign = text[0] == '+' ? 1 : -1;
            i++;
        }
        var totalDice = 0;
        while (true)
        {
            if (terms.Count == DiceLimits.MaxTerms)
                return Fail("dice.too-many-terms", $"The dice expression has more than {DiceLimits.MaxTerms} terms.", out error);
            var count = ReadNumber(text, ref i);
            if (i < text.Length && text[i] == 'd')
            {
                i++;
                var sides = ReadNumber(text, ref i);
                if (sides is null)
                    return Fail("dice.syntax", $"Expected the number of sides after 'd' in '{source}'.", out error);
                var n = count ?? 1;
                if (n < 1)
                    return Fail("dice.syntax", $"'{source}' rolls zero dice.", out error);
                if (n > DiceLimits.MaxDice || (totalDice += n) > DiceLimits.MaxDice)
                    return Fail("dice.too-many-dice", $"'{source}' rolls more than {DiceLimits.MaxDice} dice.", out error);
                if (sides is < DiceLimits.MinSides or > DiceLimits.MaxSides)
                    return Fail("dice.sides-out-of-range", $"A die must have {DiceLimits.MinSides} to {DiceLimits.MaxSides} sides, not {sides}.", out error);
                terms.Add(new(sign, n, sides.Value, 0));
            }
            else if (count is { } constant)
            {
                if (constant > DiceLimits.MaxConstant)
                    return Fail("dice.modifier-out-of-range", $"Modifier {constant} is larger than {DiceLimits.MaxConstant}.", out error);
                terms.Add(new(sign, 0, 0, constant));
            }
            else
            {
                return Fail("dice.syntax", $"'{source}' is not a dice expression like 1d20+5 or 2d6.", out error);
            }

            if (i == text.Length)
                break;
            if (text[i] is not ('+' or '-'))
                return Fail("dice.syntax", $"Unexpected '{text[i]}' in '{source}'.", out error);
            sign = text[i] == '+' ? 1 : -1;
            i++;
            if (i == text.Length)
                return Fail("dice.syntax", $"'{source}' ends with an operator.", out error);
        }
        if (!terms.Any(t => t.IsDice))
            return Fail("dice.no-dice", $"'{source}' has no dice to roll.", out error);

        expression = new DiceExpression(source, terms);
        error = null;
        return true;
    }

    public override string ToString() => Source;

    /// <summary>Reads up to 4 digits (anything larger is out of every range); null if no digit.</summary>
    private static int? ReadNumber(string text, ref int i)
    {
        var start = i;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
            i++;
        if (i == start)
            return null;
        return i - start > 4 ? int.MaxValue : int.Parse(text[start..i], CultureInfo.InvariantCulture);
    }

    private static bool Fail(string code, string message, out FormulaError error)
    {
        error = new(code, message);
        return false;
    }
}

/// <summary>A flat modifier added to a roll, with where it came from (for example the sheet's initiative trace).</summary>
public sealed record RollModifier(string Label, int Amount, TraceOrigin? Origin = null);

/// <summary>
/// What the roll is for. <see cref="LinkedResourceId"/> is informational: spending it is a separate command.
/// <see cref="LinkedResourceContent"/> is the content that defines that resource (another feature's, for a shared one).
/// </summary>
public sealed record RollProvenance(
    string RollId,
    string Label,
    ContentReference? Content = null,
    string? ContentName = null,
    string? EffectId = null,
    Guid? SourceId = null,
    string? SourceTitle = null,
    PageRef? Page = null,
    string? LinkedResourceId = null,
    Guid? LinkedResourceContent = null);

/// <param name="KeepHighest">
/// Item 2, D32: keep this many highest dice of the single die term (4d6 drop lowest); the dropped dice are recorded
/// with Kept = false.
/// </param>
public sealed record RollRequest(
    string Formula,
    RollMode Mode = RollMode.Normal,
    bool Critical = false,
    IReadOnlyList<RollModifier>? Modifiers = null,
    RollProvenance? Provenance = null,
    int? KeepHighest = null);

/// <param name="Term">Index of the expression term the die belongs to.</param>
/// <param name="Kept">False for a die that does not count: dropped by advantage/disadvantage or by KeepHighest.</param>
public sealed record DieResult(int Term, int Sides, int Value, bool Kept, bool FromCritical = false);

/// <summary>
/// SPEC C-04: formula, component dice, modifiers and provenance of one roll. A record is data only. Producing one
/// never changes a character, resource or any other state (ARCHITECTURE "commands vs calculation").
/// </summary>
public sealed record RollRecord(
    string Formula,
    RollMode Mode,
    bool Critical,
    IReadOnlyList<DieResult> Dice,
    int DiceTotal,
    int ExpressionConstant,
    IReadOnlyList<RollModifier> Modifiers,
    int Total,
    RollProvenance? Provenance);

/// <summary>Rolls dice. Pure apart from the injected <see cref="IRandomSource"/>; takes no character or resource state.</summary>
public static class DiceRoller
{
    public static bool TryRoll(RollRequest request, IRandomSource random, out RollRecord? record, out FormulaError? error)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(random);
        record = null;
        if (!DiceExpression.TryParse(request.Formula, out var expression, out error))
            return false;
        if (request.Mode != RollMode.Normal && request.Critical)
        {
            // Advantage belongs to a d20 test and critical doubling to its damage roll: they are separate rolls.
            error = new("dice.critical-with-advantage", "A roll cannot use advantage or disadvantage and critical doubling together; roll the d20 test and the damage separately.");
            return false;
        }
        if (request.Mode != RollMode.Normal && !expression!.IsD20Test)
        {
            error = new("dice.advantage-requires-d20", $"Advantage and disadvantage apply to a single d20 roll, not '{request.Formula}'.");
            return false;
        }
        var modifiers = request.Modifiers ?? [];
        if (modifiers.Any(m => Math.Abs(m.Amount) > DiceLimits.MaxConstant))
        {
            error = new("dice.modifier-out-of-range", $"Roll modifiers must be within ±{DiceLimits.MaxConstant}.");
            return false;
        }
        if (request.KeepHighest is { } keep)
        {
            var diceTerms = expression!.Terms.Where(t => t.IsDice).ToList();
            if (diceTerms.Count != 1 || request.Mode != RollMode.Normal || request.Critical)
            {
                error = new("dice.keep-requires-single-term", "Keeping the highest dice applies to one plain die term, without advantage or critical doubling.");
                return false;
            }
            if (keep < 1 || keep >= diceTerms[0].Count)
            {
                error = new("dice.keep-out-of-range", diceTerms[0].Count == 1
                    ? $"Keeping the highest dice needs more than one die in '{request.Formula}'."
                    : $"Keep between 1 and {diceTerms[0].Count - 1} dice of '{request.Formula}'.");
                return false;
            }
        }

        var dice = new List<DieResult>();
        var diceTotal = 0;
        var constant = 0;
        for (var t = 0; t < expression!.Terms.Count; t++)
        {
            var term = expression.Terms[t];
            if (!term.IsDice)
            {
                constant += term.Sign * term.Constant;
                continue;
            }
            if (request.Mode != RollMode.Normal)
            {
                var first = random.Next(term.Sides);
                var second = random.Next(term.Sides);
                var keepFirst = request.Mode == RollMode.Advantage ? first >= second : first <= second;
                dice.Add(new(t, term.Sides, first, keepFirst));
                dice.Add(new(t, term.Sides, second, !keepFirst));
                diceTotal += term.Sign * (keepFirst ? first : second);
                continue;
            }
            // 5e critical: roll the damage dice twice; flat modifiers are not doubled.
            var count = request.Critical ? term.Count * 2 : term.Count;
            var values = new int[count];
            for (var d = 0; d < count; d++)
                values[d] = random.Next(term.Sides);
            var kept = new bool[count];
            Array.Fill(kept, true);
            if (request.KeepHighest is { } keepCount)
            {
                // Drop the lowest; among ties the earliest rolled is dropped first (stable order by value, then index).
                foreach (var index in Enumerable.Range(0, count).OrderBy(i => values[i]).ThenBy(i => i).Take(count - keepCount))
                    kept[index] = false;
            }
            for (var d = 0; d < count; d++)
            {
                dice.Add(new(t, term.Sides, values[d], kept[d], FromCritical: d >= term.Count));
                if (kept[d])
                    diceTotal += term.Sign * values[d];
            }
        }

        var total = diceTotal + constant + modifiers.Sum(m => m.Amount);
        record = new RollRecord(request.Formula, request.Mode, request.Critical, dice, diceTotal, constant, modifiers, total, request.Provenance);
        return true;
    }

    /// <summary>Builds a request for a content <see cref="RollEffect"/>, citing its revision and source.</summary>
    public static RollRequest FromEffect(
        RollEffect effect, ContentRevision revision, SourceRecord? source, RollMode mode = RollMode.Normal, bool critical = false,
        IReadOnlyList<RollModifier>? modifiers = null)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(revision);
        return new RollRequest(
            effect.Dice,
            mode,
            critical,
            modifiers,
            new RollProvenance(
                effect.RollId, effect.Label, revision.Reference, revision.Name, effect.Id,
                source?.Id, source?.Title, revision.Provenance.Page, effect.ResourceId, effect.ResourceContent ?? (effect.ResourceId is null ? null : revision.ContentId)));
    }
}
