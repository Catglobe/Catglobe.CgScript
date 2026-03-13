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
      var docItem = TryGetDocCommentCompletion(text, p.Position.Line, offset, _clientSupportsSnippets);
      if (docItem is not null)
         return new SumType<CompletionItem[], CompletionList>(
            new CompletionList { IsIncomplete = false, Items = [docItem] });

      // Inside any comment line (// or ///) no code completions make sense.
      // For '///' lines, TryGetDocCommentCompletion() already handled the template above.
      var currentLineText = text.Split('\n').ElementAtOrDefault(p.Position.Line) ?? string.Empty;
      if (currentLineText.TrimStart().StartsWith("//"))
         return new SumType<CompletionItem[], CompletionList>(
            new CompletionList { IsIncomplete = false, Items = [] });

      var prefix      = GetWordPrefix(text, offset);
      int prefixStart = offset - prefix.Length;
      bool afterDot   = prefixStart > 0 && text[prefixStart - 1] == '.';

      var parseResult = _store.GetParseResult(p.TextDocument.Uri.ToString());
      var list = afterDot
         ? MemberCompletions(text, prefixStart - 1, prefix, parseResult?.Tree)
         : TopLevelCompletions(prefix, text, parseResult?.Tree);
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
            // Resolve variable or chained property expression (e.g. Catglobe.Json)
            exact = ResolveReceiverObjectAtDot(text, dotPos, tree);
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
   /// Resolves the receiver expression to the left of a dot at <paramref name="dotPos"/>
   /// to its <see cref="ObjectDefinition"/>, supporting chained access such as
   /// <c>Catglobe.Json</c> (where <c>Catglobe</c> is a global variable of type
   /// <c>GlobalNamespace</c> and <c>Json</c> is a property of that type).
   /// Returns <c>null</c> when the type cannot be determined.
   /// </summary>
   private ObjectDefinition? ResolveReceiverObjectAtDot(
      string text, int dotPos, Antlr4.Runtime.Tree.IParseTree? tree)
   {
      var receiverName = GetIdentifierBefore(text, dotPos);
      if (receiverName is null) return null;

      // Direct match: receiver is a known type name (static / namespace access)
      if (_definitions.Objects.TryGetValue(receiverName, out var direct))
         return direct;

      // Resolve as a local or global variable
      var typeName = ResolveVariableType(receiverName, text, tree);
      if (typeName != null && _definitions.Objects.TryGetValue(typeName, out var fromVar))
         return fromVar;

      // Chained: check for another dot to the left of this identifier and recurse
      int idEnd   = dotPos;
      while (idEnd > 0 && text[idEnd - 1] == ' ') idEnd--;
      int idStart = idEnd - receiverName.Length;
      if (idStart > 0 && text[idStart - 1] == '.')
      {
         var innerObj = ResolveReceiverObjectAtDot(text, idStart - 1, tree);
         if (innerObj != null)
         {
            var prop = (innerObj.Properties ?? [])
               .FirstOrDefault(p => string.Equals(p.Name, receiverName, StringComparison.Ordinal));
            if (prop?.ReturnType != null
                && _definitions.Objects.TryGetValue(prop.ReturnType, out var propType))
               return propType;
         }
      }

      return null;
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
      // Global variables pre-declared by the runtime
      if (_definitions.GlobalVariables.TryGetValue(varName, out var globalType))
         return globalType;
      return null;
   }

   /// <summary>
   /// Returns top-level completions (functions, types, keywords, constants, and local
   /// variables) filtered by whatever identifier prefix the user has already typed.
   /// </summary>
   private CompletionList TopLevelCompletions(string prefix, string text = "", IParseTree? tree = null)
   {
      bool all = prefix.Length == 0;
      var items = new List<CompletionItem>();

      // Local variables declared in this document (including function parameters)
      var localVars = tree != null
         ? DocumentSymbolCollector.CollectAll(tree)
         : CollectVariablesFromText(text);
      foreach (var sym in localVars)
      {
         if (sym.Kind is not "variable" and not "parameter") continue;
         if (!all && !sym.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
         items.Add(new CompletionItem
         {
            Label      = sym.Name,
            FilterText = sym.Name,
            InsertText = sym.Name,
            Kind       = CompletionItemKind.Variable,
            Detail     = sym.TypeName,
         });
      }

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
               Documentation = obj.Doc,
            });
      }

      foreach (var name in _definitions.Constants)
      {
         if (all || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            items.Add(new CompletionItem { Label = name, Kind = CompletionItemKind.Constant });
      }

      foreach (var (name, typeName) in _definitions.GlobalVariables)
      {
         if (all || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            items.Add(new CompletionItem
            {
               Label  = name,
               Kind   = CompletionItemKind.Variable,
               Detail = typeName,
            });
      }

      foreach (var kw in Keywords)
      {
         if (all || kw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            items.Add(new CompletionItem { Label = kw, Kind = CompletionItemKind.Keyword });
      }

      if (_clientSupportsSnippets)
      {
         foreach (var (label, filter, snippet) in StatementSnippets)
         {
            if (all || filter.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
               items.Add(new CompletionItem
               {
                  Label            = label,
                  FilterText       = filter,
                  InsertText       = snippet,
                  InsertTextFormat = InsertTextFormat.Snippet,
                  Kind             = CompletionItemKind.Snippet,
               });
         }
      }

      return new CompletionList { IsIncomplete = false, Items = items.ToArray() };
   }

   /// <summary>
   /// Text-based fallback: collects variable declarations by scanning each line for the
   /// pattern <c>TypeName varName</c>.  Used when no parse tree is available.
   /// </summary>
   private static IReadOnlyList<DocumentSymbolInfo> CollectVariablesFromText(string text)
   {
      var result = new List<DocumentSymbolInfo>();
      var lines  = text.Split('\n');
      for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
      {
         var trimmed  = lines[lineIdx].TrimStart();
         var spaceIdx = trimmed.IndexOf(' ');
         if (spaceIdx <= 0) continue;
         var typeName = trimmed[..spaceIdx];
         // Reject lines that start with a control-flow keyword (if/for/while/return/…)
         if (Array.IndexOf(Keywords, typeName) >= 0) continue;
         // typeName must be a valid identifier (letters, digits, underscore; starts with letter or underscore)
         if (!IsValidIdentifier(typeName)) continue;
         var rest     = trimmed[(spaceIdx + 1)..].TrimStart();
         // Extract identifier: stop at space, '=', ';', ','
         int nameEnd = 0;
         while (nameEnd < rest.Length && rest[nameEnd] is not ' ' and not '=' and not ';' and not ',')
            nameEnd++;
         if (nameEnd == 0) continue;
         var varName = rest[..nameEnd];
         if (!IsValidIdentifier(varName)) continue;
         result.Add(new DocumentSymbolInfo(
            Name:        varName,
            Kind:        "variable",
            TypeName:    typeName,
            StartLine:   lineIdx + 1,
            StartColumn: 0,
            EndLine:     lineIdx + 1,
            EndColumn:   0,
            NameLine:    lineIdx + 1,
            NameColumn:  spaceIdx + 1,
            NameLength:  varName.Length));
      }
      return result;
   }

   private static bool IsValidIdentifier(string s)
   {
      if (s.Length == 0) return false;
      if (!char.IsLetter(s[0]) && s[0] != '_') return false;
      foreach (var c in s.AsSpan(1))
         if (!char.IsLetterOrDigit(c) && c != '_') return false;
      return true;
   }

   // All language keywords from the lexer grammar.
   private static readonly string[] Keywords =
   [
      "if", "else", "while", "for", "break", "continue", "return",
      "true", "false", "empty", "new", "switch", "case", "default",
      "try", "catch", "throw", "where",
      "object", "function",
   ];

   // Pre-defined snippets for common language constructs.
   internal static readonly (string Label, string Filter, string Snippet)[] StatementSnippets =
   [
      ("if statement",        "if",     "if (${1:condition}) {\n\t$0\n}"),
      ("if-else statement",   "if",     "if (${1:condition}) {\n\t$2\n} else {\n\t$0\n}"),
      ("while statement",     "while",  "while (${1:condition}) {\n\t$0\n}"),
      ("for-in statement",    "for",    "for (${1:item} for ${2:collection}; ${3:count}) {\n\t$0\n}"),
      ("for-var statement",   "for",    "for (${1:number} ${2:i} = ${3:0}; ${4:condition}; ${2:i} = ${2:i} + 1) {\n\t$0\n}"),
      ("switch statement",    "switch", "switch (${1:expression}) {\n\tcase ${2:value}:\n\t\t$0\n\t\tbreak;\n\tdefault:\n\t\tbreak;\n}"),
      ("try-catch statement", "try",      "try {\n\t$1\n} catch (${2:e}) {\n\t$0\n}"),
      ("function expression", "function", "Function ${1:name} = function(${2:params}) {\n\t$0\n};"),
   ];

   // ── Doc comment template generation ─────────────────────────────────────────

   /// <summary>
   /// When the cursor is at the end of a line that is just <c>///</c> (optional leading
   /// whitespace, optional trailing whitespace), looks ahead to find the next non-empty,
   /// non-comment line.  If that line (or continuation lines) contains a
   /// <c>function(...)</c> declaration, returns a completion item that inserts the full
   /// C# XML doc template as a snippet.
   /// Returns <c>null</c> if the conditions are not met.
   /// </summary>
   private CompletionItem? TryGetDocCommentCompletion(string text, int cursorLine, int offset, bool snippetSupport)
   {
      var lines    = text.Split('\n');
      if (cursorLine >= lines.Length) return null;
      var lineText = lines[cursorLine];

      if (!IsDocCommentOnlyLine(lineText, out int indent)) return null;
      var indentStr = lineText[..indent];

      // Don't offer the template if a <summary> already exists in this doc block.
      for (int i = cursorLine - 1; i >= 0; i--)
      {
         var t = lines[i];
         if (!t.TrimStart().StartsWith("///")) break;
         if (t.Contains("<summary>", StringComparison.OrdinalIgnoreCase)) return null;
      }

      var paramList = FindNextFunctionParams(lines, cursorLine);
      if (paramList is null) return null;

      var insertText = snippetSupport ? BuildDocSnippet(indentStr, paramList) : BuildDocTemplate(indentStr, paramList);

      return new CompletionItem
      {
         Label            = "/// XML doc comment",
         Kind             = CompletionItemKind.Snippet,
         Detail           = "Generate XML doc comment",
         Preselect        = true,
         FilterText       = "///",
         SortText         = "\x00",
         InsertTextFormat = snippetSupport ? InsertTextFormat.Snippet : InsertTextFormat.Plaintext,
         TextEdit         = new TextEdit
         {
            NewText = insertText,
            Range   = new LspRange
            {
               Start = new Position(cursorLine, indent),
               End   = new Position(cursorLine, lineText.TrimEnd('\r', '\n').Length),
            },
         },
      };
   }

   /// <summary>Builds the LSP snippet text for the XML doc template.</summary>
   private static string BuildDocSnippet(string indent, string paramList)
   {
      var sb         = new System.Text.StringBuilder();
      var paramNames = ParseParamNames(paramList);

      sb.Append("/// <summary>$1</summary>");

      int tabStop = 2;
      foreach (var (cgsType, name) in paramNames)
      {
         bool needsType = cgsType is "array" or "object" or "question" or "number";
         sb.AppendLine();
         if (needsType)
            sb.Append($"{indent}/// <param name=\"{name}\" type=\"${tabStop++}\">${tabStop++}</param>");
         else
            sb.Append($"{indent}/// <param name=\"{name}\">${tabStop++}</param>");
      }

      sb.AppendLine();
      sb.Append($"{indent}/// <returns type=\"${tabStop++}\">${tabStop}</returns>");
      return sb.ToString();
   }

   /// <summary>Builds a plain-text doc-comment template (no snippet syntax) for clients that do not support snippets.</summary>
   private static string BuildDocTemplate(string indent, string paramList)
   {
      var sb         = new System.Text.StringBuilder();
      var paramNames = ParseParamNames(paramList);

      sb.Append("/// <summary></summary>");

      foreach (var (cgsType, name) in paramNames)
      {
         sb.AppendLine();
         bool needsType = cgsType is "array" or "object" or "question" or "number";
         if (needsType)
            sb.Append($"{indent}/// <param name=\"{name}\" type=\"\"></param>");
         else
            sb.Append($"{indent}/// <param name=\"{name}\"></param>");
      }

      sb.AppendLine();
      sb.Append($"{indent}/// <returns></returns>");
      return sb.ToString();
   }

   // ── Shared doc-comment helpers (also used by onTypeFormatting) ─────────────────

   /// <summary>
   /// Returns <c>true</c> when <paramref name="lineText"/> contains only optional
   /// whitespace followed by <c>///</c> (and optional trailing whitespace / <c>\r</c>).
   /// Outputs the number of leading whitespace characters as <paramref name="indent"/>.
   /// </summary>
   internal static bool IsDocCommentOnlyLine(string lineText, out int indent)
   {
      var stripped = lineText.TrimEnd('\r', '\n', ' ', '\t');
      var content  = stripped.TrimStart();
      indent       = stripped.Length - content.Length;
      return content == "///";
   }

   /// <summary>
   /// Searches forward from <paramref name="cursorLine"/> for the next non-empty,
   /// non-comment line that starts a <c>function(...)</c> declaration, accumulating
   /// continuation lines so that multi-line parameter lists are handled correctly.
   /// Returns <c>null</c> if no matching declaration is found.
   /// </summary>
   internal static string? FindNextFunctionParams(string[] lines, int cursorLine)
   {
      var accumulated  = new System.Text.StringBuilder();
      bool foundStart  = false;

      for (int i = cursorLine + 1; i < lines.Length; i++)
      {
         var t = lines[i].TrimEnd('\r').Trim();
         if (t.Length == 0 || t.StartsWith("//")) continue;

         if (!foundStart)
         {
            // First non-empty line must contain "function" keyword (case-sensitive)
            if (t.IndexOf("function", StringComparison.Ordinal) < 0) return null;
            foundStart = true;
         }

         accumulated.Append(' ').Append(t);

         var result = ExtractFunctionParams(accumulated.ToString());
         if (result != null) return result;

         // Guard against runaway accumulation (e.g. malformed code)
         if (i - cursorLine > 15) return null;
      }
      return null;
   }

   /// <summary>
   /// Extracts the top-level parameter list from a <c>function(...)</c> token in
   /// <paramref name="line"/>, correctly handling nested parentheses such as
   /// <c>new StringBuilder()</c> in default values.
   /// Returns <c>null</c> when no <c>function(</c> is found.
   /// </summary>
   internal static string? ExtractFunctionParams(string line)
   {
      int fi = line.IndexOf("function", StringComparison.Ordinal);
      if (fi < 0) return null;
      int pi = line.IndexOf('(', fi + "function".Length);
      if (pi < 0) return null;

      int depth = 0, start = pi + 1;
      for (int i = pi; i < line.Length; i++)
      {
         if (line[i] == '(') depth++;
         else if (line[i] == ')')
         {
            if (--depth == 0) return line[start..i];
         }
      }
      return null; // unmatched parenthesis
   }

   /// <summary>
   /// Parses <c>type name[, type name ...]</c> pairs from a function parameter list,
   /// splitting on commas at depth 0 to correctly handle nested parentheses in default
   /// values like <c>new StringBuilder()</c>.
   /// </summary>
   internal static IReadOnlyList<(string Type, string Name)> ParseParamNames(string paramList)
   {
      var result = new List<(string, string)>();
      if (string.IsNullOrWhiteSpace(paramList)) return result;

      int depth = 0, start = 0;
      for (int i = 0; i <= paramList.Length; i++)
      {
         char c = i < paramList.Length ? paramList[i] : ',';
         if (c == '(' || c == '[') depth++;
         else if (c == ')' || c == ']') depth--;
         else if (c == ',' && depth == 0)
         {
            AddParsedParam(result, paramList.AsSpan(start, i - start));
            start = i + 1;
         }
      }
      return result;
   }

   private static void AddParsedParam(List<(string, string)> result, ReadOnlySpan<char> part)
   {
      // Strip default value (everything from '=' onward)
      int eq = part.IndexOf('=');
      if (eq >= 0) part = part[..eq].TrimEnd();
      part = part.Trim();

      // First whitespace-delimited token = type
      int wsStart = 0;
      while (wsStart < part.Length && !char.IsWhiteSpace(part[wsStart])) wsStart++;
      if (wsStart >= part.Length) return;

      var type = part[..wsStart].ToString();
      var rest = part[wsStart..].TrimStart();

      // Second token = name (identifier characters only)
      int nameEnd = 0;
      while (nameEnd < rest.Length && (char.IsLetterOrDigit(rest[nameEnd]) || rest[nameEnd] == '_'))
         nameEnd++;
      if (nameEnd == 0) return;

      result.Add((type, rest[..nameEnd].ToString()));
   }
}
