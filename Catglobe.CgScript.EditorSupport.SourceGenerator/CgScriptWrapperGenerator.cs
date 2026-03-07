using Catglobe.CgScript.EditorSupport.Parsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Catglobe.CgScript.EditorSupport.SourceGenerator;

/// <summary>
/// Roslyn incremental source generator.
/// For each <c>.cgs</c> file included as an <c>AdditionalFile</c> in the consuming project,
/// detects the workflow parameter pattern and emits a typed C# wrapper method inside
/// <c>CgScriptWrappers</c> (a <c>partial static class</c> the user must declare in their project).
/// Also runs the CgScript semantic analyser and reports any diagnostics as build warnings/errors.
///
/// Usage in consuming .csproj:
/// <code>
///   &lt;ItemGroup&gt;
///     &lt;AdditionalFiles Include="CgScript\**\*.cgs" /&gt;
///   &lt;/ItemGroup&gt;
/// </code>
/// </summary>
[Generator]
public sealed class CgScriptWrapperGenerator : IIncrementalGenerator
{
   public void Initialize(IncrementalGeneratorInitializationContext context)
   {
      // Collect all .cgs AdditionalFiles
      var cgsFiles = context.AdditionalTextsProvider
         .Where(static f => f.Path.EndsWith(".cgs", System.StringComparison.OrdinalIgnoreCase));

      // Combine with the compilation so we can read the root namespace
      var combined = cgsFiles.Combine(context.CompilationProvider);

      context.RegisterSourceOutput(combined, static (spc, pair) =>
      {
         var (file, compilation) = pair;
         var text = file.GetText(spc.CancellationToken);
         if (text is null) return;

         var source   = text.ToString();
         var baseName = Path.GetFileNameWithoutExtension(file.Path);

         // ── Semantic diagnostics ──────────────────────────────────────────────
         ReportSemanticDiagnostics(spc, file, text, source);

         // ── Wrapper code generation ───────────────────────────────────────────
         var meta = ScriptParser.TryParse(baseName, source);
         if (meta is null) return;

         var ns        = compilation.AssemblyName ?? "CgScriptGenerated";
         var body      = WrapperEmitter.Emit(meta, ns);
         var fullSource = WrapperEmitter.WrapInPartialClass(ns, body);

         spc.AddSource($"CgScript.{baseName}.g.cs",
            SourceText.From(fullSource, Encoding.UTF8));
      });
   }

   private static void ReportSemanticDiagnostics(
      SourceProductionContext spc,
      AdditionalText          file,
      SourceText              text,
      string                  source)
   {
      // Parse and analyse
      var parseResult = CgScriptParseService.Parse(source);
      var allDiags    = new List<Parsing.Diagnostic>(parseResult.Diagnostics);

      var semanticDiags = SemanticAnalyzer.Analyze(
         parseResult.Tree,
         KnownNamesLoader.FunctionNames,
         KnownNamesLoader.ObjectNames,
         KnownNamesLoader.ConstantNames);

      allDiags.AddRange(semanticDiags);

      foreach (var d in allDiags)
      {
         var descriptor = CgScriptDiagnostics.DescriptorFor(d);
         var location   = ToLocation(file, text, d);
         spc.ReportDiagnostic(Microsoft.CodeAnalysis.Diagnostic.Create(descriptor, location, d.Message));
      }
   }

   private static Location ToLocation(AdditionalText file, SourceText text, Parsing.Diagnostic d)
   {
      if (d.Line <= 0 || d.Line > text.Lines.Count)
         return Location.None;

      var line = text.Lines[d.Line - 1];
      var col  = System.Math.Min(d.Column, line.Span.Length);
      var len  = System.Math.Min(d.Length, line.Span.Length - col);
      var span = new TextSpan(line.Start + col, System.Math.Max(len, 0));
      return Location.Create(file.Path, span, text.Lines.GetLinePositionSpan(span));
   }
}
