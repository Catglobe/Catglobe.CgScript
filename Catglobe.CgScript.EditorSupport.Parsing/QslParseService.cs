using Antlr4.Runtime;

namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>
/// Entry point for parsing QSL (Questionnaire Script Language) source text.
/// </summary>
public static class QslParseService
{
   /// <summary>
   /// Parse a complete QSL source string and return the parse result.
   /// </summary>
   /// <param name="source">The QSL source text to parse.</param>
   /// <returns>A <see cref="ParseResult"/> containing the parse tree and any diagnostics.</returns>
   public static ParseResult Parse(string source)
   {
      var preprocessed  = QslPreprocessor.Clean(source);
      var errorListener = new DiagnosticErrorListener("QSL001");

      // Lexer
      var inputStream = CharStreams.fromString(preprocessed);
      var lexer       = new QslLexer(inputStream);
      lexer.RemoveErrorListeners();
      lexer.AddErrorListener(errorListener);

      // Token stream
      var tokenStream = new CommonTokenStream(lexer);

      // Parser
      var parser = new QslParser(tokenStream);
      parser.RemoveErrorListeners();
      parser.AddErrorListener(errorListener);

      // Parse starting at the top-level rule
      var tree = parser.root();

      return new ParseResult(tree, errorListener.Diagnostics);
   }

   /// <summary>
   /// Parse a QSL source string and run semantic analysis in one call.
   /// </summary>
   public static (ParseResult Parse, QslAnalysis Analysis) ParseAndAnalyze(string source)
   {
      var parseResult = Parse(source);
      QslAnalysis analysis;
      try
      {
         analysis = parseResult.Tree is QslParser.RootContext root
            ? QslSemanticAnalyzer.Analyze(root)
            : QslAnalysis.Empty;
      }
      catch
      {
         analysis = QslAnalysis.Empty;
      }
      return (parseResult, analysis);
   }
}
