using Catglobe.CgScript.Deployment;
using Microsoft.Extensions.Options;
using Xunit;

namespace Catglobe.CgScript.Deployment.Tests;

public class FilesFromDirectoryScriptProviderTests
{
   [Fact]
   public async Task GetAllReadsNamesWithoutMarkersFromDisk()
   {
      using var folder = new TempScriptFolder();
      folder.Write("Plain.cgs", "return 1;");
      folder.Write("Impersonated@123.cgs", "return 1;");
      folder.Write("Public@123.public.cgs", "return 1;");
      folder.Write("Dir/File.cgs", "return 1;");

      var scripts = await CreateProvider(folder.Root).GetAll();

      Assert.Equal(4, scripts.Count);
      Assert.Equal("Plain", scripts["Plain"].ScriptName);
      Assert.Null(scripts["Plain"].Impersonation);
      Assert.False(scripts["Plain"].AllowExecuteWithoutLogin);
      Assert.Equal((uint?)123, scripts["Impersonated"].Impersonation);
      Assert.False(scripts["Impersonated"].AllowExecuteWithoutLogin);
      Assert.Equal((uint?)123, scripts["Public"].Impersonation);
      Assert.True(scripts["Public"].AllowExecuteWithoutLogin);
      Assert.Equal("Dir/File", scripts["Dir/File"].ScriptName);
      Assert.Null(scripts["Dir/File"].Impersonation);
   }

   [Fact]
   public async Task GetAllReportsDuplicateScriptNamesWithBothFiles()
   {
      using var folder = new TempScriptFolder();
      folder.Write("A.cgs", "return 1;");
      folder.Write("A@1.cgs", "return 1;");

      var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateProvider(folder.Root).GetAll());

      Assert.Contains("'A'", exception.Message);
      Assert.Contains("A.cgs", exception.Message);
      Assert.Contains("A@1.cgs", exception.Message);
   }

   [Fact]
   public async Task GetAllCarriesThePiiMarker()
   {
      using var folder = new TempScriptFolder();
      folder.Write("Plain.cgs", "return 1;");
      folder.Write("Pii@1.pii.cgs", "return 1;");

      var scripts = await CreateProvider(folder.Root).GetAll();

      Assert.False(scripts["Plain"].CanAccessPII);
      Assert.Equal((uint?)1, scripts["Pii"].Impersonation);
      Assert.True(scripts["Pii"].CanAccessPII);
   }

   private static FilesFromDirectoryScriptProvider CreateProvider(string scriptFolder) =>
      new(Options.Create(new DeploymentOptions { ScriptFolder = scriptFolder }));

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
