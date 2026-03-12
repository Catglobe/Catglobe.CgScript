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
   // ── signature help ────────────────────────────────────────────────────────────

   /// <summary>
   /// Provides active signature information by scanning backwards from the cursor
   /// to find the enclosing function call and the current argument index.
   /// </summary>
   public SignatureHelp? OnSignatureHelp(SignatureHelpParams p)
   {
      var text = _store.GetText(p.TextDocument.Uri.ToString());
      if (text is null) return null;

      var offset = GetOffset(text, p.Position.Line, p.Position.Character);

      // Scan backwards to find the opening paren of the current call.
      int depth      = 0;
      int commaCount = 0;

      for (int i = offset - 1; i >= 0; i--)
      {
         char c = text[i];

         if      (c == ')')                { depth++; }
         else if (c == '(' && depth > 0)   { depth--; }
         else if (c == '(' && depth == 0)
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
            else if (_definitions.Objects.TryGetValue(funcName, out var obj)
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
         else if (c == ',' && depth == 0)
         {
            commaCount++;
         }
      }

      return null;
   }
}
