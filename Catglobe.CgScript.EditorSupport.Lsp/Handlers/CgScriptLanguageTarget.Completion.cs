using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
using Catglobe.CgScript.EditorSupport.Parsing;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;
using System.Collections.Concurrent;
using System.Threading;
using LspDiagnostic = Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic;
using LspRange      = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace Catglobe.CgScript.EditorSupport.Lsp.Handlers;

public partial class CgScriptLanguageTarget
{
   // ── completion ───────────────────────────────────────────────────────────────

   public virtual SumType<CompletionItem[], CompletionList>? OnCompletion(CompletionParams p)
   {
      var text        = _store.GetText(p.TextDocument.Uri.ToString()) ?? string.Empty;
      var offset      = GetOffset(text, p.Position.Line, p.Position.Character);

      // ── Doc comment template trigger: user just typed the third '/' ───────────
      var docItem = TryGetDocCommentCompletion(text, p.Position.Line, offset);
      if (docItem is not null)
         return new SumType<CompletionItem[], CompletionList>(
            new CompletionList { IsIncomplete = false, Items = [docItem] });

      var prefix      = GetWordPrefix(text, offset);
      int prefixStart = offset - prefix.Length;
      bool afterDot   = prefixStart > 0 && text[prefixStart - 1] == '.';

      var list = afterDot
         ? MemberCompletions(text, prefixStart - 1, prefix, _store.GetParseResult(p.TextDocument.Uri.ToString())?.Tree)
         : TopLevelCompletions(prefix);
      return new SumType<CompletionItem[], CompletionList>(list);
   }

