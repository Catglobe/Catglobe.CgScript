using Catglobe.CgScript.EditorSupport.Lsp.Handlers;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

/// <summary>
/// Verifies that <see cref="SemanticTokensBuilder"/> handles preprocessor
/// directives without crashing and emits the correct macro-typed tokens.
/// </summary>
public class SemanticTokensBuilderTests
{
   // TypeMacro is index 12 in the legend (matches SemanticTokensBuilder.TokenTypes).
   private const int TypeMacro = 12;

   // ── Legend ────────────────────────────────────────────────────────────────

   [Fact]
   public void TokenTypes_ContainsMacroEntry()
   {
      Assert.Contains("macro", SemanticTokensBuilder.TokenTypes);
   }

   [Fact]
   public void TokenTypes_MacroIsAtIndex12()
   {
      Assert.Equal("macro", SemanticTokensBuilder.TokenTypes[TypeMacro]);
   }

   // ── No crash on preprocessor input ───────────────────────────────────────

   [Fact]
   public void Build_WithIfDirective_DoesNotThrow()
   {
      var result = SemanticTokensBuilder.Build("#IF Development\nnumber x = 1;\n#ENDIF");
      Assert.NotNull(result.Data);
   }

   [Fact]
   public void Build_WithAllEnvironments_DoesNotThrow()
   {
      const string src =
         "#IF Development\nnumber a = 1;\n#ENDIF\n" +
         "#IF Production\nnumber b = 2;\n#ENDIF\n" +
         "#IF Staging\nnumber c = 3;\n#ENDIF";

      var result = SemanticTokensBuilder.Build(src);
      Assert.NotNull(result.Data);
   }

   // ── Macro tokens emitted ──────────────────────────────────────────────────

   [Fact]
   public void Build_WithIfAndEndif_EmitsTwoMacroTokens()
   {
      const string src = "#IF Development\nnumber x = 1;\n#ENDIF";
      var data = SemanticTokensBuilder.Build(src).Data;

      Assert.Equal(2, CountTokensOfType(data, TypeMacro));
   }

   [Fact]
   public void Build_TwoBlocks_EmitsFourMacroTokens()
   {
      const string src =
         "#IF Development\nnumber a = 1;\n#ENDIF\n" +
         "#IF Production\nnumber b = 2;\n#ENDIF";

      var data = SemanticTokensBuilder.Build(src).Data;

      Assert.Equal(4, CountTokensOfType(data, TypeMacro));
   }

   [Fact]
   public void Build_WithNoDirectives_EmitsNoMacroTokens()
   {
      const string src = "number x = 1;\nreturn x;";
      var data = SemanticTokensBuilder.Build(src).Data;

      Assert.Equal(0, CountTokensOfType(data, TypeMacro));
   }

   // ── Token ordering ────────────────────────────────────────────────────────

   [Fact]
   public void Build_TokensAreSortedByLine()
   {
      // #IF is on line 0, a keyword token ('number') is on line 1, #ENDIF on line 2.
      const string src = "#IF Development\nnumber x = 1;\n#ENDIF";
      var data = SemanticTokensBuilder.Build(src).Data;

      // Reconstruct absolute lines from delta encoding.
      var lines = AbsoluteLines(data);
      for (int i = 1; i < lines.Count; i++)
         Assert.True(lines[i] >= lines[i - 1],
            $"Token at index {i} (line {lines[i]}) is before token at index {i - 1} (line {lines[i - 1]}).");
   }

   // ── Inner code still highlighted ──────────────────────────────────────────

   [Fact]
   public void Build_ContentInsideBlock_StillHighlighted()
   {
      // The 'number' type keyword on line 1 should produce a class token (primitives are classes).
      const int TypeClass = 7;
      const string src = "#IF Development\nnumber x = 1;\n#ENDIF";
      var data = SemanticTokensBuilder.Build(src).Data;

      Assert.True(CountTokensOfType(data, TypeClass) > 0,
         "Expected at least one class token for the content inside the #IF block.");
   }

