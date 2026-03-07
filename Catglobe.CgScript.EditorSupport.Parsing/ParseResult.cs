using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using System.Collections.Generic;

namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>The result of parsing a CgScript source file.</summary>
public sealed class ParseResult
{
   /// <summary>The root node of the parse tree produced by ANTLR4.</summary>
   public IParseTree Tree { get; }

   /// <summary>All diagnostics (errors and warnings) reported during parsing.</summary>
   public IReadOnlyList<Diagnostic> Diagnostics { get; }

   /// <summary><c>true</c> when there are no error-severity diagnostics.</summary>
   public bool IsSuccess => !HasErrors;

   /// <summary><c>true</c> when at least one error-severity diagnostic was produced.</summary>
   public bool HasErrors { get; }

   internal ParseResult(IParseTree tree, IReadOnlyList<Diagnostic> diagnostics)
   {
      Tree        = tree;
      Diagnostics = diagnostics;
      foreach (var d in diagnostics)
      {
         if (d.Severity == DiagnosticSeverity.Error)
         {
            HasErrors = true;
            break;
         }
      }
   }

   /// <summary>
   /// Returns a new <see cref="ParseResult"/> that is identical to <paramref name="original"/>
   /// except that <paramref name="extra"/> diagnostics are appended to the list.
   /// </summary>
   public static ParseResult WithExtra(ParseResult original, IEnumerable<Diagnostic> extra)
   {
      var merged = original.Diagnostics.Concat(extra).ToList();
      return new ParseResult(original.Tree, merged);
   }
}
