using Antlr4.Runtime;
using System.Collections.Generic;
using System.IO;

namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>
/// Collects ANTLR4 lexer/parser errors into <see cref="Diagnostic"/> objects.
/// </summary>
internal sealed class DiagnosticErrorListener : BaseErrorListener, IAntlrErrorListener<int>
{
   private readonly List<Diagnostic> _diagnostics = new();
   private readonly string _syntaxErrorCode;

   /// <param name="syntaxErrorCode">Diagnostic code used for parser/lexer syntax errors.</param>
   public DiagnosticErrorListener(string syntaxErrorCode)
   {
      _syntaxErrorCode = syntaxErrorCode;
   }

   public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

   // ── Parser error listener ─────────────────────────────────────────────────
   public override void SyntaxError(
      TextWriter      output,
      IRecognizer     recognizer,
      IToken?         offendingSymbol,
      int             line,
      int             charPositionInLine,
      string          msg,
      RecognitionException? e)
   {
      var length = offendingSymbol is { } t
         ? t.StopIndex - t.StartIndex + 1
         : 0;
      _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, msg, line, charPositionInLine, length, _syntaxErrorCode));
   }

   // ── Lexer error listener ──────────────────────────────────────────────────
   void IAntlrErrorListener<int>.SyntaxError(
      TextWriter      output,
      IRecognizer     recognizer,
      int             offendingSymbol,
      int             line,
      int             charPositionInLine,
      string          msg,
      RecognitionException? e)
   {
      _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Error, msg, line, charPositionInLine, 1, _syntaxErrorCode));
   }
}
