using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using System.Collections.Generic;

namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>
/// Computes selection-range chains from a CgScript parse tree for the
/// <c>textDocument/selectionRange</c> LSP request.
/// </summary>
public static class SelectionRangeHelper
{
   /// <summary>
   /// Returns the chain of parse-tree nodes that cover the cursor position,
   /// ordered <em>innermost-first</em> (index 0) through to the root (last index).
   /// </summary>
   /// <param name="tree">Root of the ANTLR4 parse tree.</param>
   /// <param name="cursorLine1">1-based line number (ANTLR convention).</param>
   /// <param name="cursorColumn0">0-based column (ANTLR convention).</param>
   public static IReadOnlyList<IParseTree> GetNodeChain(
      IParseTree tree,
      int        cursorLine1,
      int        cursorColumn0)
   {
      var deepest = FindDeepest(tree, cursorLine1, cursorColumn0);
      if (deepest is null)
         return Array.Empty<IParseTree>();

      var chain = new List<IParseTree>();

      // Walk from the deepest matching node up through all ancestors to the root.
      // IParseTree.Parent is declared non-nullable in the ANTLR4 API but returns
      // null at runtime for the root node.  Widening each step to IParseTree?
      // lets the while-condition detect the root cleanly without a pragma.
      IParseTree? cur = deepest;
      while (cur is not null)
      {
         chain.Add(cur);
         cur = cur.Parent; // IParseTree (declared) → IParseTree? (widening); null at root
      }

      return chain;
   }

   // ── Depth-first search ──────────────────────────────────────────────────────

   private static IParseTree? FindDeepest(IParseTree node, int line, int col)
   {
      if (!TryGetSpan(node, out int sl, out int sc, out int el, out int ec))
         return null;

      if (!Covers(sl, sc, el, ec, line, col))
         return null;

      // Try to find a deeper (more-specific) child that also covers the cursor.
      for (int i = 0; i < node.ChildCount; i++)
      {
         var deeper = FindDeepest(node.GetChild(i), line, col);
         if (deeper is not null)
            return deeper;
      }

      // No child covers the cursor — this node is the deepest match.
      // Exclude EOF terminal nodes: they carry no meaningful source text.
      if (node is ITerminalNode tn && tn.Symbol.Type == TokenConstants.EOF)
         return null;

      return node;
   }

   // ── Span helpers ────────────────────────────────────────────────────────────

   private static bool TryGetSpan(
      IParseTree node,
      out int startLine, out int startCol,
      out int stopLine,  out int stopCol)
   {
      if (node is ParserRuleContext rule)
      {
         var s = rule.Start;
         var e = rule.Stop;
         if (s is null || e is null)
         {
            startLine = startCol = stopLine = stopCol = 0;
            return false;
         }
         startLine = s.Line;
         startCol  = s.Column;
         stopLine  = e.Line;
         stopCol   = e.Column + TokenTextLen(e) - 1;
         return true;
      }

      if (node is ITerminalNode terminal)
      {
         var sym = terminal.Symbol;
         if (sym.Type == TokenConstants.EOF)
         {
            startLine = startCol = stopLine = stopCol = 0;
            return false;
         }
         startLine = sym.Line;
         startCol  = sym.Column;
         stopLine  = sym.Line;
         stopCol   = sym.Column + TokenTextLen(sym) - 1;
         return true;
      }

      startLine = startCol = stopLine = stopCol = 0;
      return false;
   }

   /// <summary>Safe token text length — never returns less than 1.</summary>
   private static int TokenTextLen(IToken tok)
      => tok.Text is { Length: > 0 } t ? t.Length : 1;

   private static bool Covers(
      int startLine, int startCol,
      int stopLine,  int stopCol,
      int line,      int col)
      => (startLine < line || (startLine == line && startCol <= col))
      && (stopLine  > line || (stopLine  == line && stopCol  >= col));
}
