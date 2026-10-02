using System.Globalization;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace TomeStack.RulesCore.Tests.Properties;

/// <summary>
/// T3 property 3 (ADR-003 bounded formulas, ADR-010 <c>SCALE.&lt;id&gt;</c> in v9 only): printing an AST and parsing it
/// back gives the same AST, and parsing and evaluating are total (they return an error code, never throw) for any input.
/// </summary>
public class FormulaProperties
{
    private static readonly string[] Identifiers = [.. FormulaIdentifiers.All];

    private static readonly string[] ScaleIdentifiers = ["SCALE.ink", "SCALE.knots", "SCALE.a1", "SCALE.sparkDice"];

    private static readonly (string Name, int Min, int Max)[] Functions = [("floor", 1, 1), ("ceil", 1, 1), ("abs", 1, 1), ("min", 2, 4), ("max", 2, 4)];

    private static readonly HashSet<string> EvaluationErrors = ["formula.value-unavailable", "formula.division-by-zero", "formula.out-of-range"];

    /// <summary>An AST of the ADR-003 grammar, with <c>SCALE.</c> identifiers when <paramref name="scales"/>.</summary>
    private static Gen<FormulaNode> Node(int size, bool scales)
    {
        var leaf = Gen.OneOf(
            Gen.Choose(0, FormulaLimits.MaxLiteral).Select(n => (FormulaNode)new NumberNode(n)),
            Gen.Elements(scales ? [.. Identifiers, .. ScaleIdentifiers] : Identifiers).Select(i => (FormulaNode)new IdentifierNode(i)));
        if (size <= 0)
            return leaf;
        var child = Node(size / 2, scales);
        return Gen.Frequency(
            (3, leaf),
            (1, child.Select(c => (FormulaNode)new NegateNode(c))),
            (4, from op in Gen.Elements('+', '-', '*', '/')
                from l in child
                from r in child
                select (FormulaNode)new BinaryNode(op, l, r)),
            (2, from f in Gen.Elements(Functions)
                from n in Gen.Choose(f.Min, f.Max)
                from args in child.ArrayOf(n)
                select (FormulaNode)new CallNode(f.Name, args)));
    }

    /// <summary>An AST whose printed form is inside every ADR-003 limit, so the parser must accept it.</summary>
    private static Gen<(FormulaNode Ast, string Text)> Bounded(bool scales) =>
        Gen.Sized(size => Node(Math.Min(size, 16), scales))
            .Select(ast => (Ast: ast, Printed: Print(ast)))
            .Where(p => p.Printed.Text.Length <= FormulaLimits.MaxLength && p.Printed.Tokens <= FormulaLimits.MaxTokens && p.Printed.Depth <= FormulaLimits.MaxDepth)
            .Select(p => (p.Ast, p.Printed.Text));

    /// <summary>Shrinks to subtrees and smaller literals, so a failure is reported on the smallest formula that still fails.</summary>
    private static IEnumerable<FormulaNode> Shrink(FormulaNode node)
    {
        switch (node)
        {
            case NumberNode { Value: > 0 } n:
                yield return new NumberNode(0);
                if (n.Value > 1)
                    yield return new NumberNode(Math.Floor(n.Value / 2));
                break;
            case IdentifierNode:
                yield return new NumberNode(1);
                break;
            case NegateNode n:
                yield return n.Operand;
                foreach (var s in Shrink(n.Operand))
                    yield return new NegateNode(s);
                break;
            case BinaryNode b:
                yield return b.Left;
                yield return b.Right;
                foreach (var s in Shrink(b.Left))
                    yield return b with { Left = s };
                foreach (var s in Shrink(b.Right))
                    yield return b with { Right = s };
                break;
            case CallNode c:
                foreach (var a in c.Arguments)
                    yield return a;
                for (var i = 0; i < c.Arguments.Count; i++)
                {
                    foreach (var s in Shrink(c.Arguments[i]))
                        yield return c with { Arguments = [.. c.Arguments.Select((a, j) => j == i ? s : a)] };
                }
                break;
        }
    }

    private static Arbitrary<(FormulaNode Ast, string Text)> BoundedArb(bool scales, Func<FormulaNode, bool>? where = null)
    {
        where ??= _ => true;
        return Arb.From(
            Bounded(scales).Where(p => where(p.Ast)),
            p => Shrink(p.Ast).Where(where).Select(a => (a, Print(a).Text)));
    }

    private static int Precedence(char op) => op is '+' or '-' ? 1 : 2;

