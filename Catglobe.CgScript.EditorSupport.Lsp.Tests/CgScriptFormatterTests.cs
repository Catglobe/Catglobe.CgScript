using Catglobe.CgScript.EditorSupport.Lsp.Handlers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

/// <summary>
/// Verifies that <see cref="CgScriptFormatter"/> produces well-formatted CgScript output
/// consistent with clang default style.
/// </summary>
public class CgScriptFormatterTests
{
   // ── issue example ─────────────────────────────────────────────────────────

   [Fact]
   public void Format_IssueExample_AddsSpaceAndIndent()
   {
      // From the issue: before → after
      var input    = "if(true)\ntrue;";
      var expected = "if (true)\n  true;\n";
      Assert.Equal(expected, CgScriptFormatter.Format(input));
   }

   // ── keyword spacing ───────────────────────────────────────────────────────

   [Fact]
   public void Format_IfWithBlock_AddsSpaceBeforeParen()
   {
      var input    = "if(x == 1) { }";
      var result   = CgScriptFormatter.Format(input);
      Assert.StartsWith("if (", result);
   }

   [Fact]
   public void Format_WhileLoop_AddsSpaceBeforeParen()
   {
      var input  = "while(true) { break; }";
      var result = CgScriptFormatter.Format(input);
      Assert.StartsWith("while (", result);
   }

   [Fact]
   public void Format_ForLoop_AddsSpaceBeforeParen()
   {
      var input  = "for(i for 0; 10) { }";
      var result = CgScriptFormatter.Format(input);
      Assert.StartsWith("for (", result);
   }

   // ── indentation ───────────────────────────────────────────────────────────

   [Fact]
   public void Format_BlockBody_IndentsContents()
   {
      var input  = "if(true) { number x = 1; }";
      var result = CgScriptFormatter.Format(input);
      // Body should be indented with 2 spaces.
      Assert.Contains("\n  number x = 1;", result);
   }

   [Fact]
   public void Format_NonBlockBody_IndentsStatement()
   {
      var input  = "if(true)\nnumber x = 1;";
      var result = CgScriptFormatter.Format(input);
      Assert.Contains("\n  number x = 1;", result);
   }

   [Fact]
   public void Format_NestedBlocks_IndentsCorrectly()
   {
      var input  = "if(a) {\nif(b) {\nnumber x = 1;\n}\n}";
      var result = CgScriptFormatter.Format(input);
      Assert.Contains("  if (b)", result);
      Assert.Contains("    number x", result);
   }

   // ── binary operator spacing ───────────────────────────────────────────────

   [Fact]
   public void Format_BinaryEquals_HasSpaces()
   {
      var result = CgScriptFormatter.Format("number x = a==b;");
      Assert.Contains("a == b", result);
   }

   [Fact]
   public void Format_Assignment_HasSpaces()
   {
      var result = CgScriptFormatter.Format("number x=1;");
      Assert.Contains("x = 1", result);
   }

   [Fact]
   public void Format_Addition_HasSpaces()
   {
      var result = CgScriptFormatter.Format("number x = a+b;");
      Assert.Contains("a + b", result);
   }

   // ── comma spacing ─────────────────────────────────────────────────────────

   [Fact]
   public void Format_FunctionCall_SpaceAfterComma()
   {
      var result = CgScriptFormatter.Format("foo(a,b,c);");
      Assert.Contains("foo(a, b, c)", result);
   }

   // ── else / else-if ────────────────────────────────────────────────────────

   [Fact]
   public void Format_IfElse_ElseOnSameLine()
   {
      var input  = "if(a) { x = 1; } else { x = 2; }";
      var result = CgScriptFormatter.Format(input);
      Assert.Contains("} else {", result);
   }

   // ── idempotency ───────────────────────────────────────────────────────────

   [Fact]
   public void Format_AlreadyFormatted_IsIdempotent()
   {
      var input = "if (true)\n  number x = 1;\n";
      Assert.Equal(input, CgScriptFormatter.Format(input));
   }

   // ── tab-size option ───────────────────────────────────────────────────────

   [Fact]
   public void Format_WithTabSize4_Indents4Spaces()
   {
      var opts   = new FormattingOptions { TabSize = 4, InsertSpaces = true };
      var input  = "if(true) { number x = 1; }";
      var result = CgScriptFormatter.Format(input, opts);
      Assert.Contains("\n    number x", result);
   }

   // ── return statement ──────────────────────────────────────────────────────

