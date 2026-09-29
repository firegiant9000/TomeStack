using System.Globalization;

namespace TomeStack.RulesCore;

/// <summary>ADR-003 bounds. Content is untrusted (SPEC Q-02): every limit is checked before or while evaluating.</summary>
public static class FormulaLimits
{
    public const int MaxLength = 200;
    public const int MaxTokens = 64;
    public const int MaxDepth = 8;
    public const int MaxLiteral = 10_000;
    public const double MaxMagnitude = 1_000_000;
}

/// <summary>A parse or evaluation failure. <see cref="Code"/> is stable; <see cref="Message"/> is for people.</summary>
public sealed record FormulaError(string Code, string Message);

/// <summary>The identifiers a formula may use, and the derived field each one reads (ADR-003).</summary>
public static class FormulaIdentifiers
{
    public const string ProficiencyBonus = "PB";
    public const string Level = "LEVEL";
    public const string ClassLevel = "CLASS_LEVEL";

    /// <summary>
    /// Content schema v9 (ADR-010): <c>SCALE.&lt;scaleId&gt;</c> reads a class's per-level column (<see cref="ScaleEffect"/>)
    /// at the level of the class the content belongs to. It reads no field, so it adds no dependency edge.
    /// </summary>
    public const string ScalePrefix = "SCALE.";

    private static readonly Dictionary<string, string?> Fields = Build();

    public static IEnumerable<string> All => Fields.Keys;

    public static bool IsKnown(string identifier) => Fields.ContainsKey(identifier);

    /// <summary>Whether <paramref name="identifier"/> is <c>SCALE.</c> followed by a valid scale id.</summary>
    public static bool IsScale(string identifier) =>
        identifier.StartsWith(ScalePrefix, StringComparison.Ordinal) && ScaleEffect.IsValidScaleId(identifier[ScalePrefix.Length..]);

    /// <summary>The scale id of a <c>SCALE.</c> identifier.</summary>
    public static string ScaleId(string identifier) => identifier[ScalePrefix.Length..];

    /// <summary>The field an identifier reads, or null for inputs that are not derived fields (LEVEL, CLASS_LEVEL).</summary>
    public static string? FieldFor(string identifier) => Fields.GetValueOrDefault(identifier);

    private static Dictionary<string, string?> Build()
    {
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [ProficiencyBonus] = FieldIds.ProficiencyBonus,
            [Level] = null,
            [ClassLevel] = null,
        };
        foreach (var ability in Enum.GetValues<Ability>())
        {
            var name = ability.ToString().ToUpperInvariant();
            fields[$"{name}.MOD"] = FieldIds.Modifier(ability);
            fields[$"{name}.SCORE"] = FieldIds.Score(ability);
        }
        return fields;
    }
}

/// <summary>Immutable formula AST. Only these node kinds exist; there is no way to express a call to user code.</summary>
public abstract record FormulaNode;

public sealed record NumberNode(double Value) : FormulaNode;

public sealed record IdentifierNode(string Name) : FormulaNode;

public sealed record NegateNode(FormulaNode Operand) : FormulaNode;

public sealed record BinaryNode(char Operator, FormulaNode Left, FormulaNode Right) : FormulaNode;

public sealed record CallNode(string Function, IReadOnlyList<FormulaNode> Arguments) : FormulaNode;

/// <summary>A parsed, bounded formula (ADR-003 grammar). Parse once, evaluate against any resolver.</summary>
public sealed class Formula
{
    private static readonly Dictionary<string, (int Min, int Max)> Functions = new(StringComparer.Ordinal)
    {
        ["floor"] = (1, 1),
        ["ceil"] = (1, 1),
        ["abs"] = (1, 1),
        ["min"] = (2, 4),
        ["max"] = (2, 4),
    };

    private Formula(string source, FormulaNode root, IReadOnlySet<string> identifiers)
    {
        Source = source;
        Root = root;
        Identifiers = identifiers;
    }

    public string Source { get; }

    public FormulaNode Root { get; }

    /// <summary>Every identifier the formula reads. These become dependency edges (item 11).</summary>
    public IReadOnlySet<string> Identifiers { get; }

    public static bool TryParse(string? source, out Formula? formula, out FormulaError? error) =>
        TryParse(source, allowScales: false, out formula, out error);

    /// <param name="allowScales">
    /// Whether <c>SCALE.&lt;id&gt;</c> is a known identifier: only in a content schema v9 (or newer) revision
    /// (<see cref="ScaleEffect.SchemaVersion"/>). Below v9 it is <c>formula.unknown-identifier</c>, exactly as in builds
    /// before v9, so one stored revision never means two things (ADR-010).
    /// </param>
    public static bool TryParse(string? source, bool allowScales, out Formula? formula, out FormulaError? error)
    {
        formula = null;
        if (string.IsNullOrWhiteSpace(source))
        {
            error = new("formula.empty", "The formula is empty.");
            return false;
        }
        if (source.Length > FormulaLimits.MaxLength)
        {
            error = new("formula.too-long", $"The formula is {source.Length} characters long; the limit is {FormulaLimits.MaxLength}.");
            return false;
        }
        try
        {
            var tokens = Tokenize(source);
            var parser = new Parser(tokens, allowScales);
            var root = parser.ParseFormula();
            formula = new Formula(source, root, parser.Identifiers);
            error = null;
            return true;
        }
        catch (FormulaException ex)
        {
            error = ex.Error;
            return false;
        }
    }

