using Catglobe.CgScript.Common;
using Catglobe.CgScript.Deployment;
using Xunit;

namespace Catglobe.CgScript.Deployment.Tests;

public class ScriptFromFileOnDiskTests
{
   [Theory]
   [InlineData("Name.cgs", "Name", null, false)]
   [InlineData("Name@123.cgs", "Name", 123, false)]
   [InlineData("Name@123.public.cgs", "Name", 123, true)]
   public void ReadsSecurityMetadataFromNamesWithoutMarkers(string relativePath, string scriptName, int? impersonation, bool allowExecuteWithoutLogin)
   {
      using var folder = new TempScriptFolder();
      var definition = new ScriptFromFileOnDisk(folder.Write(relativePath, "return 1;"), relativePath);

      Assert.Equal(scriptName, definition.ScriptName);
      Assert.Equal((uint?)impersonation, definition.Impersonation);
      Assert.Equal(allowExecuteWithoutLogin, definition.AllowExecuteWithoutLogin);
   }

   [Theory]
   [InlineData("Weather/GetData@42.pii.cgs", false)]
   [InlineData("Weather/GetData@42.pii.public.cgs", true)]
   public void StripsThePiiMarkerFromTheScriptName(string relativePath, bool allowExecuteWithoutLogin)
   {
      var definition = new ScriptFromFileOnDisk(Path.Combine(@"C:\scripts\", relativePath.Replace('/', '\\')), relativePath);

      Assert.Equal("Weather/GetData", definition.ScriptName);
      Assert.Equal((uint?)42, definition.Impersonation);
      Assert.Equal(allowExecuteWithoutLogin, definition.AllowExecuteWithoutLogin);
      Assert.True(definition.CanAccessPII);
   }

   [Theory]
   [InlineData("Weather/GetData.cgs")]
   [InlineData("Weather/GetData@42.cgs")]
   [InlineData("Weather/GetData@42.public.cgs")]
   public void DoesNotRequestPiiWithoutTheMarker(string relativePath)
   {
      var definition = new ScriptFromFileOnDisk(Path.Combine(@"C:\scripts\", relativePath.Replace('/', '\\')), relativePath);

      Assert.False(definition.CanAccessPII);
   }

   [Fact]
   public void RejectsMalformedNameWithTheParserError()
   {
      const string relativePath = "Weather/GetData@42.public.pii.cgs";

      var exception = Assert.Throws<InvalidScriptFileNameException>(() => new ScriptFromFileOnDisk(Path.Combine(@"C:\scripts\", "Weather", "GetData@42.public.pii.cgs"), relativePath));

      Assert.Contains(relativePath, exception.Message);
      Assert.Contains("@42.public.pii", exception.Message);
      Assert.Contains("name[@<userId>[.pii][.public]].cgs", exception.Message);
   }

   private sealed class TempScriptFolder : IDisposable
   {
      public TempScriptFolder() => Root = Directory.CreateTempSubdirectory("cgscript-t2-").FullName;

      public string Root { get; }

      public string Write(string relativePath, string content)
      {
         var fullPath = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
         Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
         File.WriteAllText(fullPath, content);
         return fullPath;
      }

      public void Dispose() => Directory.Delete(Root, recursive: true);
   }
}
