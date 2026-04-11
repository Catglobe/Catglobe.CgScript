using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
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
   // ── signature help ────────────────────────────────────────────────────────────

   /// <summary>
   /// Provides active signature information by scanning backwards from the cursor
   /// to find the enclosing function call or indexer and the current argument index.
   /// </summary>
   public SignatureHelp? OnSignatureHelp(SignatureHelpParams p)
   {
      try { return OnSignatureHelpCore(p); }
      catch (Exception ex)
      {
         System.Diagnostics.Debug.WriteLine($"[CgScript LSP] SignatureHelp error: {ex}");
         return null;
      }
   }

   private SignatureHelp? OnSignatureHelpCore(SignatureHelpParams p)
   {
      var text = _store.GetText(p.TextDocument.Uri.ToString());
      if (text is null) return null;

      var offset = GetOffset(text, p.Position.Line, p.Position.Character);

      // Scan backwards to find the opening paren/bracket of the current call/indexer.
      int parenDepth   = 0;
      int bracketDepth = 0;
      int commaCount   = 0;

      for (int i = offset - 1; i >= 0; i--)
      {
         char c = text[i];

         if      (c == ')')                               { parenDepth++;   }
         else if (c == '(' && parenDepth > 0)             { parenDepth--;   }
         else if (c == ']')                               { bracketDepth++; }
         else if (c == '[' && bracketDepth > 0)           { bracketDepth--; }
         else if (c == '(' && parenDepth == 0 && bracketDepth == 0)
         {
            // Found the call-opening paren. The identifier immediately to its
            // left is the function name (or constructor type name).
            var funcName = GetIdentifierBefore(text, i);
            if (funcName is null) return null;

            SignatureInformation[]? signatures = null;

            if (_definitions.Functions.TryGetValue(funcName, out var fn))
            {
               signatures = BuildSignatureInfoList(funcName, fn);
            }
            else if (TryGetObjectDefinition(funcName, out var obj)
                     && obj.Constructors?.Length > 0
                     && IsConstructorCall(text, i, funcName))
            {
               signatures = BuildConstructorSignatureInfoList(funcName, obj.Constructors);
            }
            else
            {
               // Method call on a variable: receiver.Method(  — find the dot and
               // resolve the receiver's declared type, then look up the method.
               signatures = TryBuildMethodSignatures(funcName, text, i,
                  _store.GetParseResult(p.TextDocument.Uri.ToString())?.Tree);
            }

            if (signatures is null) return null;

            return new SignatureHelp
            {
               Signatures      = signatures,
               ActiveSignature = 0,
               ActiveParameter = commaCount,
            };
         }
         else if (c == '[' && parenDepth == 0 && bracketDepth == 0)
         {
            // Found an indexer-opening bracket.  Resolve the receiver type and look
            // up its "[]" methods to show parameter info for the index argument(s).
            var signatures = TryBuildIndexerSignatures(text, i,
               _store.GetParseResult(p.TextDocument.Uri.ToString())?.Tree);
            if (signatures is null) return null;

            return new SignatureHelp
            {
               Signatures      = signatures,
               ActiveSignature = 0,
               ActiveParameter = commaCount,
            };
         }
         else if (c == ',' && parenDepth == 0 && bracketDepth == 0)
         {
            commaCount++;
         }
      }

      return null;
   }

   /// <summary>
   /// Builds signature info for an indexer expression <c>receiver[</c>.
   /// Resolves the receiver variable (or chained property) to its declared type, then
   /// returns signature information for all <c>[]</c> methods defined on that type.
   /// Returns <c>null</c> when the type or its indexer cannot be resolved.
   /// </summary>
   private SignatureInformation[]? TryBuildIndexerSignatures(
      string text, int bracketPos, IParseTree? tree)
   {
      var receiverName = GetIdentifierBefore(text, bracketPos);
      if (receiverName is null) return null;

      ObjectDefinition? objDef = null;

      // Direct type-name access (rare for indexers, but handle it)
      if (!TryGetObjectDefinition(receiverName, out objDef))
      {
         // Local/global variable
         var typeName = ResolveVariableType(receiverName, text, tree);
         if (typeName != null)
            TryGetObjectDefinition(typeName, out objDef);
      }

      // Chained property access: someObj.someProperty[
      if (objDef is null)
      {
         int pos = bracketPos;
         while (pos > 0 && text[pos - 1] == ' ') pos--;
         pos -= receiverName.Length;
         while (pos > 0 && text[pos - 1] == ' ') pos--;

         if (pos > 0 && text[pos - 1] == '.')
         {
            var receiverObj = ResolveReceiverObjectAtDot(text, pos - 1, tree);
            if (receiverObj != null)
            {
               if (receiverObj.Properties != null
                   && receiverObj.Properties.TryGetValue(receiverName, out var propDef)
                   && propDef?.ReturnType != null)
                  TryGetObjectDefinition(propDef.ReturnType, out objDef);
            }
         }
      }

      if (objDef is null) return null;

      if (objDef.Methods == null || !objDef.Methods.TryGetValue("[]", out var indexerMethods) || indexerMethods.Length == 0)
         return null;

      return indexerMethods.Select(m => new SignatureInformation
      {
         Label         = $"{m.ReturnType} this[{BuildMethodParamList(m.Param)}]",
         Documentation = new SumType<string, MarkupContent>(
            new MarkupContent { Kind = MarkupKind.Markdown, Value = m.Doc ?? string.Empty }),
         Parameters    = (m.Param ?? []).Select(mp =>
            new ParameterInformation
            {
               Label         = new SumType<string, Tuple<int, int>>($"{mp.Type} {mp.Name}"),
               Documentation = new SumType<string, MarkupContent>(mp.Doc ?? string.Empty),
            }).ToArray(),
      }).ToArray();
   }
}
