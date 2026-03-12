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
   // ── hover ─────────────────────────────────────────────────────────────────────

   /// <summary>
   /// Returns a Markdown hover for the word under the cursor.
   /// Checks built-in functions, object types, and constants in that order before
   /// falling back to a declaration look-up in the parse tree.
   /// </summary>
   public Hover? OnHover(TextDocumentPositionParams p)
   {
      var text = _store.GetText(p.TextDocument.Uri.ToString());
      if (text is null) return null;

      var offset = GetOffset(text, p.Position.Line, p.Position.Character);
      var word   = GetWordAt(text, offset);
      if (string.IsNullOrEmpty(word)) return null;

      // ── Method / property on a known object type (cursor after a dot) ──────────
      int wordStart = offset;
      while (wordStart > 0 && IsWordChar(text[wordStart - 1])) wordStart--;
      if (wordStart > 0 && text[wordStart - 1] == '.')
      {
         var receiverName = GetIdentifierBefore(text, wordStart - 1);
         ObjectDefinition? exactObj = null;
         bool isStaticAccess = false;
         if (receiverName != null)
         {
            if (_definitions.Objects.TryGetValue(receiverName, out exactObj))
               isStaticAccess = true;
            else
            {
               var typeName = ResolveVariableType(receiverName, text, _store.GetParseResult(p.TextDocument.Uri.ToString())?.Tree);
               if (typeName != null) _definitions.Objects.TryGetValue(typeName, out exactObj);
            }
         }
         IEnumerable<ObjectDefinition> candidates = exactObj != null
            ? [(ObjectDefinition)exactObj]
            : (IEnumerable<ObjectDefinition>)_definitions.Objects.Values;

         foreach (var candidate in candidates)
         {
            // Properties shown for both instance and static access
            var prop = (candidate.Properties ?? []).FirstOrDefault(
               pr => string.Equals(pr.Name, word, StringComparison.OrdinalIgnoreCase));
            if (prop != null)
               return new Hover
               {
                  Contents = HoverContent(
                     (string.IsNullOrWhiteSpace(prop.Doc) ? "" : $"{prop.Doc}\n\n")
                     + $"`{prop.ReturnType} {prop.Name}`"),
               };

            var methodSource = isStaticAccess ? (candidate.StaticMethods ?? []) : (candidate.Methods ?? []);
            var methods = methodSource
               .Where(m => string.Equals(m.Name, word, StringComparison.OrdinalIgnoreCase))
               .ToList();
            if (methods.Count == 0) continue;

            var sb = new System.Text.StringBuilder();
            foreach (var m in methods)
            {
               if (sb.Length > 0) sb.Append("\n\n---\n\n");
               if (!string.IsNullOrWhiteSpace(m.Doc)) sb.Append($"{m.Doc}\n\n");
               sb.Append($"`{m.ReturnType} {m.Name}({BuildMethodParamList(m.Param ?? [])})`");
               if (m.Param?.Length > 0)
               {
                  sb.Append("\n\n**Parameters:**");
                  foreach (var mp in m.Param)
                     sb.Append($"\n- `{mp.Type} {mp.Name}`{(string.IsNullOrWhiteSpace(mp.Doc) ? "" : $" — {mp.Doc}")}");
               }
            }
            return new Hover
            {
               Contents = HoverContent(sb.ToString()),
            };
         }
      }

      // ── Built-in function ──────────────────────────────────────────────────────
      if (_definitions.Functions.TryGetValue(word, out var fn))
      {
         return new Hover
         {
            Contents = HoverContent(BuildFunctionHover(word, fn)),
         };
      }

      // ── Built-in object type ───────────────────────────────────────────────────
      if (_definitions.Objects.TryGetValue(word, out var obj))
      {
         return new Hover
         {
            Contents = HoverContent(obj.Doc ?? word),
         };
      }

      // ── Built-in constant ──────────────────────────────────────────────────────
      if (_definitions.Constants.Contains(word))
      {
         return new Hover
         {
            Contents = HoverContent($"constant: {word}"),
         };
      }

      // ── User-defined symbol: show declared type ───────────────────────────────
      var result = _store.GetParseResult(p.TextDocument.Uri.ToString());
      if (result is not null)
      {
         var decl = ReferenceAnalyzer.FindDeclaration(
            result.Tree,
            cursorLine:   p.Position.Line + 1,
            cursorColumn: p.Position.Character);

         if (decl is not null)
         {
            // Use CollectAll to find declarations at any nesting depth.
            var sym = DocumentSymbolCollector.CollectAll(result.Tree)
               .FirstOrDefault(s => s.Name == word);
            var typeLabel = sym is not null ? sym.TypeName : "?";
            return new Hover
            {
               Contents = HoverContent($"{typeLabel} {word}"),
            };
         }
      }

      return null;
   }

   // ── string builders ───────────────────────────────────────────────────────────

   /// <summary>
   /// Converts a markdown-formatted string to <see cref="MarkupContent"/> respecting
   /// what the client declared during <c>initialize</c>. VS doesn't declare markdown
   /// support, so we strip inline syntax to plain text instead of showing raw asterisks.
   /// </summary>
   private MarkupContent HoverContent(string markdown)
   {
      if (_clientSupportsMarkdownHover)
         return new MarkupContent { Kind = MarkupKind.Markdown, Value = markdown };

      // Strip inline markdown for clients that only understand plain text
      var plain = System.Text.RegularExpressions.Regex.Replace(markdown, @"`([^`]*)`", "$1");
      plain = System.Text.RegularExpressions.Regex.Replace(plain, @"\*\*([^*]*)\*\*", "$1");
      plain = System.Text.RegularExpressions.Regex.Replace(plain, @"\*([^*]*)\*", "$1");
      plain = plain.Replace("\n\n---\n\n", "\n\n");
      return new MarkupContent { Kind = MarkupKind.PlainText, Value = plain };
   }

   private static string BuildParamList(FunctionParam[]? parameters)
      => parameters is null ? string.Empty
         : string.Join(", ", parameters.Select(p => $"{p.ConstantType} {p.Name}{(p.IsOptional ? "?" : "")}"));

   private static string BuildVariantParamList(FunctionVariantParam[]? parameters)
      => parameters is null ? string.Empty
         : string.Join(", ", parameters.Select(p => $"{p.Type} {p.Name}"));

   private static string BuildMethodParamList(MethodParam[]? parameters)
      => parameters is null ? string.Empty
         : string.Join(", ", parameters.Select(p => $"{p.Type} {p.Name}"));

   private static string GetFunctionReturnType(FunctionDefinition fn)
      => fn.IsNewStyle && fn.Variants?.Length > 0 ? fn.Variants[0].ReturnType : fn.ReturnType ?? string.Empty;

   private static string BuildFunctionLabel(string name, FunctionDefinition fn)
   {
      if (fn.IsNewStyle && fn.Variants?.Length > 0)
      {
         var first = fn.Variants[0];
         return fn.Variants.Length == 1
            ? $"{name}({BuildVariantParamList(first.Param)})"
            : $"{name}(+{fn.Variants.Length} overloads)";
      }
      return $"{name}({BuildParamList(fn.Parameters)})";
   }

   /// <summary>Builds the markdown hover text for a built-in function.</summary>
   private static string BuildFunctionHover(string name, FunctionDefinition fn)
   {
      if (fn.IsNewStyle && fn.Variants?.Length > 0)
      {
         var sb = new System.Text.StringBuilder();
         foreach (var v in fn.Variants)
         {
            if (sb.Length > 0) sb.Append("\n\n---\n\n");
            if (!string.IsNullOrWhiteSpace(v.Doc)) sb.Append($"{v.Doc}\n\n");
            sb.Append($"`{v.ReturnType} {name}({BuildVariantParamList(v.Param)})`");
            if (v.Param?.Length > 0)
            {
               sb.Append("\n\n**Parameters:**");
               foreach (var p in v.Param)
                  sb.Append($"\n- `{p.Type} {p.Name}` — {p.Doc}");
            }
         }
         return sb.ToString();
      }
      // Old-style: no doc available, show signature + param types
      var sig = $"`{fn.ReturnType} {name}({BuildParamList(fn.Parameters)})`";
      if (fn.Parameters is null || fn.Parameters.Length == 0) return sig;
      var doc = new System.Text.StringBuilder(sig);
      doc.Append("\n\n**Parameters:**");
      foreach (var p in fn.Parameters)
         doc.Append($"\n- `{p.ConstantType} {p.Name}`{(p.IsOptional ? " *(optional)*" : "")}");
      return doc.ToString();
   }

   private static SignatureInformation[] BuildSignatureInfoList(string funcName, FunctionDefinition fn)
   {
      if (fn.IsNewStyle && fn.Variants?.Length > 0)
      {
         return fn.Variants.Select(v => new SignatureInformation
         {
            Label         = $"{v.ReturnType} {funcName}({BuildVariantParamList(v.Param)})",
            Documentation = new SumType<string, MarkupContent>(
               new MarkupContent { Kind = MarkupKind.Markdown, Value = v.Doc ?? string.Empty }),
            Parameters    = (v.Param ?? []).Select(p =>
               new ParameterInformation
               {
                  Label         = new SumType<string, Tuple<int, int>>($"{p.Type} {p.Name}"),
                  Documentation = new SumType<string, MarkupContent>(p.Doc ?? string.Empty),
               }).ToArray(),
         }).ToArray();
      }
      return
      [
         new SignatureInformation
         {
            Label         = $"{fn.ReturnType} {funcName}({BuildParamList(fn.Parameters)})",
            Documentation = new SumType<string, MarkupContent>(
               new MarkupContent { Kind = MarkupKind.Markdown, Value = BuildFunctionHover(funcName, fn) }),
            Parameters    = (fn.Parameters ?? []).Select(p =>
               new ParameterInformation
               {
                  Label         = new SumType<string, Tuple<int, int>>($"{p.ConstantType} {p.Name}"),
                  Documentation = new SumType<string, MarkupContent>(p.IsOptional ? "(optional)" : string.Empty),
               }).ToArray(),
         },
      ];
   }

   private static string BuildFunctionDoc(FunctionDefinition fn)
   {
      if (fn.IsNewStyle && fn.Variants?.Length > 0)
         return string.Join("\n\n---\n\n", fn.Variants.Select(v => v.Doc ?? string.Empty));
      if (fn.Parameters is null || fn.Parameters.Length == 0) return $"Returns {fn.ReturnType}";
      var sb = new System.Text.StringBuilder();
      sb.AppendLine($"Returns {fn.ReturnType}");
      sb.AppendLine("Parameters:");
      foreach (var p in fn.Parameters)
         sb.AppendLine($"  {p.Name} : {p.ConstantType}{(p.IsOptional ? " (optional)" : "")}");
      return sb.ToString();
   }
}