   [Fact]
   public void Format_ReturnStatement_HasSpace()
   {
      var result = CgScriptFormatter.Format("return 42;");
      Assert.StartsWith("return 42", result);
   }

   // ── dictionary formatting ─────────────────────────────────────────────────

   [Fact]
   public void Format_ShortDictionary_StaysOnOneLine()
   {
      // Short enough to fit in 80 chars — stays inline.
      var input    = "return {\"a\": 1, \"b\": false,};";
      var expected = "return {\"a\" : 1, \"b\" : false,};\n";
      Assert.Equal(expected, CgScriptFormatter.Format(input));
   }

   [Fact]
   public void Format_ShortDictionary_IsIdempotent()
   {
      var input = "return {\"a\" : 1, \"b\" : false,};\n";
      Assert.Equal(input, CgScriptFormatter.Format(input));
   }

   [Fact]
   public void Format_ShortNestedDictionary_StaysOnOneLine()
   {
      var input    = "return {\"a\": {\"x\": 1, \"y\": 2,}, \"b\": 3,};";
      var expected = "return {\"a\" : {\"x\" : 1, \"y\" : 2,}, \"b\" : 3,};\n";
      Assert.Equal(expected, CgScriptFormatter.Format(input));
   }

   [Fact]
   public void Format_ShortDictionaryWithFunctionCallValue_StaysOnOneLine()
   {
      // Commas inside function call args should NOT produce newlines even in expanded mode.
      var input    = "return {\"a\": foo(1, 2),};";
      var expected = "return {\"a\" : foo(1, 2),};\n";
      Assert.Equal(expected, CgScriptFormatter.Format(input));
   }

   [Fact]
   public void Format_LongDictionary_ExpandsToMultipleLines()
   {
      // Inline form would exceed 80 chars → each entry on its own line.
      var input    = "return {\"longKey1\" : value1, \"longKey2\" : value2, \"longKey3\" : value3, \"longKey4\" : value4,};";
      var expected = "return {\n  \"longKey1\" : value1,\n  \"longKey2\" : value2,\n  \"longKey3\" : value3,\n  \"longKey4\" : value4,\n};\n";
      Assert.Equal(expected, CgScriptFormatter.Format(input));
   }

   [Fact]
   public void Format_LongDictionaryExpanded_IsIdempotent()
   {
      var input = "return {\n  \"longKey1\" : value1,\n  \"longKey2\" : value2,\n  \"longKey3\" : value3,\n  \"longKey4\" : value4,\n};\n";
      Assert.Equal(input, CgScriptFormatter.Format(input));
   }



   // ── array formatting ──────────────────────────────────────────────────────

   [Fact]
   public void Format_ShortArray_StaysOnOneLine()
   {
      // Short enough to fit in 80 chars — inline, no expansion.
      var input    = "return {1, 2, 3};";
      var expected = "return {1, 2, 3};\n";
      Assert.Equal(expected, CgScriptFormatter.Format(input));
   }

   [Fact]
   public void Format_LongArrayShortElements_ChunksMultiplePerLine()
   {
      // Array whose inline form exceeds 80 chars but elements are short: multiple
      // elements per line, wrapping when the next element would push past 80.
      var input    = "return {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30,};";
      var expected = "return {\n  1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21,\n  22, 23, 24, 25, 26, 27, 28, 29, 30,\n};\n";
      Assert.Equal(expected, CgScriptFormatter.Format(input));
   }

   [Fact]
   public void Format_LongArrayChunked_IsIdempotent()
   {
      var input = "return {\n  1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21,\n  22, 23, 24, 25, 26, 27, 28, 29, 30,\n};\n";
      Assert.Equal(input, CgScriptFormatter.Format(input));
   }

   [Fact]
   public void Format_LongArrayLongElements_OnePerLine()
   {
      // Elements are too long to fit two per line — chunked mode naturally
      // degenerates to one element per line (each is ~47 chars).
      var input  = "return {veryLongArrayElementNameForTestingPurposesHere1, veryLongArrayElementNameForTestingPurposesHere2, veryLongArrayElementNameForTestingPurposesHere3,};";
      var result = CgScriptFormatter.Format(input);
      Assert.Equal(
         "return {\n  veryLongArrayElementNameForTestingPurposesHere1,\n  veryLongArrayElementNameForTestingPurposesHere2,\n  veryLongArrayElementNameForTestingPurposesHere3,\n};\n",
         result);
   }

   [Fact]
   public void Format_EmptyString_ReturnsEmpty()
   {
      Assert.Equal(string.Empty, CgScriptFormatter.Format(string.Empty));
   }
}
