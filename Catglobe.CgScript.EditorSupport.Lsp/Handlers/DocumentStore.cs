using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
using Catglobe.CgScript.EditorSupport.Parsing;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Catglobe.CgScript.EditorSupport.Lsp.Handlers;

/// <summary>
/// Keeps the most-recently-parsed version of each open document.
/// Parsing is followed by semantic analysis so that the stored
/// <see cref="ParseResult"/> contains both syntax and semantic diagnostics.
/// </summary>
public sealed class DocumentStore
{
   private readonly ConcurrentDictionary<string, (string Text, ParseResult Result)> _docs = new();
   private readonly DefinitionLoader _definitions;
   private readonly IReadOnlyDictionary<string, ObjectMemberInfo> _objectMemberInfos;
   private readonly IReadOnlyDictionary<string, FunctionInfo>     _functionInfos;
   private readonly IReadOnlyDictionary<string, string?>          _obsoleteFunctions;
   private readonly IReadOnlyDictionary<string, string?>          _obsoleteConstants;

   public DocumentStore(DefinitionLoader definitions)
   {
      _definitions        = definitions;
      _objectMemberInfos  = BuildMemberInfos(definitions.Objects);
      _functionInfos      = BuildFunctionInfos(definitions.Functions);
      _obsoleteFunctions  = BuildObsoleteFunctions(definitions.Functions);
      _obsoleteConstants  = BuildObsoleteConstants(definitions.Enums);
   }

   public void Update(string uri, string text)
   {
      // Expose the new text immediately so concurrent readers (hover, semantic tokens, etc.)
      // never see a stale version while the parse is running.
      if (_docs.TryGetValue(uri, out var existing))
         _docs[uri] = (text, existing.Result);

      // Each stage is caught independently so a failure in one step does not discard
      // valid results from earlier steps.  The user always sees a diagnostic instead of
      // a silent LSP disconnection.

      // Stage 1: preprocessor stripping
      string cleanedText;
      var stageDiags = new List<Diagnostic>();
      try
      {
         (cleanedText, _) = PreprocessorScanner.Strip(text);
      }
      catch (Exception ex)
      {
         stageDiags.Add(new Diagnostic(DiagnosticSeverity.Error,
            $"Internal LSP error (preprocessor): {ex.Message}", 1, 0, 0, "CGS000"));
         cleanedText = text; // fall back to raw text so later stages still run
      }

      // Stage 2: parsing
      ParseResult? result = null;
      try
      {
         result = CgScriptParseService.Parse(cleanedText);
      }
      catch (Exception ex)
      {
         stageDiags.Add(new Diagnostic(DiagnosticSeverity.Error,
            $"Internal LSP error (parser): {ex.Message}", 1, 0, 0, "CGS000"));
         // Fall back to an empty-program tree so semantic tokens / hover still have a valid tree.
         try { result = CgScriptParseService.Parse(string.Empty); } catch { /* last resort */ }
      }

      if (result is null)
      {
         // If we have no tree at all we cannot proceed; leave the previous result in place.
         if (stageDiags.Count > 0 && _docs.TryGetValue(uri, out var prev))
            _docs[uri] = (text, ParseResult.WithExtra(prev.Result, stageDiags));
         return;
      }

      // Stage 3: semantic analysis
      // Policy for transient failures: a semantic analysis exception is likely triggered by an
      // intermediate (incomplete) expression being typed.  Surfacing CGS000 on every keystroke
      // would be very noisy.  Instead we store the syntax-only result (fresh, accurate line numbers)
      // and skip semantic diagnostics for this cycle.  The next successful update restores them.
      // The exception is logged to Debug output for LSP developer diagnostics.
      IEnumerable<Diagnostic> extraDiags = [];
      try
      {
         extraDiags = SemanticAnalyzer.Analyze(
            result.Tree,
            _definitions.Functions.Keys,
            _definitions.Objects.Keys,
            _definitions.Constants,
            _objectMemberInfos,
            _definitions.GlobalVariables,
            _functionInfos,
            _obsoleteFunctions,
            _obsoleteConstants);
      }
      catch (Exception ex)
      {
         System.Diagnostics.Debug.WriteLine(
            $"[CgScript LSP] Semantic analysis error for '{uri}': {ex}");
         // Leave extraDiags empty — syntax diagnostics from 'result' are preserved and the
         // user sees no spurious CGS000 flash.  Semantic results resume on the next update.
      }

      _docs[uri] = (text, ParseResult.WithExtra(result, extraDiags.Concat(stageDiags)));
   }

   public void Remove(string uri) => _docs.TryRemove(uri, out _);

   public ParseResult? GetParseResult(string uri)
      => _docs.TryGetValue(uri, out var entry) ? entry.Result : null;

   public string? GetText(string uri)
      => _docs.TryGetValue(uri, out var entry) ? entry.Text : null;

