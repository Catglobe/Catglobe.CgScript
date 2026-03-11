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
      // The 'number' keyword on line 1 should produce a keyword token.
      const int TypeKeyword = 0;
      const string src = "#IF Development\nnumber x = 1;\n#ENDIF";
      var data = SemanticTokensBuilder.Build(src).Data;

      Assert.True(CountTokensOfType(data, TypeKeyword) > 0,
         "Expected at least one keyword token for the content inside the #IF block.");
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
