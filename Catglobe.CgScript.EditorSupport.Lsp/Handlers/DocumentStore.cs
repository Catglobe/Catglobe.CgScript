using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
using Catglobe.CgScript.EditorSupport.Parsing;
using System.Collections.Concurrent;

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

   public DocumentStore(DefinitionLoader definitions)
   {
      _definitions       = definitions;
      _objectMemberInfos = BuildMemberInfos(definitions.Objects);
      _functionInfos     = BuildFunctionInfos(definitions.Functions);
   }

   public void Update(string uri, string text)
   {
      // Expose the new text immediately so concurrent readers (hover, semantic tokens, etc.)
      // never see a stale version while the parse is running.
      if (_docs.TryGetValue(uri, out var existing))
         _docs[uri] = (text, existing.Result);

      var (cleanedText, _) = PreprocessorScanner.Strip(text);
      var result     = CgScriptParseService.Parse(cleanedText);
      var extraDiags = SemanticAnalyzer.Analyze(
         result.Tree,
         _definitions.Functions.Keys,
         _definitions.Objects.Keys,
         _definitions.Constants,
         _objectMemberInfos,
         _definitions.GlobalVariables,
         _functionInfos);
      var merged = ParseResult.WithExtra(result, extraDiags);
      _docs[uri] = (text, merged);
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
         var def             = kvp.Value;
         var properties      = new Dictionary<string, bool>(StringComparer.Ordinal);
         var propertyRetTypes = new Dictionary<string, string>(StringComparer.Ordinal);
         if (def.Properties != null)
            foreach (var p in def.Properties)
            {
               properties[p.Name] = p.HasSetter;
               if (!string.IsNullOrEmpty(p.ReturnType))
                  propertyRetTypes[p.Name] = p.ReturnType;
            }

         var methods = new List<string>();
         if (def.Methods != null)
            foreach (var m in def.Methods)
               if (!string.IsNullOrEmpty(m.Name))
                  methods.Add(m.Name);

         result[kvp.Key] = new ObjectMemberInfo(properties, methods, propertyRetTypes);
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
         // Skip new-style functions (they use variants/overloads) and functions
         // without parameter definitions — we have no signature to validate against,
         // and including them would produce false-positive CGS022 errors.
         if (def.IsNewStyle || def.Parameters == null || def.Parameters.Length == 0) continue;

         var paramInfos = new List<FunctionParamInfo>(def.Parameters.Length);
         foreach (var p in def.Parameters)
            paramInfos.Add(new FunctionParamInfo(p.ConstantType, p.ObjectType));

         result[kvp.Key] = new FunctionInfo(def.ReturnType, def.NumberOfRequiredArguments, paramInfos);
      }
      return result;
   }
}
