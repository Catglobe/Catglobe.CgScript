using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
using System.Text;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

/// <summary>
/// Contract tests: every required field in the embedded CgScriptDefinitions.json
/// must be non-null.  ObsoleteDoc is intentionally optional and is not checked.
/// </summary>
public class DefinitionContractTests
{
   private static readonly DefinitionLoader _defs = new();

   [Fact]
   public void Functions_AllVariants_HaveRequiredFields()
   {
      var violations = new StringBuilder();
      foreach (var (name, fn) in _defs.Functions)
      {
         var variants = fn.Variants;
         if (variants == null) { violations.AppendLine($"functions[{name}].variants is null"); continue; }
         for (var i = 0; i < variants.Length; i++)
         {
            var v = variants[i];
            if (v.Doc        == null) violations.AppendLine($"functions[{name}].variants[{i}].doc is null");
            if (v.Param      == null) violations.AppendLine($"functions[{name}].variants[{i}].param is null");
            if (v.ReturnType == null) violations.AppendLine($"functions[{name}].variants[{i}].returnType is null");
         }
      }
      Assert.True(violations.Length == 0, $"Required fields are null:\n{violations}");
   }

   [Fact]
   public void Objects_AllMembers_HaveRequiredFields()
   {
      var violations = new StringBuilder();
      foreach (var (typeName, obj) in _defs.Objects)
      {
         if (obj.Doc          == null) violations.AppendLine($"objects[{typeName}].doc is null");
         if (obj.Constructors == null) violations.AppendLine($"objects[{typeName}].constructors is null");
         if (obj.Methods      == null) violations.AppendLine($"objects[{typeName}].methods is null");
         if (obj.StaticMethods == null) violations.AppendLine($"objects[{typeName}].staticMethods is null");
         if (obj.Properties   == null) violations.AppendLine($"objects[{typeName}].properties is null");

         foreach (var m in (obj.Constructors ?? []).Concat(obj.Methods ?? []).Concat(obj.StaticMethods ?? []))
         {
            if (m.Doc        == null) violations.AppendLine($"objects[{typeName}].method[{m.Name}].doc is null");
            if (m.Param      == null) violations.AppendLine($"objects[{typeName}].method[{m.Name}].param is null");
            if (m.ReturnType == null) violations.AppendLine($"objects[{typeName}].method[{m.Name}].returnType is null");
         }

         foreach (var p in obj.Properties ?? [])
         {
            if (p.Doc        == null) violations.AppendLine($"objects[{typeName}].property[{p.Name}].doc is null");
            if (p.ReturnType == null) violations.AppendLine($"objects[{typeName}].property[{p.Name}].returnType is null");
         }
      }
      Assert.True(violations.Length == 0, $"Required fields are null:\n{violations}");
   }

   [Fact]
   public void Enums_AllValues_HaveRequiredFields()
   {
      var violations = new StringBuilder();
      foreach (var (enumName, enumDef) in _defs.Enums)
      {
         if (enumDef.Doc == null) violations.AppendLine($"enums[{enumName}].doc is null");
         foreach (var v in enumDef.Values)
            if (v.Doc == null) violations.AppendLine($"enums[{enumName}].values[{v.Name}].doc is null");
      }
      Assert.True(violations.Length == 0, $"Required fields are null:\n{violations}");
   }
}
