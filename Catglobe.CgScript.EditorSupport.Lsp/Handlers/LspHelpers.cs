using Catglobe.CgScript.EditorSupport.Parsing;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using LspDiagnostic = Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic;
using LspRange      = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace Catglobe.CgScript.EditorSupport.Lsp.Handlers;

internal static class LspHelpers
{
   internal static LspDiagnostic DiagnosticToLsp(Parsing.Diagnostic d, string source) =>
      new LspDiagnostic
      {
         Severity = d.Severity == Parsing.DiagnosticSeverity.Error
            ? Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Error
            : d.Severity == Parsing.DiagnosticSeverity.Information
            ? Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Information
            : Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Warning,
         Code    = string.IsNullOrEmpty(d.Code) ? default : new SumType<int, string>(d.Code),
         Message = d.Message,
         Range   = new LspRange
         {
            Start = new Position(d.Line - 1, d.Column),
            End   = new Position(d.Line - 1, d.Column + d.Length),
         },
         Source = source,
      };

   internal static LspDiagnostic MakeInternalErrorDiag(string code, string message, string source) =>
      new LspDiagnostic
      {
         Severity = Microsoft.VisualStudio.LanguageServer.Protocol.DiagnosticSeverity.Error,
         Code     = new SumType<int, string>(code),
         Message  = message,
         Range    = new LspRange { Start = new Position(0, 0), End = new Position(0, 0) },
         Source   = source,
      };

   internal static string ApplyChange(string text, TextDocumentContentChangeEvent change)
   {
      if (change.Range is null) return change.Text;
      int start = GetOffset(text, change.Range.Start.Line, change.Range.Start.Character);
      int end   = GetOffset(text, change.Range.End.Line,   change.Range.End.Character);
      return text[..start] + change.Text + text[end..];
   }

   internal static (SemanticTokensEdit Edit, bool HasChanges) ComputeSemanticEdit(int[] oldData, int[] newData)
   {
      int oldLen = oldData.Length, newLen = newData.Length;

      int prefixLen = 0;
      while (prefixLen < oldLen && prefixLen < newLen && oldData[prefixLen] == newData[prefixLen])
         prefixLen++;
      prefixLen = (prefixLen / 5) * 5;

      int suffixLen = 0;
      while (suffixLen < (oldLen - prefixLen) && suffixLen < (newLen - prefixLen)
             && oldData[oldLen - 1 - suffixLen] == newData[newLen - 1 - suffixLen])
         suffixLen++;
      suffixLen = (suffixLen / 5) * 5;

      int deleteCount = oldLen - prefixLen - suffixLen;
      int insertEnd   = newLen - suffixLen;
      return (
         new SemanticTokensEdit { Start = prefixLen, DeleteCount = deleteCount, Data = newData[prefixLen..insertEnd] },
         deleteCount > 0 || (insertEnd - prefixLen) > 0);
   }

   private static int GetOffset(string text, int line, int character)
   {
      int currentLine = 0, i = 0;
      while (i < text.Length && currentLine < line)
         if (text[i++] == '\n') currentLine++;
      return Math.Min(i + character, text.Length);
   }
}
