extern alias generator;

using System.Text;
using CgScriptWrapperGenerator = generator::Catglobe.CgScript.EditorSupport.SourceGenerator.CgScriptWrapperGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Catglobe.CgScript.Deployment.Tests;

/// <summary>
/// Drives <see cref="CgScriptWrapperGenerator"/> with a Roslyn <see cref="CSharpGeneratorDriver"/>
/// and asserts that the wrapper name is derived through the shared filename grammar.
/// </summary>
public class CgScriptWrapperGeneratorFileNameTests
{
   /// <summary>A minimal [CgScriptSerializer] context plus a stub of the marker attribute.</summary>
   private const string SerializerContextSource = """
      namespace Catglobe.CgScript
      {
         [System.AttributeUsage(System.AttributeTargets.Class)]
         public sealed class CgScriptSerializerAttribute : System.Attribute { }
      }

      namespace TestApp
      {
         [Catglobe.CgScript.CgScriptSerializer]
         public partial class TestCgScriptSerializerContext { }
      }
      """;

   /// <summary>A minimal script whose parameters parse without any annotation diagnostics.</summary>
   private const string SimpleScriptSource = """
      Dictionary namedParameters = Workflow_getParameters()[0];
      string city = namedParameters["city"];
      return city;
      """;

   [Fact]
   public void PiiMarkedFileName_EmitsWrapperWithScriptNameWithoutMetadata()
   {
      var runResult = RunGenerator(new InMemoryAdditionalText("Foo@42.pii.cgs", SimpleScriptSource));

      var generated = Assert.Single(runResult.Results[0].GeneratedSources);
      var source = generated.SourceText.ToString();
      Assert.Contains("\"Foo\"", source);
      Assert.DoesNotContain("Foo@42", source);
   }

   [Fact]
   public void MalformedFileName_ReportsCgs028AndEmitsNothing()
   {
      var additionalText = new InMemoryAdditionalText("Foo@42.public.pii.cgs", SimpleScriptSource);
      var runResult = RunGenerator(additionalText);

      var diagnostic = Assert.Single(runResult.Diagnostics);
      Assert.Equal("CGS028", diagnostic.Id);
      Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
      Assert.Equal(additionalText.Path, diagnostic.Location.GetLineSpan().Path);
      Assert.Contains("@42.public.pii", diagnostic.GetMessage());
      Assert.Contains("name[@<userId>[.pii][.public]].cgs", diagnostic.GetMessage());
      Assert.Empty(runResult.Results[0].GeneratedSources);
   }

   [Fact]
   public void MetadataFreeFileName_EmitsWrapperWithScriptName()
   {
      var runResult = RunGenerator(new InMemoryAdditionalText("Weather.cgs", SimpleScriptSource));

      var generated = Assert.Single(runResult.Results[0].GeneratedSources);
      Assert.Contains("\"Weather\"", generated.SourceText.ToString());
      Assert.Empty(runResult.Diagnostics);
   }

   private static GeneratorDriverRunResult RunGenerator(params AdditionalText[] additionalTexts)
   {
      var compilation = CSharpCompilation.Create(
         assemblyName: "CgScriptWrapperGeneratorFileNameTests",
         syntaxTrees: new[] { CSharpSyntaxTree.ParseText(SerializerContextSource) },
         references: ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p)),
         options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

      var driver = CSharpGeneratorDriver.Create(
         generators: new ISourceGenerator[] { new CgScriptWrapperGenerator().AsSourceGenerator() },
         additionalTexts: additionalTexts);

      return driver.RunGenerators(compilation).GetRunResult();
   }

   private sealed class InMemoryAdditionalText : AdditionalText
   {
      private readonly SourceText _text;

      public InMemoryAdditionalText(string path, string text)
      {
         Path = path;
         _text = SourceText.From(text, Encoding.UTF8);
      }

      public override string Path { get; }

      public override SourceText? GetText(CancellationToken cancellationToken = default) => _text;
   }
}