   private static IReadOnlyDictionary<string, ObjectMemberInfo> BuildMemberInfos(
      IReadOnlyDictionary<string, ObjectDefinition> objects)
   {
      var result = new Dictionary<string, ObjectMemberInfo>(StringComparer.Ordinal);
      foreach (var kvp in objects)
      {
         var def              = kvp.Value;
         var properties       = new Dictionary<string, bool>(StringComparer.Ordinal);
         var propertyRetTypes = new Dictionary<string, string>(StringComparer.Ordinal);
         var obsoleteProps    = new Dictionary<string, string?>(StringComparer.Ordinal);
         if (def.Properties != null)
            foreach (var p in def.Properties)
            {
               properties[p.Name] = p.HasSetter;
               if (!string.IsNullOrEmpty(p.ReturnType))
                  propertyRetTypes[p.Name] = p.ReturnType;
               if (p.IsObsolete)
                  obsoleteProps[p.Name] = p.ObsoleteDoc;
            }

         var methods        = new List<string>();
         var obsoleteMethods = new Dictionary<string, string?>(StringComparer.Ordinal);
         Dictionary<string, IReadOnlyList<IReadOnlyList<string>>>? methodOverloads = null;
         if (def.Methods != null)
         {
            var mOverloads = new Dictionary<string, List<IReadOnlyList<string>>>(StringComparer.Ordinal);
            foreach (var m in def.Methods)
               if (!string.IsNullOrEmpty(m.Name))
               {
                  methods.Add(m.Name);
                  if (m.IsObsolete)
                     obsoleteMethods[m.Name] = m.ObsoleteDoc;
                  if (!mOverloads.TryGetValue(m.Name, out var mol))
                  {
                     mol = new List<IReadOnlyList<string>>();
                     mOverloads[m.Name] = mol;
                  }
                  mol.Add(m.Param?.Select(p => p.Type ?? "").ToList() ?? new List<string>());
               }
            if (mOverloads.Count > 0)
            {
               var frozen = new Dictionary<string, IReadOnlyList<IReadOnlyList<string>>>(StringComparer.Ordinal);
               foreach (var mo in mOverloads)
                  frozen[mo.Key] = mo.Value;
               methodOverloads = frozen;
            }
         }

         List<IReadOnlyList<string>>? constructorOverloads = null;
         if (def.Constructors != null && def.Constructors.Length > 0)
         {
            constructorOverloads = new List<IReadOnlyList<string>>();
            foreach (var ctor in def.Constructors)
               constructorOverloads.Add(ctor.Param?.Select(p => p.Type ?? "").ToList() ?? new List<string>());
         }

         // Inject the preprocessor special case for WorkflowScript: new WorkflowScript("filename")
         // This form doesn't exist in the real API — the source generator replaces it with
         // new WorkflowScript(resourceId) on deployment.  No CGS023 should be raised for it.
         if (kvp.Key == "WorkflowScript" && constructorOverloads != null)
            constructorOverloads.Add(new[] { "string" });

         result[kvp.Key] = new ObjectMemberInfo(
            properties, methods, propertyRetTypes,
            constructorOverloads: constructorOverloads,
            methodOverloads: methodOverloads,
            obsoletePropertyNames: obsoleteProps.Count > 0 ? obsoleteProps : null,
            obsoleteMethodNames:   obsoleteMethods.Count > 0 ? obsoleteMethods : null);
      }
      return result;
   }

   private static IReadOnlyDictionary<string, FunctionInfo> BuildFunctionInfos(
      IReadOnlyDictionary<string, FunctionDefinition> functions)
   {
      var result = new Dictionary<string, FunctionInfo>(StringComparer.Ordinal);
      foreach (var kvp in functions)
      {
         var def = kvp.Value;
         if (def.Variants == null) continue;
         var overloads    = new List<IReadOnlyList<string>>(def.Variants.Length);
         bool allObsolete  = def.Variants.Length > 0;
         string? obsoleteDoc = null;
         foreach (var variant in def.Variants)
         {
            var paramTypes = new List<string>(variant.Param?.Length ?? 0);
            if (variant.Param != null)
               foreach (var p in variant.Param)
                  paramTypes.Add(p.Type ?? "");
            overloads.Add(paramTypes);
            if (!variant.IsObsolete)
               allObsolete = false;
            else
               obsoleteDoc ??= variant.ObsoleteDoc;
         }
         result[kvp.Key] = new FunctionInfo(overloads, isObsolete: allObsolete, obsoleteDoc: obsoleteDoc);
      }
      return result;
   }

   private static IReadOnlyDictionary<string, string?> BuildObsoleteFunctions(
      IReadOnlyDictionary<string, FunctionDefinition> functions)
   {
      var result = new Dictionary<string, string?>(StringComparer.Ordinal);
      foreach (var kvp in functions)
      {
         var def = kvp.Value;
         if (def.Variants != null && def.Variants.Length > 0
             && def.Variants.All(v => v.IsObsolete))
            result[kvp.Key] = def.Variants.Select(v => v.ObsoleteDoc).FirstOrDefault(d => d != null);
      }
      return result;
   }

   private static IReadOnlyDictionary<string, string?> BuildObsoleteConstants(
      IReadOnlyDictionary<string, EnumDefinition> enums)
   {
      var result = new Dictionary<string, string?>(StringComparer.Ordinal);
      foreach (var enumDef in enums.Values)
         foreach (var v in enumDef.Values)
            if (v.IsObsolete)
               result[v.Name] = v.ObsoleteDoc;
      return result;
   }
}