   // ── Comments inside blocks ────────────────────────────────────────────────

   [Fact]
   public void Build_CommentsInsideBlock_AreHighlighted()
   {
      const int TypeComment = 3;
      const string src = "number a = 1;\n{\n// comment\n}";
      var data = SemanticTokensBuilder.Build(src).Data;

      Assert.True(CountTokensOfType(data, TypeComment) > 0,
         "Expected comment tokens to be highlighted inside a block.");
   }

   // ── Multi-line token splitting (issues #111, #113) ────────────────────────

   [Fact]
   public void Build_MultiLineStringLiteral_EmitsOneTokenPerLine()
   {
      // Issue #113: multi-line string should colour each line, not just the first.
      const int TypeString = 1;
      const string src = "string s = \"hello\nworld\";";
      var data = SemanticTokensBuilder.Build(src).Data;

      // Expect two string tokens — one for each line of the literal.
      Assert.Equal(2, CountTokensOfType(data, TypeString));
   }

   [Fact]
   public void Build_MultiLineStringLiteral_TokensOnCorrectLines()
   {
      // The string starts on line 0 — second segment must be on line 1.
      const int TypeString = 1;
      const string src = "string s = \"line0\nline1\";";
      var data = SemanticTokensBuilder.Build(src).Data;

      var lines = AbsoluteLines(data);
      var tokenTypes = new List<int>();
      for (int i = 3; i < data.Length; i += 5) tokenTypes.Add(data[i]);

      // Collect lines for string tokens.
      var stringLines = lines.Zip(tokenTypes)
                              .Where(p => p.Second == TypeString)
                              .Select(p => p.First)
                              .ToList();

      Assert.Contains(0, stringLines); // first segment on line 0
      Assert.Contains(1, stringLines); // second segment on line 1
   }

   [Fact]
   public void Build_MultiLineBlockComment_EmitsOneTokenPerLine()
   {
      // Issue #111: /* ... */ spanning multiple lines should colour each line.
      const int TypeComment = 3;
      const string src = "/*\nThis is\na comment\n*/\nnumber x = 1;";
      var data = SemanticTokensBuilder.Build(src).Data;

      // Four lines in the comment block → 4 segments (but line 0 is just "/*" length 2,
      // line 3 is just "*/" length 2, middle lines are non-empty too).
      Assert.True(CountTokensOfType(data, TypeComment) >= 3,
         "Expected at least 3 comment tokens for a 4-line block comment.");
   }

   [Fact]
   public void Build_MultiLineBlockComment_NoTokenCrossesLineBoundary()
   {
      // LSP forbids tokens spanning line boundaries — verify all emitted lengths
      // are ≤ the length of their respective source line.
      const string src = "/*\nline one\nline two\n*/\nnumber x;";
      var data = SemanticTokensBuilder.Build(src).Data;

      var srcLines = src.Split('\n');
      int line = 0;
      for (int i = 0; i < data.Length; i += 5)
      {
         line += data[i];
         int len = data[i + 2];
         Assert.True(line < srcLines.Length,
            $"Token references line {line} but source only has {srcLines.Length} lines.");
         Assert.True(len <= srcLines[line].TrimEnd('\r').Length,
            $"Token length {len} exceeds source line {line} length '{srcLines[line]}'.");
      }
   }

   // ── Helpers ───────────────────────────────────────────────────────────────

   private static int CountTokensOfType(int[] data, int typeIdx)
   {
      int count = 0;
      for (int i = 3; i < data.Length; i += 5)
         if (data[i] == typeIdx) count++;
      return count;
   }

   private static List<int> AbsoluteLines(int[] data)
   {
      var lines = new List<int>(data.Length / 5);
      int line  = 0;
      for (int i = 0; i < data.Length; i += 5)
      {
         line += data[i];
         lines.Add(line);
      }
      return lines;
   }
}
