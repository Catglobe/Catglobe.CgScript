using Catglobe.CgScript.EditorSupport.Lsp.Handlers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

/// <summary>
/// Verifies that <see cref="CgScriptLanguageTarget.StatementSnippets"/> contains
/// the expected pre-defined snippets for common language constructs.
/// </summary>
public class StatementSnippetsTests
{
   // ── Presence of expected snippets ────────────────────────────────────────

   [Theory]
   [InlineData("if statement",        "if")]
   [InlineData("if-else statement",   "if")]
   [InlineData("while statement",     "while")]
   [InlineData("for-in statement",    "for")]
   [InlineData("for-var statement",   "for")]
   [InlineData("switch statement",    "switch")]
   [InlineData("try-catch statement", "try")]
   public void StatementSnippets_ContainsExpectedEntry(string label, string filter)
   {
      Assert.Contains(CgScriptLanguageTarget.StatementSnippets,
         s => s.Label == label && s.Filter == filter);
   }

   // ── Snippet text is valid LSP snippet syntax ──────────────────────────────

   [Fact]
   public void ForIn_SnippetContainsForKeyword()
   {
      var (_, _, snippet) = Array.Find(CgScriptLanguageTarget.StatementSnippets,
         s => s.Label == "for-in statement");
      Assert.Contains(" for ", snippet);
   }

   [Fact]
   public void ForVar_SnippetContainsTwoSemicolons_AndAssignmentIncrement()
   {
      var (_, _, snippet) = Array.Find(CgScriptLanguageTarget.StatementSnippets,
         s => s.Label == "for-var statement");
      // Classic C-style for loop needs exactly two semicolons (init; condition; update)
      Assert.Equal(2, snippet.Count(c => c == ';'));
      // Must have an initialiser (=) and an increment assignment
      Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(snippet, @"=").Count);
   }

   [Fact]
   public void TryCatch_SnippetContainsCatch()
   {
      var (_, _, snippet) = Array.Find(CgScriptLanguageTarget.StatementSnippets,
         s => s.Label == "try-catch statement");
      Assert.Contains("catch", snippet);
   }

   [Fact]
   public void Switch_SnippetContainsDefault()
   {
      var (_, _, snippet) = Array.Find(CgScriptLanguageTarget.StatementSnippets,
         s => s.Label == "switch statement");
      Assert.Contains("default", snippet);
   }

   // ── All snippets use LSP snippet tab-stop syntax ──────────────────────────

   [Fact]
   public void AllSnippets_ContainTabStopSyntax()
   {
      foreach (var (label, _, snippet) in CgScriptLanguageTarget.StatementSnippets)
         Assert.True(snippet.Contains('$'), $"Snippet '{label}' has no tab-stop ($).");
   }
}