    /// <summary>The text with only the parentheses precedence and left associativity need, its token count and its nesting depth as the parser counts it (parentheses, calls and negations).</summary>
    internal static (string Text, int Tokens, int Depth) Print(FormulaNode node)
    {
        static (string Text, int Tokens, int Depth) Paren((string Text, int Tokens, int Depth) p) => ($"({p.Text})", p.Tokens + 2, p.Depth + 1);
        switch (node)
        {
            case NumberNode n:
                return (((int)n.Value).ToString(CultureInfo.InvariantCulture), 1, 0);
            case IdentifierNode i:
                return (i.Name, 1, 0);
            case NegateNode n:
            {
                var operand = n.Operand is BinaryNode ? Paren(Print(n.Operand)) : Print(n.Operand);
                return ("-" + operand.Text, operand.Tokens + 1, operand.Depth + 1);
            }
            case BinaryNode b:
            {
                var left = b.Left is BinaryNode l && Precedence(l.Operator) < Precedence(b.Operator) ? Paren(Print(b.Left)) : Print(b.Left);
                var right = b.Right is BinaryNode r && Precedence(r.Operator) <= Precedence(b.Operator) ? Paren(Print(b.Right)) : Print(b.Right);
                return ($"{left.Text} {b.Operator} {right.Text}", left.Tokens + right.Tokens + 1, Math.Max(left.Depth, right.Depth));
            }
            case CallNode c:
            {
                var args = c.Arguments.Select(Print).ToList();
                return ($"{c.Function}({string.Join(", ", args.Select(a => a.Text))})", args.Sum(a => a.Tokens) + args.Count + 2, args.Max(a => a.Depth) + 1);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(node));
        }
    }

    /// <summary>Structural equality: <see cref="CallNode.Arguments"/> is a list, which records compare by reference.</summary>
    internal static bool Same(FormulaNode a, FormulaNode b) => (a, b) switch
    {
        (NumberNode x, NumberNode y) => x.Value.Equals(y.Value),
        (IdentifierNode x, IdentifierNode y) => x.Name == y.Name,
        (NegateNode x, NegateNode y) => Same(x.Operand, y.Operand),
        (BinaryNode x, BinaryNode y) => x.Operator == y.Operator && Same(x.Left, y.Left) && Same(x.Right, y.Right),
        (CallNode x, CallNode y) => x.Function == y.Function && x.Arguments.Count == y.Arguments.Count && x.Arguments.Zip(y.Arguments).All(p => Same(p.First, p.Second)),
        _ => false,
    };

    private static bool HasScale(FormulaNode node) => node switch
    {
        IdentifierNode i => FormulaIdentifiers.IsScale(i.Name),
        NegateNode n => HasScale(n.Operand),
        BinaryNode b => HasScale(b.Left) || HasScale(b.Right),
        CallNode c => c.Arguments.Any(HasScale),
        _ => false,
    };

    [Property(MaxTest = 1000)]
    public Property Parsing_a_printed_AST_gives_the_same_AST() =>
        Prop.ForAll(BoundedArb(scales: true), p =>
        {
            var parsed = Formula.TryParse(p.Text, allowScales: true, out var formula, out var error);
            return (parsed && Same(p.Ast, formula!.Root)).Label($"{p.Text} → {error?.Code}");
        });

    [Property(MaxTest = 500)]
    public Property SCALE_identifiers_parse_only_where_scales_are_allowed() =>
        Prop.ForAll(BoundedArb(scales: true, HasScale), p =>
        {
            var allowed = Formula.TryParse(p.Text, allowScales: true, out _, out _);
            var refused = !Formula.TryParse(p.Text, allowScales: false, out _, out var error) && error!.Code == "formula.unknown-identifier";
            return (allowed && refused).Label(p.Text);
        });

    [Property(MaxTest = 1000)]
    public Property Evaluation_is_total_inside_the_bounded_grammar() =>
        Prop.ForAll(
            BoundedArb(scales: true),
            Gen.Frequency((1, Gen.Constant<int?>(null)), (12, Gen.Choose(-10, 30).Select(v => (int?)v))).ArrayOf(Identifiers.Length + ScaleIdentifiers.Length).ToArbitrary(),
            (p, values) =>
            {
                var names = Identifiers.Concat(ScaleIdentifiers).ToArray();
                int? Resolve(string name) => values[Array.IndexOf(names, name)];
                Assert.True(Formula.TryParse(p.Text, allowScales: true, out var formula, out _));
                var ok = formula!.TryEvaluate(Resolve, out var value, out var error);
                var again = formula.TryEvaluate(Resolve, out var value2, out var error2);
                return ((ok ? Math.Abs(value) <= FormulaLimits.MaxMagnitude : EvaluationErrors.Contains(error!.Code))
                        && ok == again && value == value2 && error?.Code == error2?.Code)
                    .Label($"{p.Text} → {(ok ? value.ToString(CultureInfo.InvariantCulture) : error!.Code)}");
            });

    /// <summary>Untrusted text (SPEC Q-02): any string, mostly from the formula alphabet, parses or fails with a code.</summary>
    [Property(MaxTest = 2000)]
    public Property Parsing_any_text_returns_a_result_or_an_error_code_and_never_throws() =>
        Prop.ForAll(
            Gen.OneOf(
                Gen.Elements("1", "99999", "PB", "SCALE.", "SCALE.ink", "DEX.MOD", "max", "floor", "(", ")", ",", "+", "-", "*", "/", " ", "\t", ".", "1.5", "é", "\0", "💥", "_x")
                    .ArrayOf().Select(parts => string.Concat(parts.Take(80))),
                ArbMap.Default.GeneratorFor<string>()).ToArbitrary(),
            Gen.Elements(true, false).ToArbitrary(),
            (text, scales) =>
            {
                var parsed = Formula.TryParse(text, scales, out var formula, out var error);
                return (parsed ? formula is not null && error is null : formula is null && error?.Code.StartsWith("formula.", StringComparison.Ordinal) == true)
                    .Label(text ?? "<null>");
            });
}
