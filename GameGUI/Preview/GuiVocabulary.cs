using System.Text.RegularExpressions;

namespace Ck3MapGen.GameGui.Preview;

/// <summary>
/// Every datafunction call vanilla makes, harvested from its own <c>.gui</c> files, so a generated
/// file can be asked whether it is speaking the language.
///
/// This exists because of a specific silent failure. A window written with
/// <c>datacontext = "[Scope.Character]"</c> parses, loads, passes ck3-tiger without a word, and
/// resolves to nothing — the widgets draw, the arms come out as an empty frame and every line of
/// text renders blank. The accessor is spelled <c>Scope.Char</c>. Nothing in the toolchain said so,
/// and nothing in a static preview could: a preview draws <c>⟨GetName⟩</c> as a stub whether or not
/// the call behind it exists.
///
/// What can be said is that vanilla, across 373 files and some ten thousand distinct call pairs,
/// never once writes <c>Scope.Character</c>. That is not proof of an error — this project reaches
/// for things vanilla has no use for, and <c>GetGlobalList</c> is one of them — so it is reported as
/// a question rather than a failure. In the one case that mattered it took a bug that survived
/// tiger, a preview and a screenshot, and put it at the top of the report.
///
/// Deliberately a heuristic, and deliberately not wired to anything that can refuse to ship.
/// </summary>
public sealed class GuiVocabulary
{
    /// <summary>Adjacent pairs in a call chain — <c>Character.GetPrimaryTitle</c> — and bare roots.</summary>
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);

    private static readonly Regex Expression = new(@"\[([^\]]*)\]", RegexOptions.Compiled);

    private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public int Count => _known.Count;

    /// <summary>Records every call in a tree as known vocabulary.</summary>
    public void Learn(IEnumerable<GuiNode> nodes)
    {
        foreach (var node in nodes) Walk(node, _known.Add);
    }

    /// <summary>
    /// Calls in a tree that the vocabulary has never seen, with how often each is used.
    ///
    /// Ordered by name so the report reads the same twice running.
    /// </summary>
    public IReadOnlyList<(string Call, int Uses)> Unknown(IEnumerable<GuiNode> nodes)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            Walk(node, call =>
            {
                if (_known.Contains(call)) return false;

                counts[call] = counts.GetValueOrDefault(call) + 1;
                return true;
            });
        }

        return [.. counts.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => (e.Key, e.Value))];
    }

    private static void Walk(GuiNode node, Func<string, bool> visit)
    {
        if (node.Value is { } value) Scan(value, visit);

        foreach (var child in node.Children) Walk(child, visit);
    }

    /// <summary>
    /// Pulls the call pairs out of one property value.
    ///
    /// Split on the punctuation that separates arguments from the chain around them, so
    /// <c>GetScriptedGui('x').IsShown( GuiScope.SetRoot( … ) )</c> contributes
    /// <c>GuiScope.SetRoot</c> without inventing a pair across the bracket.
    /// </summary>
    private static void Scan(string value, Func<string, bool> visit)
    {
        foreach (Match match in Expression.Matches(value))
        {
            foreach (string chain in match.Groups[1].Value.Split([',', '(', ')', ' ', '\t'],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = chain.Split('.').Where(p => Identifier.IsMatch(p)).ToList();

                if (parts.Count > 0) visit(parts[0]);

                for (int i = 1; i < parts.Count; i++) visit($"{parts[i - 1]}.{parts[i]}");
            }
        }
    }
}
