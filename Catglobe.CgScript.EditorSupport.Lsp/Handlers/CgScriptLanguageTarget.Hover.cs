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
   public Hover OnHover(TextDocumentPositionParams p)
   {
      var text = _store.GetText(p.TextDocument.Uri.ToString());
      if (text is null) return null!;

      var offset = GetOffset(text, p.Position.Line, p.Position.Character);
      var word   = GetWordAt(text, offset);
      if (string.IsNullOrEmpty(word)) return null!;

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
         var sb = new System.Text.StringBuilder();
         if (!string.IsNullOrWhiteSpace(obj.Doc)) sb.Append(obj.Doc);
         if (obj.Constructors?.Length > 0)
         {
            foreach (var ctor in obj.Constructors)
            {
               if (sb.Length > 0) sb.Append("\n\n---\n\n");
               if (!string.IsNullOrWhiteSpace(ctor.Doc)) sb.Append($"{ctor.Doc}\n\n");
               sb.Append($"`new {word}({BuildMethodParamList(ctor.Param)})`");
               if (ctor.Param?.Length > 0)
               {
                  sb.Append("\n\n**Parameters:**");
                  foreach (var mp in ctor.Param)
                     sb.Append($"\n- `{mp.Type} {mp.Name}`{(string.IsNullOrWhiteSpace(mp.Doc) ? "" : $" — {mp.Doc}")}");
               }
            }
         }
         return new Hover
         {
            Contents = HoverContent(sb.Length > 0 ? sb.ToString() : word),
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

      return null!;
   }

   // ── string builders ───────────────────────────────────────────────────────────

   /// <summary>Strips inline markdown syntax for clients that only understand plain text.</summary>
   private static string StripMarkdown(string markdown)
   {
      var plain = System.Text.RegularExpressions.Regex.Replace(markdown, @"`([^`]*)`", "$1");
      plain = System.Text.RegularExpressions.Regex.Replace(plain, @"\*\*([^*]*)\*\*", "$1");
      plain = System.Text.RegularExpressions.Regex.Replace(plain, @"\*([^*]*)\*", "$1");
      return plain.Replace("\n\n---\n\n", "\n\n");
   }

   /// <summary>
   /// Converts a markdown-formatted string to <see cref="MarkupContent"/> respecting
   /// what the client declared during <c>initialize</c>. VS doesn't declare markdown
   /// support, so we strip inline syntax to plain text instead of showing raw asterisks.
   /// </summary>
   private MarkupContent HoverContent(string markdown)
   {
      if (_clientSupportsMarkdownHover)
         return new MarkupContent { Kind = MarkupKind.Markdown, Value = markdown };
      return new MarkupContent { Kind = MarkupKind.PlainText, Value = StripMarkdown(markdown) };
   }

   /// <summary>
   /// Returns documentation for a completion item, respecting the client's declared
   /// <c>completionItem.documentationFormat</c> capability.
   /// </summary>
   private SumType<string, MarkupContent> CompletionDoc(string markdown)
   {
      var kind  = _clientSupportsMarkdownCompletion ? MarkupKind.Markdown : MarkupKind.PlainText;
      var value = _clientSupportsMarkdownCompletion ? markdown : StripMarkdown(markdown);
      return new SumType<string, MarkupContent>(new MarkupContent { Kind = kind, Value = value });
   }

   /// <summary>
   /// Returns documentation for a signature-help item, respecting the client's declared
   /// <c>signatureHelp.signatureInformation.documentationFormat</c> capability.
   /// </summary>
   private SumType<string, MarkupContent> SignatureDoc(string markdown)
   {
      var kind  = _clientSupportsMarkdownSignature ? MarkupKind.Markdown : MarkupKind.PlainText;
      var value = _clientSupportsMarkdownSignature ? markdown : StripMarkdown(markdown);
      return new SumType<string, MarkupContent>(new MarkupContent { Kind = kind, Value = value });
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

   private SignatureInformation[] BuildSignatureInfoList(string funcName, FunctionDefinition fn)
   {
      if (fn.IsNewStyle && fn.Variants?.Length > 0)
      {
         return fn.Variants.Select(v => new SignatureInformation
         {
            Label         = $"{v.ReturnType} {funcName}({BuildVariantParamList(v.Param)})",
            Documentation = SignatureDoc(v.Doc ?? string.Empty),
            Parameters    = (v.Param ?? []).Select(p =>
               new ParameterInformation
               {
                  Label         = new SumType<string, Tuple<int, int>>($"{p.Type} {p.Name}"),
                  Documentation = SignatureDoc(p.Doc ?? string.Empty),
               }).ToArray(),
         }).ToArray();
      }
      return
      [
         new SignatureInformation
         {
            Label         = $"{fn.ReturnType} {funcName}({BuildParamList(fn.Parameters)})",
            Documentation = SignatureDoc(BuildFunctionHover(funcName, fn)),
            Parameters    = (fn.Parameters ?? []).Select(p =>
               new ParameterInformation
               {
                  Label         = new SumType<string, Tuple<int, int>>($"{p.ConstantType} {p.Name}"),
                  Documentation = SignatureDoc(p.IsOptional ? "(optional)" : string.Empty),
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

   /// <summary>
   /// Builds signature info for a method call on an instance or type: <c>receiver.Method(</c>.
   /// Locates the dot before <paramref name="parenPos"/>, resolves the receiver's declared
   /// type, then looks up matching methods (instance or static) on that type.
   /// Returns <c>null</c> if the type or method cannot be resolved.
   /// </summary>
   private SignatureInformation[]? TryBuildMethodSignatures(
      string methodName, string text, int parenPos, Antlr4.Runtime.Tree.IParseTree? tree)
   {
      // Step back over whitespace to the char before the method name identifier.
      int pos = parenPos;
      while (pos > 0 && text[pos - 1] == ' ') pos--;
      pos -= methodName.Length; // step over the method name itself
      while (pos > 0 && text[pos - 1] == ' ') pos--;

      if (pos == 0 || text[pos - 1] != '.') return null;

      var receiverName = GetIdentifierBefore(text, pos - 1);
      if (receiverName is null) return null;

      ObjectDefinition? objDef = null;
      bool isStatic = false;
      if (_definitions.Objects.TryGetValue(receiverName, out objDef))
         isStatic = true;
      else
      {
         objDef = ResolveReceiverObjectAtDot(text, pos - 1, tree);
      }

      if (objDef is null) return null;

      var methods = (isStatic ? (objDef.StaticMethods ?? []) : (objDef.Methods ?? []))
         .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase))
         .ToArray();

      if (methods.Length == 0) return null;

      return methods.Select(m => new SignatureInformation
      {
         Label         = $"{m.ReturnType} {m.Name}({BuildMethodParamList(m.Param)})",
         Documentation = SignatureDoc(m.Doc ?? string.Empty),
         Parameters    = (m.Param ?? []).Select(p =>
            new ParameterInformation
            {
               Label         = new SumType<string, Tuple<int, int>>($"{p.Type} {p.Name}"),
               Documentation = SignatureDoc(p.Doc ?? string.Empty),
            }).ToArray(),
      }).ToArray();
   }

   /// <summary>
   /// Returns <c>true</c> when the <c>(</c> at <paramref name="parenPos"/> belongs to a
   /// <c>new TypeName(</c> expression, i.e. the keyword immediately preceding the type
   /// name is <c>new</c>.
   /// </summary>
   private static bool IsConstructorCall(string text, int parenPos, string typeName)
   {
      // Skip back over whitespace, then over the type name, then over whitespace.
      int pos = parenPos;
      while (pos > 0 && text[pos - 1] == ' ') pos--;
      pos -= typeName.Length; // step over type name
      while (pos > 0 && text[pos - 1] == ' ') pos--;
      if (pos < 3) return false;
      return text[(pos - 3)..pos] == "new" && (pos == 3 || !IsWordChar(text[pos - 4]));
   }

   /// <summary>Builds one <see cref="SignatureInformation"/> per constructor overload.</summary>
   private SignatureInformation[] BuildConstructorSignatureInfoList(string typeName, MethodDefinition[] ctors)
      => ctors.Select(c => new SignatureInformation
      {
         Label         = $"new {typeName}({BuildMethodParamList(c.Param)})",
         Documentation = SignatureDoc(c.Doc ?? string.Empty),
         Parameters    = (c.Param ?? []).Select(p =>
            new ParameterInformation
            {
               Label         = new SumType<string, Tuple<int, int>>($"{p.Type} {p.Name}"),
               Documentation = SignatureDoc(p.Doc ?? string.Empty),
            }).ToArray(),
      }).ToArray();
}
