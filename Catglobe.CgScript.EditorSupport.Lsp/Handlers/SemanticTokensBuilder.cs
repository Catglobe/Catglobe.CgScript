using Antlr4.Runtime;
using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
using Catglobe.CgScript.EditorSupport.Parsing;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Catglobe.CgScript.EditorSupport.Lsp.Handlers;

/// <summary>
/// Encodes ANTLR lexer tokens into the LSP semantic tokens integer array format.
/// Provides rich, context-sensitive classification for identifiers by examining
/// the surrounding default-channel token neighbours and the known definition sets.
/// </summary>
public static class SemanticTokensBuilder
{
   // ── Token type indices (position in legend must match these constants) ────────
   private const int TypeKeyword    = 0;
   private const int TypeString     = 1;
   private const int TypeNumber     = 2;
   private const int TypeComment    = 3;
   private const int TypeOperator   = 4;
   private const int TypeVariable   = 5;
   private const int TypeFunction   = 6;
   private const int TypeClass      = 7;
   private const int TypeEnumMember = 8;
   private const int TypeParameter  = 9;
   private const int TypeMethod     = 10;
   private const int TypeProperty   = 11;
   private const int TypeMacro      = 12;

   // ── Token modifier bit positions ──────────────────────────────────────────────
   private const int ModDeclaration    = 1 << 0; // bit 0
   private const int ModDefaultLibrary = 1 << 1; // bit 1

   /// <summary>Standard LSP token type names (position == type index sent in the legend).</summary>
   public static readonly string[] TokenTypes =
   [
      SemanticTokenTypes.Keyword,    // 0
      SemanticTokenTypes.String,     // 1
      SemanticTokenTypes.Number,     // 2
      SemanticTokenTypes.Comment,    // 3
      SemanticTokenTypes.Operator,   // 4
      SemanticTokenTypes.Variable,   // 5
      SemanticTokenTypes.Function,   // 6
      SemanticTokenTypes.Class,      // 7
      SemanticTokenTypes.EnumMember, // 8
      SemanticTokenTypes.Parameter,  // 9
      SemanticTokenTypes.Method,     // 10
      SemanticTokenTypes.Property,   // 11
      SemanticTokenTypes.Macro,      // 12
   ];

   /// <summary>Token modifier names (bit position == index in this array).</summary>
   public static readonly string[] TokenModifiers =
   [
      SemanticTokenModifiers.Declaration,    // bit 0
      SemanticTokenModifiers.DefaultLibrary, // bit 1
   ];

   // ── Internal raw token record ─────────────────────────────────────────────────
   private readonly record struct RawToken(int Line, int Col, int Length, int TypeIdx, int Modifier);

   // ── Public API ────────────────────────────────────────────────────────────────

   /// <summary>
   /// Builds a full set of semantic tokens for <paramref name="text"/>.
   /// </summary>
   public static SemanticTokens Build(
      string text,
      IReadOnlyDictionary<string, FunctionDefinition>? knownFunctions  = null,
      IReadOnlyDictionary<string, ObjectDefinition>?   knownObjects    = null,
      IReadOnlyCollection<string>?                     knownConstants  = null,
      IReadOnlyDictionary<string, string>?             globalVariables = null)
   {
      var raw = BuildRawTokens(text, knownFunctions, knownObjects, knownConstants, globalVariables);
      return new SemanticTokens { Data = EncodeTokens(raw) };
   }

   /// <summary>
   /// Builds a range-filtered set of semantic tokens.
   /// Only tokens whose 0-based line falls within [<paramref name="startLine0"/>,
   /// <paramref name="endLine0"/>] are included; the relative delta encoding is
   /// recomputed from the first included token.
   /// </summary>
   public static SemanticTokens BuildRange(
      string text,
      int    startLine0,
      int    endLine0,
      IReadOnlyDictionary<string, FunctionDefinition>? knownFunctions  = null,
      IReadOnlyDictionary<string, ObjectDefinition>?   knownObjects    = null,
      IReadOnlyCollection<string>?                     knownConstants  = null,
      IReadOnlyDictionary<string, string>?             globalVariables = null)
   {
      var filtered = BuildRawTokens(text, knownFunctions, knownObjects, knownConstants, globalVariables)
                        .Where(t => t.Line >= startLine0 && t.Line <= endLine0);
      return new SemanticTokens { Data = EncodeTokens(filtered) };
   }

