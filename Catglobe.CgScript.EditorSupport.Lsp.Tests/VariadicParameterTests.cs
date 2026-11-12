using Catglobe.CgScript.EditorSupport.Lsp.Handlers;
using Catglobe.CgScript.EditorSupport.Parsing;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

public class VariadicParameterTests
{
   private sealed class TestDefinitions : CgScriptDefinitions
   {
      private static readonly MethodOverload Mixed = new(
         "Accepts a typed prefix and arbitrary trailing values",
         [new("count", "A count", "number"), new("label", "A label", "string"), new("", Type: "Params object")],
         "Sample");

      public TestDefinitions() : base(
         new Dictionary<string, MethodOverload[]>
         {
            ["mixedFn"] = [Mixed],
            ["variadicOnly"] = [new(Param: [new("", Type: "Params object")])],
            ["fixedFn"] = [new(Param: [new("label", Type: "string")])],
         },
         new Dictionary<string, ObjectDefinition>
         {
            ["Sample"] = new("", Constructors: [Mixed],
               Methods: new() { ["Mix"] = [Mixed] },
               StaticMethods: new() { ["Create"] = [Mixed] }),
         }, new Dictionary<string, GlobalVariableDefinition>(), [], [])
      { }
   }

   [Theory]
   [InlineData("1, \"label\"", true)]
   [InlineData("1, \"label\", true", true)]
   [InlineData("1, \"label\", true, 2, \"text\", {}", true)]
   [InlineData("", false)]
   [InlineData("1", false)]
   [InlineData("\"wrong\", \"label\"", false)]
   [InlineData("1, false", false)]
   [InlineData("1, false, \"label\", 2", false)]
   public void TypedPrefix_ValidatesFunctionsMethodsAndConstructors(string arguments, bool valid)
   {
      var source = $"mixedFn({arguments}); Sample s; s.Mix({arguments}); new Sample({arguments});";
      var parsed = CgScriptParseService.Parse(source);
      Assert.Empty(parsed.Diagnostics);

      var diagnostics = SemanticAnalyzer.Analyze(parsed.Tree, new TestDefinitions());
      if (valid)
         Assert.Empty(diagnostics);
      else
      {
         Assert.Equal(3, diagnostics.Count);
         Assert.Single(diagnostics, d => d.Code == "CGS022");
         Assert.Single(diagnostics, d => d.Code == "CGS023");
         Assert.Single(diagnostics, d => d.Code == "CGS024");
      }
   }

   [Theory]
   [InlineData("")]
   [InlineData("1")]
   [InlineData("1, true, \"text\", {}")]
   public void VariadicOnly_AcceptsAnyArgumentCount(string arguments)
   {
      var parsed = CgScriptParseService.Parse($"variadicOnly({arguments});");
      Assert.Empty(parsed.Diagnostics);
      Assert.Empty(SemanticAnalyzer.Analyze(parsed.Tree, new TestDefinitions()));
   }

   [Theory]
   [InlineData("mixedFn(", "Sample mixedFn")]
   [InlineData("Sample s; s.Mix(", "Sample Mix")]
   [InlineData("Sample.Create(", "Sample Create")]
   [InlineData("new Sample(", "new Sample")]
   public void SignatureHelp_HighlightsTypedPrefixThenVariadicTail(string call, string label)
   {
      string[] arguments = ["", "1, ", "1, \"label\", ", "1, \"label\", true, ", "1, \"label\", true, 2, "];
      for (var argumentIndex = 0; argumentIndex < arguments.Length; argumentIndex++)
      {
         var help = GetSignatureHelp(call + arguments[argumentIndex], new TestDefinitions());
         Assert.NotNull(help);
         var signature = Assert.Single(help.Signatures);
         Assert.Equal(label + "(number count, string label, Params object )", signature.Label);
         Assert.NotNull(signature.Parameters);
         Assert.Equal(3, signature.Parameters.Length);
         Assert.Equal(Math.Min(argumentIndex, 2), help.ActiveParameter);
      }
   }

   [Theory]
   [InlineData("variadicOnly(")]
   [InlineData("variadicOnly(1, ")]
   [InlineData("variadicOnly(1, true, ")]
   public void SignatureHelp_VariadicOnly_KeepsTailActive(string source)
   {
      var help = GetSignatureHelp(source, new TestDefinitions());
      Assert.NotNull(help);
      Assert.Equal(0, help.ActiveParameter);
      Assert.Single(help.Signatures);
   }

   [Theory]
   [InlineData("fixedFn(", 0)]
   [InlineData("fixedFn(\"label\", ", 1)]
   public void SignatureHelp_NonVariadic_PreservesArgumentIndex(string source, int expected)
   {
      var help = GetSignatureHelp(source, new TestDefinitions());
      Assert.NotNull(help);
      Assert.Equal(expected, help.ActiveParameter);
   }

   [Theory]
   [InlineData("formatToUser(\"text\");", true)]
   [InlineData("formatToUser(\"{0} {1}\", 1, true);", true)]
   [InlineData("User u; formatToUser(u, \"text\");", true)]
   [InlineData("User u; formatToUser(u, \"{0} {1}\", 1, true);", true)]
   [InlineData("formatToUser(empty, \"{0}\", 1);", true)]
   [InlineData("formatToUser();", false)]
   [InlineData("formatToUser(42, \"{0}\", 1);", false)]
   [InlineData("User u; formatToUser(u);", false)]
   [InlineData("User u; formatToUser(u, 42, \"text\");", false)]
   public void FormatToUser_BundledDefinitions_ValidateBothOverloads(string source, bool valid)
   {
      var parsed = CgScriptParseService.Parse(source);
      Assert.Empty(parsed.Diagnostics);
      var diagnostics = SemanticAnalyzer.Analyze(parsed.Tree, new CgScriptDefinitions());
      if (valid)
         Assert.Empty(diagnostics);
      else
         Assert.Equal("CGS022", Assert.Single(diagnostics).Code);
   }

   [Fact]
   public void FormatToUser_SignatureHelp_PreservesEachOverloadsPrefixAndTail()
   {
      var help = GetSignatureHelp("formatToUser(\"{0} {1}\", 1, true, ", new CgScriptDefinitions());
      Assert.NotNull(help);
      Assert.Equal(2, help.Signatures.Length);
      Assert.Contains(help.Signatures, s => s.Label == "string formatToUser(User user, string format, Params object )");
      Assert.Contains(help.Signatures, s => s.Label == "string formatToUser(string format, Params object )");
      var active = help.Signatures[help.ActiveSignature ?? 0];
      Assert.NotNull(active.Parameters);
      Assert.Equal(active.Parameters.Length - 1, help.ActiveParameter);
   }

   private static SignatureHelp? GetSignatureHelp(string source, CgScriptDefinitions definitions)
   {
      const string uri = "file:///variadic.cgs";
      var store = new DocumentStore(definitions);
      store.Update(uri, source);
      var target = new CgScriptLanguageTarget(store, definitions);
      return target.OnSignatureHelp(new SignatureHelpParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = new Uri(uri) },
         Position = new Position(0, source.Length),
      });
   }
}
