using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Catglobe.CgScript.EditorSupport.Parsing;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using StreamJsonRpc;
using System.Collections.Concurrent;
using System.Linq;
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
            if (TryGetObjectDefinition(receiverName, out exactObj))
               isStaticAccess = true;
            else
            {
               var typeName = ResolveVariableType(receiverName, text, _store.GetParseResult(p.TextDocument.Uri.ToString())?.Tree);
               if (typeName != null) TryGetObjectDefinition(typeName, out exactObj);
            }
         }
         IEnumerable<ObjectDefinition> candidates = exactObj != null
            ? [(ObjectDefinition)exactObj]
            : (IEnumerable<ObjectDefinition>)_definitions.Objects.Values;

         foreach (var candidate in candidates)
         {
            // Properties shown for both instance and static access
            if (candidate.Properties != null && candidate.Properties.TryGetValue(word, out var prop))
               return new Hover
               {
                  Contents = HoverContent(
                     (prop.IsObsolete ? DeprecatedPrefix(prop.ObsoleteDoc) : "")
                     + (string.IsNullOrWhiteSpace(prop.Doc) ? "" : $"{prop.Doc}\n\n")
                     + $"`{prop.ReturnType} {word}`"),
               };

            var methodSource = isStaticAccess ? candidate.StaticMethods : candidate.Methods;
            if (methodSource == null || !methodSource.TryGetValue(word, out var methods) || methods.Length == 0) continue;

            bool allMethodsObsolete = methods.All(m => m.IsObsolete);
            var sb = new System.Text.StringBuilder();
            if (allMethodsObsolete) sb.Append("**⚠ Deprecated**\n\n");
            bool firstEntry = true;
            foreach (var m in methods)
            {
               if (!firstEntry) sb.Append("\n\n---\n\n");
               firstEntry = false;
               if (m.IsObsolete && !allMethodsObsolete) sb.Append(DeprecatedPrefix(m.ObsoleteDoc));
               else if (m.IsObsolete && m.ObsoleteDoc is not null) sb.Append($"{m.ObsoleteDoc}\n\n");
               if (!string.IsNullOrWhiteSpace(m.Doc)) sb.Append($"{m.Doc}\n\n");
               sb.Append($"`{m.ReturnType} {word}({BuildMethodParamList(m.Param ?? [])})`");
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
            bool allCtorsObsolete = obj.Constructors.All(c => c.IsObsolete);
            bool firstCtor = true;
            foreach (var ctor in obj.Constructors)
            {
               if (sb.Length > 0) sb.Append("\n\n---\n\n");
               if (firstCtor && allCtorsObsolete) sb.Append("**⚠ Deprecated**\n\n");
               firstCtor = false;
               if (ctor.IsObsolete && !allCtorsObsolete) sb.Append(DeprecatedPrefix(ctor.ObsoleteDoc));
               else if (ctor.IsObsolete && ctor.ObsoleteDoc is not null) sb.Append($"{ctor.ObsoleteDoc}\n\n");
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

      // ── Global variable ──────────────────────────────────────────────────────────
      if (_definitions.GlobalVariables.TryGetValue(word, out var globalVarDef))
      {
         var sb = new System.Text.StringBuilder();
         sb.Append('`').Append(globalVarDef.TypeName).Append("` ").Append(word);
         if (!string.IsNullOrEmpty(globalVarDef.Doc))
            sb.Append("\n\n").Append(globalVarDef.Doc);
         if (globalVarDef.IsObsolete)
         {
            sb.Append("\n\n⚠ **Deprecated**");
            if (!string.IsNullOrEmpty(globalVarDef.ObsoleteDoc))
               sb.Append(": ").Append(globalVarDef.ObsoleteDoc);
         }
         if (_definitions.Objects.TryGetValue(globalVarDef.TypeName, out var typeDef)
             && !string.IsNullOrEmpty(typeDef.Doc))
            sb.Append("\n\n---\n\n").Append(typeDef.Doc);
         return new Hover { Contents = HoverContent(sb.ToString()) };
      }

      // ── Built-in constant ──────────────────────────────────────────────────────
      if (_definitions.ConstantsSet.Contains(word))
      {
         return new Hover
         {
            Contents = HoverContent(BuildEnumConstantDoc(word)),
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
            var allSymbols = DocumentSymbolCollector.CollectAll(result.Tree);
            var exactSym   = allSymbols.FirstOrDefault(
               s => s.Name == word && s.NameLine == decl.Line && s.NameColumn == decl.Column);
            var typeLabel  = exactSym?.TypeName ?? ResolveVariableType(word, text, result.Tree) ?? "?";
            return new Hover
            {
               Contents = HoverContent($"{typeLabel} {word}"),
            };
         }
      }

      return null!;
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

   private static string BuildMethodParamList(MethodParam[]? parameters)
      => parameters is null ? string.Empty
         : string.Join(", ", parameters.Select(p => $"{p.Type} {p.Name}"));

   /// <summary>
   /// Returns the markdown prefix for a deprecated item.
   /// Includes the optional <paramref name="obsoleteDoc"/> as an explanatory paragraph when provided.
   /// </summary>
   private static string DeprecatedPrefix(string? obsoleteDoc)
      => "**⚠ Deprecated**" + (obsoleteDoc is null ? "" : $"\n\n{obsoleteDoc}") + "\n\n";

   private static string GetFunctionReturnType(MethodOverload[] fn)
      => fn.Length > 0 ? fn[0].ReturnType : string.Empty;

   private static string BuildFunctionLabel(string name, MethodOverload[] fn)
   {
      if (fn.Length > 0)
      {
         var first = fn[0];
         return fn.Length == 1
            ? $"{name}({BuildMethodParamList(first.Param)})"
            : $"{name}(+{fn.Length} overloads)";
      }
      return $"{name}()";
   }

   /// <summary>Builds the markdown hover text for a built-in function.</summary>
   private static string BuildFunctionHover(string name, MethodOverload[] fn)
   {
      if (fn.Length == 0) return name;
      bool allObsolete = fn.All(v => v.IsObsolete);
      var sb = new System.Text.StringBuilder();
      if (allObsolete) sb.Append("**⚠ Deprecated**\n\n");
      bool firstEntry = true;
      foreach (var v in fn)
      {
         if (!firstEntry) sb.Append("\n\n---\n\n");
         firstEntry = false;
         if (v.IsObsolete && !allObsolete) sb.Append(DeprecatedPrefix(v.ObsoleteDoc));
         else if (v.IsObsolete && v.ObsoleteDoc is not null) sb.Append($"{v.ObsoleteDoc}\n\n");
         if (!string.IsNullOrWhiteSpace(v.Doc)) sb.Append($"{v.Doc}\n\n");
         sb.Append($"`{v.ReturnType} {name}({BuildMethodParamList(v.Param)})`");
         if (v.Param?.Length > 0)
         {
            sb.Append("\n\n**Parameters:**");
            foreach (var p in v.Param)
               sb.Append($"\n- `{p.Type} {p.Name}` — {p.Doc}");
         }
      }
      return sb.ToString();
   }

   private static SignatureInformation[] BuildSignatureInfoList(string funcName, MethodOverload[] fn)
   {
      if (fn.Length == 0) return [];
      return fn.Select(v => new SignatureInformation
      {
         Label         = $"{v.ReturnType} {funcName}({BuildMethodParamList(v.Param)})",
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

   private static string BuildFunctionDoc(MethodOverload[] fn)
      => fn.Length > 0
         ? string.Join("\n\n---\n\n", fn.Select(v => v.Doc ?? string.Empty))
         : string.Empty;

   /// <summary>
   /// Builds the markdown hover text for an enum-derived constant.
   /// </summary>
   private string BuildEnumConstantDoc(string name, bool includeDeprecatedPrefix = true)
   {
      if (!_definitions.EnumByConstant.TryGetValue(name, out var entry))
         return $"constant: {name}";

      var sb = new System.Text.StringBuilder();
      if (entry.Value.IsObsolete && includeDeprecatedPrefix) sb.Append(DeprecatedPrefix(entry.Value.ObsoleteDoc));
      if (!string.IsNullOrWhiteSpace(entry.Enum.Doc))
         sb.Append(entry.Enum.Doc);
      if (!string.IsNullOrWhiteSpace(entry.Value.Doc))
      {
         if (sb.Length > 0) sb.Append("\n\n");
         sb.Append(entry.Value.Doc);
      }
      if (sb.Length > 0) sb.Append("\n\n");
      sb.Append($"`{name}` = `{entry.Value.Value}`");
      return sb.ToString();
   }

   /// <summary>
   /// Builds signature info for a method call on an instance or type: <c>receiver.Method(</c>.
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
      if (TryGetObjectDefinition(receiverName, out objDef))
         isStatic = true;
      else
      {
         objDef = ResolveReceiverObjectAtDot(text, pos - 1, tree);
      }

      if (objDef is null) return null;

      var methodSource = isStatic ? objDef.StaticMethods : objDef.Methods;
      if (methodSource == null || !methodSource.TryGetValue(methodName, out var methods) || methods.Length == 0) return null;

      return methods.Select(m => new SignatureInformation
      {
         Label         = $"{m.ReturnType} {methodName}({BuildMethodParamList(m.Param)})",
         Documentation = new SumType<string, MarkupContent>(
            new MarkupContent { Kind = MarkupKind.Markdown, Value = m.Doc ?? string.Empty }),
         Parameters    = (m.Param ?? []).Select(p =>
            new ParameterInformation
            {
               Label         = new SumType<string, Tuple<int, int>>($"{p.Type} {p.Name}"),
               Documentation = new SumType<string, MarkupContent>(p.Doc ?? string.Empty),
            }).ToArray(),
      }).ToArray();
   }

   /// <summary>
   /// Returns <c>true</c> when the <c>(</c> at <paramref name="parenPos"/> belongs to a
   /// <c>new TypeName(</c> expression.
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
   private static SignatureInformation[] BuildConstructorSignatureInfoList(string typeName, MethodOverload[] ctors)
      => ctors.Select(c => new SignatureInformation
      {
         Label         = $"new {typeName}({BuildMethodParamList(c.Param)})",
         Documentation = new SumType<string, MarkupContent>(
            new MarkupContent
            {
               Kind  = MarkupKind.Markdown,
               Value = (c.IsObsolete ? DeprecatedPrefix(c.ObsoleteDoc) : "") + (c.Doc ?? string.Empty),
            }),
         Parameters    = (c.Param ?? []).Select(p =>
            new ParameterInformation
            {
               Label         = new SumType<string, Tuple<int, int>>($"{p.Type} {p.Name}"),
               Documentation = new SumType<string, MarkupContent>(p.Doc ?? string.Empty),
            }).ToArray(),
      }).ToArray();
}
