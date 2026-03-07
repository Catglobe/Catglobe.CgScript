using Antlr4.Runtime;

namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>
/// Entry point for parsing CgScript source text.
/// </summary>
public static class CgScriptParseService
{
   /// <summary>
   /// Parse a complete CgScript source string and return the parse result.
   /// </summary>
   /// <param name="source">The CgScript source text to parse.</param>
   /// <param name="typeResolver">
   /// Optional type resolver used to validate class-name identifiers in
   /// declarations (e.g. <c>MyClass obj;</c>).
   /// Pass <c>null</c> (the default) for <em>editor / allow-all</em> mode —
   /// any identifier is accepted as a type name. This is appropriate for IDE
   /// tooling that does not have access to the full Catglobe type registry.
   /// </param>
   /// <returns>A <see cref="ParseResult"/> containing the parse tree and any diagnostics.</returns>
   public static ParseResult Parse(string source, ITypeResolver? typeResolver = null)
   {
      var errorListener = new DiagnosticErrorListener();

      // Lexer
      var inputStream = CharStreams.fromString(source);
      var lexer       = new CgScriptLexer(inputStream);
      lexer.RemoveErrorListeners();
      lexer.AddErrorListener(errorListener);

      // Token stream
      var tokenStream = new CommonTokenStream(lexer);

      // Parser
      var parser = new CgScriptParser(tokenStream);
      parser.RemoveErrorListeners();
      parser.AddErrorListener(errorListener);
      parser.SetTypeResolver(typeResolver);

      // Parse starting at the top-level rule
      var tree = parser.program();

      return new ParseResult(tree, errorListener.Diagnostics);
   }
}
