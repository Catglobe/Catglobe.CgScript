using Catglobe.CgScript.EditorSupport.Parsing;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

public class PreprocessorScannerTests
{
   // ── No directives ─────────────────────────────────────────────────────────

   [Fact]
   public void NoDirectives_ReturnsOriginalTextAndNoPositions()
   {
      const string src = "number x = 1;\nreturn x;";
      var (cleaned, directives) = PreprocessorScanner.Strip(src);

      Assert.Equal(src, cleaned);
      Assert.Empty(directives);
   }

   // ── Text replacement ──────────────────────────────────────────────────────

   [Fact]
   public void IfDirective_IsReplacedWithSpaces()
   {
      var (cleaned, _) = PreprocessorScanner.Strip("#IF Development");
      Assert.DoesNotContain("#IF", cleaned);
      Assert.DoesNotContain("Development", cleaned);
   }

   [Fact]
   public void EndifDirective_IsReplacedWithSpaces()
   {
      var (cleaned, _) = PreprocessorScanner.Strip("#ENDIF");
      Assert.DoesNotContain("#ENDIF", cleaned);
   }

   [Fact]
   public void ContentBetweenDirectives_IsPreserved()
   {
      const string src = "#IF Development\nnumber x = 1;\n#ENDIF";
      var (cleaned, _) = PreprocessorScanner.Strip(src);

      Assert.Contains("number x = 1;", cleaned);
   }

   // ── Line count preservation ───────────────────────────────────────────────

   [Fact]
   public void LineCount_IsPreservedAfterStripping()
   {
      const string src = "#IF Development\nnumber x = 1;\n#ENDIF\nreturn x;";
      var (cleaned, _) = PreprocessorScanner.Strip(src);

      Assert.Equal(CountLines(src), CountLines(cleaned));
   }

   [Fact]
   public void ColumnOfTrailingCode_IsUnchangedOnNonDirectiveLines()
   {
      // The directive line is replaced with spaces, so lines after it
      // keep their original line index.
      const string src = "#IF Development\nnumber x = 1;\n#ENDIF";
      var lines = src.Split('\n');

      var (cleaned, _) = PreprocessorScanner.Strip(src);
      var cleanedLines = cleaned.Split('\n');

      Assert.Equal(lines.Length, cleanedLines.Length);
      // Middle line (index 1) must be unchanged.
      Assert.Equal(lines[1], cleanedLines[1]);
   }

   // ── Returned positions ────────────────────────────────────────────────────

   [Fact]
   public void SingleIfDirective_ReturnsOnePosition()
   {
      var (_, directives) = PreprocessorScanner.Strip("#IF Development");
      Assert.Single(directives);
   }

   [Fact]
   public void IfAndEndif_ReturnTwoPositions()
   {
      var (_, directives) = PreprocessorScanner.Strip("#IF Development\nnumber x = 1;\n#ENDIF");
      Assert.Equal(2, directives.Count);
   }

   [Fact]
   public void DirectiveOnLine0_HasLine0()
   {
      var (_, directives) = PreprocessorScanner.Strip("#IF Development");
      Assert.Equal(0, directives[0].Line0);
   }

   [Fact]
   public void DirectiveOnThirdLine_HasLine2()
   {
      const string src = "number a = 1;\nnumber b = 2;\n#ENDIF";
      var (_, directives) = PreprocessorScanner.Strip(src);

      Assert.Single(directives);
      Assert.Equal(2, directives[0].Line0);
   }

   [Fact]
   public void DirectiveWithNoLeadingWhitespace_HasCol0()
   {
      var (_, directives) = PreprocessorScanner.Strip("#IF Development");
      Assert.Equal(0, directives[0].Col);
   }

   [Fact]
   public void DirectiveWithLeadingWhitespace_ColIsIndentWidth()
   {
      var (_, directives) = PreprocessorScanner.Strip("  #IF Development");
      Assert.Equal(2, directives[0].Col);
   }