   /// <summary>
   /// Returns completions for member access (after a <c>.</c>).
   /// Resolves the receiver variable to its declared type, then returns that type's
   /// methods and properties.  Falls back to all objects if the type cannot be resolved.
   /// </summary>
   private CompletionList MemberCompletions(string text, int dotPos, string prefix, Antlr4.Runtime.Tree.IParseTree? tree)
   {
      var receiverName = GetIdentifierBefore(text, dotPos);
      ObjectDefinition? exact = null;
      bool isStaticAccess = false;

      if (receiverName != null)
      {
         // Direct match: receiver IS a type name (e.g. "Tenant.StaticMethod")
         if (_definitions.Objects.TryGetValue(receiverName, out exact))
         {
            isStaticAccess = true;
         }
         else
         {
            // Resolve the variable name to its declared type
            var typeName = ResolveVariableType(receiverName, text, tree);
            if (typeName != null)
               _definitions.Objects.TryGetValue(typeName, out exact);
         }
      }

      // If we couldn't resolve the receiver to a known type, return empty rather than
      // flooding the list with every method from every type.
      if (exact == null)
         return new CompletionList { IsIncomplete = false, Items = [] };

      var items = new List<CompletionItem>();
      {
         var obj = exact;
         // Instance access → instance methods only; type-name access → static methods only
         var methods = isStaticAccess
            ? (obj.StaticMethods ?? [])
            : (obj.Methods ?? []);

         var groups = methods
            .Where(m => prefix.Length == 0 || m.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .GroupBy(m => m.Name);

         foreach (var group in groups)
         {
            var overloads = group.ToList();
            var first     = overloads[0];
            items.Add(new CompletionItem
            {
               Label         = overloads.Count == 1
                                  ? $"{first.Name}({BuildMethodParamList(first.Param)})"
                                  : $"{first.Name}(+{overloads.Count} overloads)",
               FilterText    = first.Name,
               InsertText    = first.Name,
               Kind          = CompletionItemKind.Method,
               Detail        = first.ReturnType,
               Documentation = new SumType<string, MarkupContent>(new MarkupContent
               {
                  Kind  = MarkupKind.Markdown,
                  Value = string.Join("\n\n---\n\n", overloads.Select(m =>
                     (string.IsNullOrWhiteSpace(m.Doc) ? "" : $"{m.Doc}\n\n") + $"`{m.ReturnType} {m.Name}({BuildMethodParamList(m.Param)})`")),
               }),
            });
         }

         foreach (var prop in obj.Properties ?? [])
         {
            if (prefix.Length > 0 && !prop.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
               continue;
            items.Add(new CompletionItem
            {
               Label         = prop.Name,
               FilterText    = prop.Name,
               InsertText    = prop.Name,
               Kind          = CompletionItemKind.Property,
               Detail        = prop.ReturnType,
               Documentation = new SumType<string, MarkupContent>(new MarkupContent
               {
                  Kind  = MarkupKind.Markdown,
                  Value = (string.IsNullOrWhiteSpace(prop.Doc) ? "" : $"{prop.Doc}\n\n") + $"`{prop.ReturnType} {prop.Name}`",
               }),
            });
         }
      }
      return new CompletionList { IsIncomplete = false, Items = items.ToArray() };
   }

   /// <summary>
   /// Resolves a variable name to its declared type by checking the parse tree (all
   /// scopes) first, then falling back to a simple text scan.
   /// </summary>
   private string? ResolveVariableType(string varName, string text, Antlr4.Runtime.Tree.IParseTree? tree)
   {
      if (tree != null)
      {
         var sym = DocumentSymbolCollector.CollectAll(tree).FirstOrDefault(s => s.Name == varName);
         if (sym != null) return sym.TypeName;
      }
      // Text-based fallback: search each line for "TypeName varName"
      foreach (var line in text.Split('\n'))
      {
         var trimmed = line.TrimStart();
         var spaceIdx = trimmed.IndexOf(' ');
         if (spaceIdx <= 0) continue;
         var rest = trimmed[(spaceIdx + 1)..].TrimStart();
         if (rest.StartsWith(varName) && (rest.Length == varName.Length || rest[varName.Length] is ' ' or '='))
            return trimmed[..spaceIdx];
      }
      return null;
   }

   /// <summary>
   /// Returns top-level completions (functions, types, keywords, constants) filtered
   /// by whatever identifier prefix the user has already typed.
   /// </summary>
   private CompletionList TopLevelCompletions(string prefix)
   {
      bool all = prefix.Length == 0;
      var items = new List<CompletionItem>();

      foreach (var (name, fn) in _definitions.Functions)
      {
         if (all || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            items.Add(new CompletionItem
            {
               Label         = BuildFunctionLabel(name, fn),
               FilterText    = name,
               InsertText    = name,
               Kind          = CompletionItemKind.Function,
               Detail        = GetFunctionReturnType(fn),
               Documentation = new SumType<string, MarkupContent>(new MarkupContent
               {
                  Kind  = MarkupKind.Markdown,
                  Value = BuildFunctionHover(name, fn),
               }),
            });
      }

      foreach (var (name, obj) in _definitions.Objects)
      {
         if (all || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            items.Add(new CompletionItem
            {
               Label         = name,
               Kind          = CompletionItemKind.Class,
               Detail        = obj.Doc,
               Documentation = obj.Doc,
            });
      }

      foreach (var name in _definitions.Constants)
      {
         if (all || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            items.Add(new CompletionItem { Label = name, Kind = CompletionItemKind.Constant });
      }

      foreach (var kw in Keywords)
      {
         if (all || kw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            items.Add(new CompletionItem { Label = kw, Kind = CompletionItemKind.Keyword });
      }

      return new CompletionList { IsIncomplete = false, Items = items.ToArray() };
   }

   // ── Doc comment template generation ─────────────────────────────────────────

   private static readonly System.Text.RegularExpressions.Regex FunctionDeclForDoc =
      new(@"function\s*\(([^)]*)\)", System.Text.RegularExpressions.RegexOptions.Compiled);

   /// <summary>
   /// When the cursor is at the end of a line that is just <c>///</c> (optional leading
   /// whitespace), looks ahead to find the next non-empty, non-comment line.  If that line
   /// contains a <c>function(...)</c> declaration, returns a completion item that inserts
   /// the full C# XML doc template as a snippet.
   /// Returns <c>null</c> if the conditions are not met.
   /// </summary>
   private static CompletionItem? TryGetDocCommentCompletion(string text, int cursorLine, int offset)
   {
      // Current line up to cursor must be exactly optional-whitespace + "///"
      var lines    = text.Split('\n');
      if (cursorLine >= lines.Length) return null;
      var lineText = lines[cursorLine];
      // Strip trailing \r
      var lineTrimmed = lineText.TrimEnd('\r').TrimStart();
      if (lineTrimmed != "///") return null;

      // Find next non-empty, non-comment line
      string? nextLine = null;
      for (int i = cursorLine + 1; i < lines.Length; i++)
      {
         var t = lines[i].TrimEnd('\r').Trim();
         if (t.Length == 0 || t.StartsWith("//")) continue;
         nextLine = t;
         break;
      }
      if (nextLine is null) return null;

      // Must be a function declaration
      var fnMatch = FunctionDeclForDoc.Match(nextLine);
      if (!fnMatch.Success) return null;

      // Build the snippet
      var indent   = lineText.Length - lineTrimmed.Length;  // leading spaces/tabs count
      var indentStr = lineText[..indent];
      var snippet  = BuildDocSnippet(indentStr, fnMatch.Groups[1].Value);

      // The insert range replaces the current "///" on the line
      var lineStartChar = indent;  // character index of the "///"
      return new CompletionItem
      {
         Label           = "/// XML doc comment",
         Kind            = CompletionItemKind.Snippet,
         Detail          = "Generate XML doc comment",
         InsertTextFormat = InsertTextFormat.Snippet,
         TextEdit        = new TextEdit
         {
            NewText = snippet,
            Range   = new LspRange
            {
               Start = new Position(cursorLine, lineStartChar),
               End   = new Position(cursorLine, lineText.TrimEnd('\r').Length),
            },
         },
      };
   }

   /// <summary>Builds the LSP snippet text for the XML doc template.</summary>
   private static string BuildDocSnippet(string indent, string paramList)
   {
      var sb = new System.Text.StringBuilder();
      var paramNames = ParseParamNames(paramList);

      sb.Append($"/// <summary>");
      sb.Append("$1");
      sb.AppendLine("</summary>");

      int tabStop = 2;
      foreach (var (cgsType, name) in paramNames)
      {
         // Ambiguous types get a type attribute placeholder; primitives just get doc text
         bool needsType = cgsType is "array" or "object" or "question" or "number";
         if (needsType)
            sb.Append($"{indent}/// <param name=\"{name}\" type=\"${tabStop++}\">${tabStop++}</param>");
         else
            sb.Append($"{indent}/// <param name=\"{name}\">${tabStop++}</param>");
         sb.AppendLine();
      }

      sb.Append($"{indent}/// <returns>${tabStop}</returns>");
      return sb.ToString();
   }

   /// <summary>Parses <c>type name, type name</c> pairs from a function parameter list.</summary>
   private static IReadOnlyList<(string Type, string Name)> ParseParamNames(string paramList)
   {
      var result = new List<(string, string)>();
      if (string.IsNullOrWhiteSpace(paramList)) return result;
      foreach (var part in paramList.Split(','))
      {
         var tokens = part.Trim().Split(new[] { ' ', '\t' },
            System.StringSplitOptions.RemoveEmptyEntries);
         if (tokens.Length >= 2)
            result.Add((tokens[0], tokens[1]));
      }
      return result;
   }
}
