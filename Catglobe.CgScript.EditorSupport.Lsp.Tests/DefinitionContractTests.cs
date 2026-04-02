
namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

/// <summary>
/// Verifies that the embedded CgScriptDefinitions.json deserialises without any
/// null values in fields that the records declare as non-nullable.
/// ObsoleteDoc is intentionally optional and is not checked.
/// </summary>
public class DefinitionContractTests
{
   private static readonly CgScriptDefinitions _defs = new();

   [Fact]
   public void Functions_AllVariants_HaveRequiredFields()
   {
      Assert.All(_defs.Functions, kvp =>
      {
         Assert.NotNull(kvp.Value.Variants);
         Assert.All(kvp.Value.Variants, v =>
         {
            Assert.NotNull(v.Doc);
            Assert.NotNull(v.Param);
            Assert.NotNull(v.ReturnType);
         });
      });
   }

   [Fact]
   public void Objects_AllMembers_HaveRequiredFields()
   {
      Assert.All(_defs.Objects, kvp =>
      {
         var obj = kvp.Value;
         Assert.NotNull(obj.Doc);
         Assert.NotNull(obj.Constructors);
         Assert.NotNull(obj.Methods);
         Assert.NotNull(obj.StaticMethods);
         Assert.NotNull(obj.Properties);
         Assert.All(obj.Constructors.Concat(obj.Methods).Concat(obj.StaticMethods), m =>
         {
            Assert.NotNull(m.Doc);
            Assert.NotNull(m.Param);
            Assert.NotNull(m.ReturnType);
         });
         Assert.All(obj.Properties, p =>
         {
            Assert.NotNull(p.Doc);
            Assert.NotNull(p.ReturnType);
         });
      });
   }

   [Fact]
   public void Enums_AllValues_HaveRequiredFields()
   {
      Assert.All(_defs.Enums, kvp =>
      {
         Assert.NotNull(kvp.Value.Doc);
         Assert.All(kvp.Value.Values, v => Assert.NotNull(v.Doc));
      });
   }
}