   // ── Core implementation ───────────────────────────────────────────────────────

   private static List<RawToken> BuildRawTokens(
      string                                           text,
      IReadOnlyDictionary<string, FunctionDefinition>? knownFunctions,
      IReadOnlyDictionary<string, ObjectDefinition>?   knownObjects,
      IReadOnlyCollection<string>?                     knownConstants,
      IReadOnlyDictionary<string, string>?             globalVariables)
   {
      // Materialise constants into a set for O(1) Contains.
      IReadOnlySet<string>? constantsSet =
         knownConstants is IReadOnlySet<string> s ? s
         : knownConstants is not null              ? new HashSet<string>(knownConstants)
                                                   : null;

      // Strip preprocessor directives before lexing so ANTLR doesn't see bare '#'.
      var (cleanedText, preprocDirectives) = PreprocessorScanner.Strip(text);

      var lexer  = new CgScriptLexer(CharStreams.fromString(cleanedText));
      var stream = new CommonTokenStream(lexer);
      stream.Fill();

      var allTokens = stream.GetTokens();

      // ── Step 1: collect default-channel tokens and build an index map ──────────
      // Default channel == 0; hidden channel (WS, comments) == 1.
      var defaultTokens    = new List<IToken>(allTokens.Count);
      var tokenToDefaultIdx = new Dictionary<int, int>(allTokens.Count);

      foreach (var t in allTokens)
      {
         if (t.Type == TokenConstants.EOF) break;
         if (t.Channel == 0) // Lexer.DefaultChannel
         {
            tokenToDefaultIdx[t.TokenIndex] = defaultTokens.Count;
            defaultTokens.Add(t);
         }
      }

      int dCount = defaultTokens.Count;

      // ── Step 2: pre-compute which IDENTIFIER indices are function parameters ───
      // Scan for FUNCTION LPAREN … RPAREN sequences and mark the declared
      // parameter identifiers (those preceded by a type-spec token).
      var paramIndices = new HashSet<int>(); // indices in defaultTokens

      for (int j = 0; j < dCount; j++)
      {
         if (defaultTokens[j].Type != CgScriptLexer.FUNCTION) continue;
         if (j + 1 >= dCount || defaultTokens[j + 1].Type != CgScriptLexer.LPAREN) continue;

         // Walk forward to find the matching RPAREN (depth tracking).
         int depth = 0;
         int end   = j + 1;
         while (end < dCount)
         {
            if      (defaultTokens[end].Type == CgScriptLexer.LPAREN) depth++;
            else if (defaultTokens[end].Type == CgScriptLexer.RPAREN) { depth--; if (depth == 0) break; }
            end++;
         }

         // Any IDENTIFIER preceded by a type-spec token inside the param list
         // is a formal parameter.
         for (int k = j + 2; k < end; k++)
         {
            if (defaultTokens[k].Type == CgScriptLexer.IDENTIFIER
                && IsTypeSpecToken(defaultTokens[k - 1].Type))
            {
               paramIndices.Add(k);
            }
         }
      }

      // ── Step 3: emit one RawToken per visible ANTLR token ────────────────────
      var result = new List<RawToken>(allTokens.Count);

      foreach (var token in allTokens)
      {
         if (token.Type == TokenConstants.EOF) break;

         int line   = token.Line - 1; // LSP is 0-based
         int col    = token.Column;
         int length = token.StopIndex - token.StartIndex + 1;

         if (token.Type == CgScriptLexer.IDENTIFIER)
         {
            if (!tokenToDefaultIdx.TryGetValue(token.TokenIndex, out int di))
               continue; // safety: IDENTIFIER should always be on default channel

            var (typeIdx, modifier) = ClassifyIdentifier(
               token, di, defaultTokens, paramIndices,
               knownFunctions, knownObjects, constantsSet, globalVariables);

            result.Add(new RawToken(line, col, length, typeIdx, modifier));
         }
         else
         {
            int typeIdx = GetSimpleTypeIndex(token.Type);
            if (typeIdx < 0) continue;
            result.Add(new RawToken(line, col, length, typeIdx, 0));
         }
      }

      // Add a macro-type token for each preprocessor directive line.
      foreach (var (line0, col, length) in preprocDirectives)
         result.Add(new RawToken(line0, col, length, TypeMacro, 0));

      // Ensure tokens are sorted by line then column for correct delta encoding.
      result.Sort((a, b) => a.Line != b.Line ? a.Line - b.Line : a.Col - b.Col);

      return result;
   }