    /// <summary>
    /// Evaluates with <paramref name="resolve"/> supplying identifier values (null means not available). Fractions round
    /// down at the end (the 5e default). Intermediate results outside ±<see cref="FormulaLimits.MaxMagnitude"/> fail.
    /// </summary>
    public bool TryEvaluate(Func<string, int?> resolve, out int value, out FormulaError? error)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        try
        {
            var result = Evaluate(Root, resolve);
            // Absorb binary floating-point noise before rounding down (for example 7 / 3 * 3).
            value = (int)Math.Floor(result + 1e-9);
            error = null;
            return true;
        }
        catch (FormulaException ex)
        {
            value = 0;
            error = ex.Error;
            return false;
        }
    }

    public override string ToString() => Source;

    private static double Evaluate(FormulaNode node, Func<string, int?> resolve)
    {
        var result = node switch
        {
            NumberNode n => n.Value,
            IdentifierNode i => resolve(i.Name) ?? throw new FormulaException("formula.value-unavailable", $"{i.Name} is not available for this character yet."),
            NegateNode n => -Evaluate(n.Operand, resolve),
            BinaryNode b => Apply(b.Operator, Evaluate(b.Left, resolve), Evaluate(b.Right, resolve)),
            CallNode c => Call(c.Function, [.. c.Arguments.Select(a => Evaluate(a, resolve))]),
            _ => throw new FormulaException("formula.syntax", "Unknown formula node."),
        };
        if (Math.Abs(result) > FormulaLimits.MaxMagnitude)
            throw new FormulaException("formula.out-of-range", $"A step of the formula reaches {result:0.##}, outside ±{FormulaLimits.MaxMagnitude:0}.");
        return result;
    }

    private static double Apply(char op, double left, double right) => op switch
    {
        '+' => left + right,
        '-' => left - right,
        '*' => left * right,
        '/' => right == 0 ? throw new FormulaException("formula.division-by-zero", "The formula divides by zero.") : left / right,
        _ => throw new FormulaException("formula.syntax", $"Unknown operator '{op}'."),
    };

    private static double Call(string function, double[] args) => function switch
    {
        "floor" => Math.Floor(args[0] + 1e-9),
        "ceil" => Math.Ceiling(args[0] - 1e-9),
        "abs" => Math.Abs(args[0]),
        "min" => args.Min(),
        "max" => args.Max(),
        _ => throw new FormulaException("formula.unknown-function", $"Unknown function '{function}'."),
    };

    private enum TokenKind { Number, Identifier, Operator, LeftParen, RightParen, Comma }

    private readonly record struct Token(TokenKind Kind, string Text, int Position);

    private static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (c is ' ' or '\t')
            {
                i++;
                continue;
            }
            if (tokens.Count >= FormulaLimits.MaxTokens)
                throw new FormulaException("formula.too-many-tokens", $"The formula has more than {FormulaLimits.MaxTokens} parts.");
            var start = i;
            if (char.IsAsciiDigit(c))
            {
                while (i < source.Length && char.IsAsciiDigit(source[i]))
                    i++;
                var digits = source[start..i];
                if (digits.Length > 5 || int.Parse(digits, CultureInfo.InvariantCulture) > FormulaLimits.MaxLiteral)
                    throw new FormulaException("formula.number-too-large", $"Number {Truncate(digits)} is larger than {FormulaLimits.MaxLiteral}.");
                if (i < source.Length && source[i] == '.')
                    throw new FormulaException("formula.syntax", $"Decimal numbers are not supported (position {i + 1}).");
                tokens.Add(new(TokenKind.Number, digits, start));
            }
            else if (char.IsAsciiLetter(c) || c == '_')
            {
                while (i < source.Length && (char.IsAsciiLetterOrDigit(source[i]) || source[i] is '_' or '.'))
                    i++;
                tokens.Add(new(TokenKind.Identifier, source[start..i], start));
            }
            else
            {
                var kind = c switch
                {
                    '+' or '-' or '*' or '/' => TokenKind.Operator,
                    '(' => TokenKind.LeftParen,
                    ')' => TokenKind.RightParen,
                    ',' => TokenKind.Comma,
                    _ => throw new FormulaException("formula.invalid-character", $"Character '{Printable(c)}' at position {i + 1} is not allowed in a formula."),
                };
                tokens.Add(new(kind, c.ToString(), start));
                i++;
            }
        }
        return tokens;
    }

    private static string Truncate(string text) => text.Length > 12 ? text[..12] + "…" : text;

    private static string Printable(char c) => char.IsControl(c) || char.IsSurrogate(c) ? $"U+{(int)c:X4}" : c.ToString();

    /// <summary>Recursive descent. Recursion consumes a token per level, so it is bounded by the token limit and MaxDepth.</summary>
    private sealed class Parser(List<Token> tokens, bool allowScales)
    {
        private int _position;
        private int _depth;

        public HashSet<string> Identifiers { get; } = new(StringComparer.Ordinal);

        public FormulaNode ParseFormula()
        {
            var node = ParseSum();
            if (_position < tokens.Count)
                throw Syntax($"Unexpected '{tokens[_position].Text}' at position {tokens[_position].Position + 1}.");
            return node;
        }

        private FormulaNode ParseSum()
        {
            var node = ParseProduct();
            while (Peek(TokenKind.Operator, "+") || Peek(TokenKind.Operator, "-"))
            {
                var op = tokens[_position++].Text[0];
                node = new BinaryNode(op, node, ParseProduct());
            }
            return node;
        }

        private FormulaNode ParseProduct()
        {
            var node = ParseUnary();
            while (Peek(TokenKind.Operator, "*") || Peek(TokenKind.Operator, "/"))
            {
                var op = tokens[_position++].Text[0];
                node = new BinaryNode(op, node, ParseUnary());
            }
            return node;
        }

        private FormulaNode ParseUnary()
        {
            if (!Peek(TokenKind.Operator, "-"))
                return ParsePrimary();
            _position++;
            return Nested(() => new NegateNode(ParseUnary()));
        }

        private FormulaNode ParsePrimary()
        {
            if (_position >= tokens.Count)
                throw Syntax("The formula ends too early.");
            var token = tokens[_position++];
            switch (token.Kind)
            {
                case TokenKind.Number:
                    return new NumberNode(int.Parse(token.Text, CultureInfo.InvariantCulture));
                case TokenKind.LeftParen:
                    return Nested(() =>
                    {
                        var inner = ParseSum();
                        Expect(TokenKind.RightParen, ")");
                        return inner;
                    });
                case TokenKind.Identifier when Peek(TokenKind.LeftParen, "("):
                    return ParseCall(token);
                case TokenKind.Identifier:
                    if (!FormulaIdentifiers.IsKnown(token.Text) && !(allowScales && FormulaIdentifiers.IsScale(token.Text)))
                        throw new FormulaException("formula.unknown-identifier", $"'{Truncate(token.Text)}' is not a known value. Use one of: {string.Join(", ", FormulaIdentifiers.All)}.");
                    Identifiers.Add(token.Text);
                    return new IdentifierNode(token.Text);
                default:
                    throw Syntax($"Unexpected '{token.Text}' at position {token.Position + 1}.");
            }
        }

        private CallNode ParseCall(Token name)
        {
            if (!Functions.TryGetValue(name.Text, out var arity))
                throw new FormulaException("formula.unknown-function", $"'{Truncate(name.Text)}' is not a supported function. Use one of: {string.Join(", ", Functions.Keys)}.");
            _position++; // (
            return Nested(() =>
            {
                var args = new List<FormulaNode> { ParseSum() };
                while (Peek(TokenKind.Comma, ","))
                {
                    _position++;
                    args.Add(ParseSum());
                }
                Expect(TokenKind.RightParen, ")");
                if (args.Count < arity.Min || args.Count > arity.Max)
                {
                    var expected = arity.Min == arity.Max ? $"{arity.Min}" : $"{arity.Min} to {arity.Max}";
                    throw new FormulaException("formula.wrong-argument-count", $"{name.Text}() takes {expected} argument(s), not {args.Count}.");
                }
                return new CallNode(name.Text, args);
            });
        }

        private T Nested<T>(Func<T> parse)
        {
            if (++_depth > FormulaLimits.MaxDepth)
                throw new FormulaException("formula.too-deep", $"The formula nests more than {FormulaLimits.MaxDepth} levels deep.");
            try
            {
                return parse();
            }
            finally
            {
                _depth--;
            }
        }

        private bool Peek(TokenKind kind, string text) =>
            _position < tokens.Count && tokens[_position].Kind == kind && tokens[_position].Text == text;

        private void Expect(TokenKind kind, string text)
        {
            if (!Peek(kind, text))
                throw Syntax(_position < tokens.Count ? $"Expected '{text}' at position {tokens[_position].Position + 1}." : $"Expected '{text}' before the end.");
            _position++;
        }

        private static FormulaException Syntax(string message) => new("formula.syntax", message);
    }

    private sealed class FormulaException(string code, string message) : Exception(message)
    {
        public FormulaError Error { get; } = new(code, message);
    }
}
