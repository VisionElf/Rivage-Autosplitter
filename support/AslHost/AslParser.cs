using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Rivage.AslHost;

public record PointerField(string Type, string Name, string? Module, long[] Offsets);
public record StateDescriptor(string Process, string Version, List<PointerField> Fields);
public record AslDocument(List<StateDescriptor> States, Dictionary<string, string> Actions);

public sealed class AslParser(string source)
{
    private readonly SyntaxToken[] tokens = SyntaxFactory.ParseTokens(source).ToArray();
    private int position;
    private SyntaxToken Next() => position < tokens.Length ? tokens[position++] : throw new Exception("Unexpected end of ASL script.");
    private bool Is(string text) => position < tokens.Length && tokens[position].Text == text;
    private void Expect(string text) { if (Next().Text != text) throw new Exception($"Expected '{text}' in ASL script."); }
    private string String() { var token = Next(); return token.IsKind(SyntaxKind.StringLiteralToken) ? token.ValueText : throw new Exception("Expected a quoted ASL name."); }
    public AslDocument Parse()
    {
        if (source.Length > 1_000_000) throw new Exception("ASL scripts must be smaller than 1 MB.");
        var states = new List<StateDescriptor>();
        var actions = new Dictionary<string, string>();
        while (position < tokens.Length && !tokens[position].IsKind(SyntaxKind.EndOfFileToken))
        {
            var name = Next().Text;
            if (name == "state")
            {
                Expect("("); var process = String(); var version = "";
                if (Is(",")) { Next(); version = String(); }
                Expect(")"); Expect("{");
                var fields = new List<PointerField>();
                while (!Is("}"))
                {
                    var type = Next().Text;
                    if (!new[] { "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong", "float", "double", "bool" }.Contains(type))
                        throw new Exception($"Unsupported ASL state type: {type}.");
                    var field = Next().Text; Expect(":"); string? module = null;
                    if (tokens[position].IsKind(SyntaxKind.StringLiteralToken)) { module = String(); Expect(","); }
                    var offsets = new List<long>();
                    do
                    {
                        if (Is(",")) Next();
                        var sign = 1L; if (Is("-")) { Next(); sign = -1; }
                        var number = Next();
                        if (!number.IsKind(SyntaxKind.NumericLiteralToken)) throw new Exception("Expected a numeric pointer offset.");
                        offsets.Add(checked(sign * Convert.ToInt64(number.Value)));
                        if (offsets.Count > 32) throw new Exception("Pointer chains cannot exceed 32 offsets.");
                    } while (Is(","));
                    Expect(";"); fields.Add(new(type, field, module, offsets.ToArray()));
                }
                Expect("}"); states.Add(new(process, version, fields));
            }
            else
            {
                if (!new[] { "startup", "init", "update", "start", "split", "reset", "isLoading", "gameTime", "exit", "shutdown" }.Contains(name))
                    throw new Exception($"Unsupported ASL action: {name}.");
                var opening = Next();
                if (opening.Text != "{") throw new Exception($"Expected a block after {name}.");
                var depth = 1; var closing = opening;
                while (depth > 0) { closing = Next(); if (closing.Text == "{") depth++; if (closing.Text == "}") depth--; }
                if (!actions.TryAdd(name, source[opening.Span.End..closing.SpanStart])) throw new Exception($"Duplicate action: {name}.");
            }
        }
        if (states.Count == 0) throw new Exception("At least one ASL state descriptor is required.");
        return new(states, actions);
    }
}