   [Fact]
   public void DirectiveLength_MatchesDirectiveTextLength()
   {
      const string directive = "#IF Development";
      var (_, directives) = PreprocessorScanner.Strip(directive);
      Assert.Equal(directive.Length, directives[0].Length);
   }

   // ── Multiple blocks ───────────────────────────────────────────────────────

   [Fact]
   public void TwoBlocks_FourDirectivesReturned()
   {
      const string src = "#IF Development\nA\n#ENDIF\n#IF Production\nB\n#ENDIF";
      var (_, directives) = PreprocessorScanner.Strip(src);
      Assert.Equal(4, directives.Count);
   }

   [Fact]
   public void TwoBlocks_ContentFromBothPreserved()
   {
      const string src = "#IF Development\nnumber a = 1;\n#ENDIF\n#IF Production\nnumber b = 2;\n#ENDIF";
      var (cleaned, _) = PreprocessorScanner.Strip(src);

      Assert.Contains("number a = 1;", cleaned);
      Assert.Contains("number b = 2;", cleaned);
   }

   // ── Environment variants ──────────────────────────────────────────────────

   [Theory]
   [InlineData("#IF Development")]
   [InlineData("#IF Production")]
   [InlineData("#IF Staging")]
   public void AllEnvironments_AreRecognised(string directive)
   {
      var (_, directives) = PreprocessorScanner.Strip(directive);
      Assert.Single(directives);
   }

   // ── Case-insensitivity ────────────────────────────────────────────────────

   [Theory]
   [InlineData("#if development")]
   [InlineData("#IF PRODUCTION")]
   [InlineData("#If Staging")]
   [InlineData("#endif")]
   [InlineData("#ENDIF")]
   public void Matching_IsCaseInsensitive(string directive)
   {
      var (_, directives) = PreprocessorScanner.Strip(directive);
      Assert.Single(directives);
   }

   // ── Leading whitespace preserved ──────────────────────────────────────────

   [Fact]
   public void LeadingWhitespace_IsPreservedInCleanedText()
   {
      // The two leading spaces must still be present so that subsequent tokens
      // on the same line keep their column positions intact.
      var (cleaned, _) = PreprocessorScanner.Strip("  #IF Development");
      Assert.StartsWith("  ", cleaned);
   }

   // ── CRLF line endings ─────────────────────────────────────────────────────

   [Fact]
   public void CrLfLineEndings_AreHandled()
   {
      const string src = "#IF Development\r\nnumber x = 1;\r\n#ENDIF";
      var (cleaned, directives) = PreprocessorScanner.Strip(src);

      Assert.Equal(2, directives.Count);
      Assert.Contains("number x = 1;", cleaned);
      Assert.DoesNotContain("#IF", cleaned);
      Assert.DoesNotContain("#ENDIF", cleaned);
   }

   // ── Integration: stripping produces no CGS019 parse errors ──────────────

   [Fact]
   public void Stripped_Script_ProducesNoCGS019Errors()
   {
      // Regression test: valid code containing #IF/#ENDIF blocks must parse without
      // CGS019 (token recognition / syntax) errors after preprocessing.
      const string src = """
         function(string name) {
             string prefix;

             #IF Development
             prefix = "[DEV] ";
             #ENDIF

             #IF Staging
             prefix = "[STAGING] ";
             #ENDIF

             #IF Production
             prefix = "";
             #ENDIF

             return prefix + "Hello, " + name + "!";
         }
         """;

      var (cleanedText, _) = PreprocessorScanner.Strip(src);
      var result = CgScriptParseService.Parse(cleanedText);
      var cgs019 = result.Diagnostics.Where(d => d.Code == "CGS019").ToList();

      Assert.Empty(cgs019);
   }

   // ── Helpers ───────────────────────────────────────────────────────────────

   private static int CountLines(string text) => text.Count(c => c == '\n') + 1;
}