   /// <summary>
   /// Classifies an IDENTIFIER tokenusing the 9-rule priority chain described in
   /// the LSP capability specification.
   /// </summary>
   private static (int TypeIdx, int Modifier) ClassifyIdentifier(
      IToken                                           token,
      int                                              di,
      List<IToken>                                     defaultTokens,
      HashSet<int>                                     paramIndices,
      IReadOnlyDictionary<string, FunctionDefinition>? knownFunctions,
      IReadOnlyDictionary<string, ObjectDefinition>?   knownObjects,
      IReadOnlySet<string>?                            constantsSet,
      IReadOnlyDictionary<string, string>?             globalVariables)
   {
      int dCount  = defaultTokens.Count;
      string name = token.Text;

      int prevType = di > 0          ? defaultTokens[di - 1].Type : -1;
      int nextType = di + 1 < dCount ? defaultTokens[di + 1].Type : -1;

      // Rule 1: obj.method(…)  — preceded by DOT and followed by LPAREN
      if (prevType == CgScriptLexer.DOT && nextType == CgScriptLexer.LPAREN)
         return (TypeMethod, 0);

      // Rule 2: obj.property  — preceded by DOT (no LPAREN after)
      if (prevType == CgScriptLexer.DOT)
         return (TypeProperty, 0);

      // Rule 3: new ClassName(…)  — preceded by NEW
      if (prevType == CgScriptLexer.NEW)
      {
         int mod = knownObjects is not null && knownObjects.ContainsKey(name) ? ModDefaultLibrary : 0;
         return (TypeClass, mod);
      }

      // Rule 4: func(…)  — followed by LPAREN
      if (nextType == CgScriptLexer.LPAREN)
      {
         int mod = knownFunctions is not null && knownFunctions.ContainsKey(name) ? ModDefaultLibrary : 0;
         return (TypeFunction, mod);
      }

      // Rule 4b: TypeName varName  — IDENTIFIER followed by IDENTIFIER is a type reference
      if (nextType == CgScriptLexer.IDENTIFIER)
      {
         int mod = knownObjects is not null && knownObjects.ContainsKey(name) ? ModDefaultLibrary : 0;
         return (TypeClass, mod);
      }

      // Rule 5: typeSpec IDENTIFIER  — declaration site
      if (IsTypeSpecToken(prevType))
      {
         int typeIdx = paramIndices.Contains(di) ? TypeParameter : TypeVariable;
         return (typeIdx, ModDeclaration);
      }

      // Rule 6: name is a known built-in function
      if (knownFunctions is not null && knownFunctions.ContainsKey(name))
         return (TypeFunction, ModDefaultLibrary);

      // Rule 7: name is a known built-in object type
      if (knownObjects is not null && knownObjects.ContainsKey(name))
         return (TypeClass, ModDefaultLibrary);

      // Rule 8: name is a known constant
      if (constantsSet is not null && constantsSet.Contains(name))
         return (TypeEnumMember, ModDefaultLibrary);

      // Rule 8b: name is a known global variable pre-declared by the runtime
      if (globalVariables is not null && globalVariables.ContainsKey(name))
         return (TypeVariable, ModDefaultLibrary);

      // Rule 9: unresolved identifier — treat as variable reference
      return (TypeVariable, 0);
   }

