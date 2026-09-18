using Catglobe.CgScript.Common;
using Xunit;

namespace Catglobe.CgScript.Deployment.Tests;

public class IScriptDefinitionTests
{
   [Fact]
   public void CustomDefinitionsKeepCompilingAndFailClosedOnPii()
   {
      IScriptDefinition definition = new CustomScriptDefinition();

      Assert.False(definition.CanAccessPII);
   }

   private sealed class CustomScriptDefinition : IScriptDefinition
   {
      public string ScriptName => "Custom";

      public Task<Stream> Content => Task.FromResult<Stream>(Stream.Null);

      public uint? Impersonation => null;

      public bool AllowExecuteWithoutLogin => false;
   }
}
