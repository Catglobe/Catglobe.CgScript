using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
using Catglobe.CgScript.EditorSupport.Parsing;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

public class DefinitionLoaderEnumTests
{
   [Fact]
   public void DefinitionLoader_ConstantsContainHandWrittenConstant()
   {
      var definitions = new DefinitionLoader();

      Assert.Contains("DATETIME_DAY", definitions.Constants);
   }

   [Fact]
   public void KnownNamesLoader_ConstantNamesContainHandWrittenConstant()
   {
      Assert.Contains("DATETIME_DAY", KnownNamesLoader.ConstantNames);
   }

   /// <summary>
   /// COLOR_RED is derived from ColorCGO.Constants (a [Cg("COLOR",...)] enum),
   /// proving that enum → prefixed-constant registration survives the unified JSON path.
   /// </summary>
   [Fact]
   public void DefinitionLoader_ConstantsContainEnumDerivedColorConstant()
   {
      var definitions = new DefinitionLoader();

      Assert.Contains("COLOR_RED", definitions.Constants);
   }

   [Fact]
   public void KnownNamesLoader_ConstantNamesContainEnumDerivedColorConstant()
   {
      Assert.Contains("COLOR_RED", KnownNamesLoader.ConstantNames);
   }
}