   /// <summary>
   /// Returns <see langword="true"/> when <paramref name="type"/> is one of the
   /// type-spec tokens that may appear immediately before a declared identifier.
   /// Includes IDENTIFIER itself to handle <c>ClassName varName</c> patterns.
   /// </summary>
   private static bool IsTypeSpecToken(int type) =>
      type == CgScriptLexer.BOOL
      || type == CgScriptLexer.NUMBER
      || type == CgScriptLexer.STRING
      || type == CgScriptLexer.ARRAY
      || type == CgScriptLexer.OBJECT
      || type == CgScriptLexer.QUESTION
      || type == CgScriptLexer.FUNCTION
      || type == CgScriptLexer.IDENTIFIER;

   /// <summary>
   /// Encodes a sequence of <see cref="RawToken"/> values into the LSP
   /// relative-delta integer array format (5 ints per token).
   /// </summary>
   private static int[] EncodeTokens(IEnumerable<RawToken> tokens)
   {
      var data         = new List<int>();
      int prevLine     = 0;
      int prevStartChar = 0;

      foreach (var t in tokens)
      {
         int deltaLine      = t.Line - prevLine;
         int deltaStartChar = deltaLine == 0 ? t.Col - prevStartChar : t.Col;

         data.Add(deltaLine);
         data.Add(deltaStartChar);
         data.Add(t.Length);
         data.Add(t.TypeIdx);
         data.Add(t.Modifier);

         prevLine      = t.Line;
         prevStartChar = t.Col;
      }

      return data.ToArray();
   }

   // ── Non-contextual (fixed) token type mapping ─────────────────────────────────

   private static int GetSimpleTypeIndex(int t) => t switch
   {
      // Primitive type keywords — coloured as class types (they are classes in the runtime)
      CgScriptLexer.BOOL or CgScriptLexer.NUMBER or CgScriptLexer.STRING
      or CgScriptLexer.ARRAY or CgScriptLexer.OBJECT or CgScriptLexer.QUESTION => TypeClass,

      // Control-flow and other keywords
      CgScriptLexer.IF or CgScriptLexer.ELSE or CgScriptLexer.WHILE or CgScriptLexer.FOR
      or CgScriptLexer.BREAK or CgScriptLexer.CONTINUE or CgScriptLexer.RETURN
      or CgScriptLexer.WHERE or CgScriptLexer.NEW
      or CgScriptLexer.SWITCH or CgScriptLexer.DEFAULT or CgScriptLexer.CASE
      or CgScriptLexer.TRY or CgScriptLexer.CATCH or CgScriptLexer.THROW
      or CgScriptLexer.FUNCTION or CgScriptLexer.TRUE or CgScriptLexer.FALSE
      or CgScriptLexer.EMPTY => TypeKeyword,

      // String / char literals
      CgScriptLexer.STRING_LITERAL or CgScriptLexer.CHAR_LITERAL => TypeString,

      // Numeric / date literals
      CgScriptLexer.NUM_INT or CgScriptLexer.NUM_DOUBLE
      or CgScriptLexer.DATE_LITERAL => TypeNumber,

      // Comments — hidden channel, but still emitted for colourisation
      CgScriptLexer.SL_COMMENT or CgScriptLexer.ML_COMMENT => TypeComment,

      // Operators
      CgScriptLexer.ASSIGN or CgScriptLexer.PLUS or CgScriptLexer.MINUS
      or CgScriptLexer.STAR or CgScriptLexer.DIV or CgScriptLexer.MOD
      or CgScriptLexer.POW or CgScriptLexer.EQUALS or CgScriptLexer.NOT_EQUALS
      or CgScriptLexer.LTHAN or CgScriptLexer.LTEQ or CgScriptLexer.GTHAN
      or CgScriptLexer.GTEQ or CgScriptLexer.AND or CgScriptLexer.OR
      or CgScriptLexer.NOT or CgScriptLexer.INC or CgScriptLexer.DEC
      or CgScriptLexer.QMARK => TypeOperator,

      // IDENTIFIER is handled separately — see ClassifyIdentifier
      _ => -1,
   };
}
