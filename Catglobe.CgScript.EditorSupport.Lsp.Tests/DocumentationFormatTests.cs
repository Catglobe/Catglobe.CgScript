using Catglobe.CgScript.EditorSupport.Lsp.Definitions;
using Catglobe.CgScript.EditorSupport.Lsp.Handlers;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

/// <summary>
/// End-to-end tests verifying that <c>Documentation</c> fields in completion items
/// and signature-help responses use <see cref="MarkupKind.Markdown"/> when the client
/// declares support for it, and produce clean <see cref="MarkupKind.PlainText"/>
/// (with all markdown syntax stripped) when the client does not.
/// Covers all response types: function completions, member method/property completions,
/// function/method/constructor signature help.
/// </summary>
public class DocumentationFormatTests
{
   // ── Definition helpers ────────────────────────────────────────────────────

   // A simple old-style function with one parameter whose doc uses inline code.
   private static readonly FunctionDefinition OldStyleFn = new FunctionDefinition(
      Name: "myFunc",
      ReturnType: "string",
      NumberOfRequiredArguments: 1,
      Parameters:
      [
         new FunctionParam("x", false, "number", "", "", false, false, false),
      ],
      IsNewStyle: false,
      Variants: null);

   // A new-style function with two variants to exercise the "---" separator.
   private static readonly FunctionDefinition NewStyleFn = new FunctionDefinition(
      Name: "myNewFunc",
      ReturnType: "string",
      NumberOfRequiredArguments: 0,
      Parameters: null,
      IsNewStyle: true,
      Variants:
      [
         new FunctionVariant("myNewFunc", "First **variant** doc.",
            [new FunctionVariantParam("a", "Param **a**.", "number")], "string"),
         new FunctionVariant("myNewFunc", "Second variant doc.",
            [new FunctionVariantParam("b", "Param b.", "string")], "void"),
      ]);

   // A method and property on an object type.
   private static readonly MethodDefinition ObjMethod = new MethodDefinition(
      Name: "DoIt",
      Doc: "Does **something** important.",
      Param: [new MethodParam("n", "The `number`.", "number")],
      ReturnType: "void");

   private static readonly PropertyDefinition ObjProp = new PropertyDefinition(
      Name: "Count",
      Doc: "Returns the **count**.",
      HasGetter: true,
      HasSetter: false,
      ReturnType: "number");

   private static readonly MethodDefinition ObjCtor = new MethodDefinition(
      Name: "MyObj",
      Doc: "Creates a **new** MyObj.",
      Param: [new MethodParam("size", "Initial `size`.", "number")],
      ReturnType: "MyObj");

   private static readonly ObjectDefinition ObjDef = new ObjectDefinition(
      Name: "MyObj",
      Doc: "The **MyObj** type.",
      Constructors: [ObjCtor],
      Methods: [ObjMethod],
      StaticMethods: null,
      Properties: [ObjProp]);

   // ── Target factory helpers ─────────────────────────────────────────────────

   private sealed class TestLoader : DefinitionLoader
   {
      public TestLoader(
         Dictionary<string, FunctionDefinition>? functions = null,
         Dictionary<string, ObjectDefinition>?   objects   = null,
         Dictionary<string, string>?              globals   = null)
         : base(
            functions:       functions ?? [],
            objects:         objects   ?? [],
            constants:       [],
            globalVariables: globals   ?? [])
      { }
   }

   private static CgScriptLanguageTarget MakeTarget(
      TestLoader                loader,
      string                    source,
      string                    uri              = "file:///test.cgs",
      InitializeParams?         initializeParams = null)
   {
      var store = new DocumentStore(loader);
      store.Update(uri, source);
      var target = new CgScriptLanguageTarget(store, loader);
      target.Initialize(initializeParams);
      return target;
   }

   private static InitializeParams MarkdownCompletionParams() => new InitializeParams
   {
      Capabilities = new ClientCapabilities
      {
         TextDocument = new TextDocumentClientCapabilities
         {
            Completion = new CompletionSetting
            {
               CompletionItem = new CompletionItemSetting
               {
                  DocumentationFormat = [MarkupKind.Markdown],
               },
            },
         },
      },
   };

   private static InitializeParams BothCompletionParams() => new InitializeParams
   {
      Capabilities = new ClientCapabilities
      {
         TextDocument = new TextDocumentClientCapabilities
         {
            Completion = new CompletionSetting
            {
               CompletionItem = new CompletionItemSetting
               {
                  DocumentationFormat = [MarkupKind.PlainText, MarkupKind.Markdown],
               },
            },
         },
      },
   };

   private static InitializeParams MarkdownSignatureParams() => new InitializeParams
   {
      Capabilities = new ClientCapabilities
      {
         TextDocument = new TextDocumentClientCapabilities
         {
            SignatureHelp = new SignatureHelpSetting
            {
               SignatureInformation = new SignatureInformationSetting
               {
                  DocumentationFormat = [MarkupKind.Markdown],
               },
            },
         },
      },
   };

   private static CompletionItem[] GetCompletions(
      CgScriptLanguageTarget target,
      string                 uri,
      int                    column)
   {
      var p = new CompletionParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = new Uri(uri) },
         Position     = new Position(0, column),
      };
      var result = target.OnCompletion(p);
      if (result is null) return [];
      if (result.Value.TryGetFirst(out var arr))   return arr  ?? [];
      if (result.Value.TryGetSecond(out var list)) return list?.Items ?? [];
      return [];
   }

   private static MarkupContent? GetCompletionDoc(CompletionItem item)
   {
      if (!item.Documentation.HasValue) return null;
      return item.Documentation.Value.TryGetSecond(out var mc) ? mc : null;
   }

   private static MarkupContent? GetSignatureDoc(SignatureInformation sig)
   {
      if (!sig.Documentation.HasValue) return null;
      return sig.Documentation.Value.TryGetSecond(out var mc) ? mc : null;
   }

   private static MarkupContent? GetParamDoc(ParameterInformation param)
   {
      if (!param.Documentation.HasValue) return null;
      return param.Documentation.Value.TryGetSecond(out var mc) ? mc : null;
   }

   // ── Function completions ──────────────────────────────────────────────────

   [Fact]
   public void FunctionCompletion_PlainTextClient_KindIsPlainText()
   {
      const string uri = "file:///test.cgs";
      var loader = new TestLoader(functions: new() { ["myFunc"] = OldStyleFn });
      var target = MakeTarget(loader, "myFunc", uri);

      var item = GetCompletions(target, uri, 6).First(i => i.FilterText == "myFunc");
      var doc  = GetCompletionDoc(item);

      Assert.NotNull(doc);
      Assert.Equal(MarkupKind.PlainText, doc.Kind);
   }

   [Fact]
   public void FunctionCompletion_PlainTextClient_StripsBoldAndCode()
   {
      const string uri = "file:///test.cgs";
      var loader = new TestLoader(functions: new() { ["myFunc"] = OldStyleFn });
      var target = MakeTarget(loader, "myFunc", uri);

      var item  = GetCompletions(target, uri, 6).First(i => i.FilterText == "myFunc");
      var value = GetCompletionDoc(item)!.Value;

      // No raw markdown syntax in plain-text output.
      Assert.DoesNotContain("**", value);
      Assert.DoesNotContain("`", value);
      // Human-readable content is present.
      Assert.Contains("myFunc", value);
      Assert.Contains("Parameters:", value);
      Assert.Contains("number x", value);
   }

   [Fact]
   public void FunctionCompletion_MarkdownClient_KindIsMarkdown()
   {
      const string uri = "file:///test.cgs";
      var loader = new TestLoader(functions: new() { ["myFunc"] = OldStyleFn });
      var target = MakeTarget(loader, "myFunc", uri, MarkdownCompletionParams());

      var item = GetCompletions(target, uri, 6).First(i => i.FilterText == "myFunc");
      var doc  = GetCompletionDoc(item);

      Assert.NotNull(doc);
      Assert.Equal(MarkupKind.Markdown, doc.Kind);
   }

   [Fact]
   public void FunctionCompletion_MarkdownClient_PreservesMarkdown()
   {
      const string uri = "file:///test.cgs";
      var loader = new TestLoader(functions: new() { ["myFunc"] = OldStyleFn });
      var target = MakeTarget(loader, "myFunc", uri, MarkdownCompletionParams());

      var item  = GetCompletions(target, uri, 6).First(i => i.FilterText == "myFunc");
      var value = GetCompletionDoc(item)!.Value;

      Assert.Contains("`", value);
      Assert.Contains("**Parameters:**", value);
   }

   [Fact]
   public void FunctionCompletion_BothFormatsClient_PrefersMdAndGivesMarkdown()
   {
      // Client declares [PlainText, Markdown] — markdown is in the list so we send markdown.
      const string uri = "file:///test.cgs";
      var loader = new TestLoader(functions: new() { ["myFunc"] = OldStyleFn });
      var target = MakeTarget(loader, "myFunc", uri, BothCompletionParams());

      var item = GetCompletions(target, uri, 6).First(i => i.FilterText == "myFunc");
      var doc  = GetCompletionDoc(item);

      Assert.Equal(MarkupKind.Markdown, doc?.Kind);
   }

   // ── New-style function with two variants (separator "---") ─────────────────

   [Fact]
   public void FunctionCompletion_NewStyle_PlainTextClient_NoRawSeparator()
   {
      const string uri = "file:///test.cgs";
      var loader = new TestLoader(functions: new() { ["myNewFunc"] = NewStyleFn });
      var target = MakeTarget(loader, "myNewFunc", uri);

      var item  = GetCompletions(target, uri, 9).First(i => i.FilterText == "myNewFunc");
      var value = GetCompletionDoc(item)!.Value;

      // The markdown "---" separator must be gone; content must be readable.
      Assert.Equal(MarkupKind.PlainText, GetCompletionDoc(item)!.Kind);
      Assert.DoesNotContain("---", value);
      Assert.DoesNotContain("**", value);
      Assert.DoesNotContain("`", value);
      Assert.Contains("First variant doc.", value);
      Assert.Contains("Second variant doc.", value);
   }

   [Fact]
   public void FunctionCompletion_NewStyle_MarkdownClient_HasSeparator()
   {
      const string uri = "file:///test.cgs";
      var loader = new TestLoader(functions: new() { ["myNewFunc"] = NewStyleFn });
      var target = MakeTarget(loader, "myNewFunc", uri, MarkdownCompletionParams());

      var item  = GetCompletions(target, uri, 9).First(i => i.FilterText == "myNewFunc");
      var value = GetCompletionDoc(item)!.Value;

      Assert.Contains("---", value);
      Assert.Contains("**variant**", value);
   }

   // ── Member method completions ──────────────────────────────────────────────

   [Fact]
   public void MethodCompletion_PlainTextClient_KindIsPlainTextAndStripped()
   {
      const string uri    = "file:///test.cgs";
      const string source = "myObj.";
      var loader = new TestLoader(
         objects: new() { ["MyObj"] = ObjDef },
         globals: new() { ["myObj"] = "MyObj" });
      var target = MakeTarget(loader, source, uri);

      var item  = GetCompletions(target, uri, source.Length).FirstOrDefault(i => i.FilterText == "DoIt");
      var doc   = GetCompletionDoc(item!);

      Assert.NotNull(doc);
      Assert.Equal(MarkupKind.PlainText, doc.Kind);
      Assert.DoesNotContain("**", doc.Value);
      Assert.DoesNotContain("`", doc.Value);
      Assert.Contains("something important", doc.Value);
      Assert.Contains("number", doc.Value);    // param type
   }

   [Fact]
   public void MethodCompletion_MarkdownClient_KindIsMarkdownAndPreserved()
   {
      const string uri    = "file:///test.cgs";
      const string source = "myObj.";
      var loader = new TestLoader(
         objects: new() { ["MyObj"] = ObjDef },
         globals: new() { ["myObj"] = "MyObj" });
      var target = MakeTarget(loader, source, uri, MarkdownCompletionParams());

      var item  = GetCompletions(target, uri, source.Length).FirstOrDefault(i => i.FilterText == "DoIt");
      var doc   = GetCompletionDoc(item!);

      Assert.NotNull(doc);
      Assert.Equal(MarkupKind.Markdown, doc.Kind);
      Assert.Contains("**something**", doc.Value);
      Assert.Contains("`void DoIt", doc.Value);  // method signature in code span
   }

   // ── Member property completions ────────────────────────────────────────────

   [Fact]
   public void PropertyCompletion_PlainTextClient_KindIsPlainTextAndStripped()
   {
      const string uri    = "file:///test.cgs";
      const string source = "myObj.";
      var loader = new TestLoader(
         objects: new() { ["MyObj"] = ObjDef },
         globals: new() { ["myObj"] = "MyObj" });
      var target = MakeTarget(loader, source, uri);

      var item = GetCompletions(target, uri, source.Length).FirstOrDefault(i => i.Label == "Count");
      var doc  = GetCompletionDoc(item!);

      Assert.NotNull(doc);
      Assert.Equal(MarkupKind.PlainText, doc.Kind);
      Assert.DoesNotContain("**", doc.Value);
      Assert.DoesNotContain("`", doc.Value);
      Assert.Contains("count", doc.Value);
   }

   [Fact]
   public void PropertyCompletion_MarkdownClient_KindIsMarkdownAndPreserved()
   {
      const string uri    = "file:///test.cgs";
      const string source = "myObj.";
      var loader = new TestLoader(
         objects: new() { ["MyObj"] = ObjDef },
         globals: new() { ["myObj"] = "MyObj" });
      var target = MakeTarget(loader, source, uri, MarkdownCompletionParams());

      var item = GetCompletions(target, uri, source.Length).FirstOrDefault(i => i.Label == "Count");
      var doc  = GetCompletionDoc(item!);

      Assert.NotNull(doc);
      Assert.Equal(MarkupKind.Markdown, doc.Kind);
      Assert.Contains("**count**", doc.Value);
   }

   // ── Signature help — function ──────────────────────────────────────────────

   private static SignatureHelp? GetSignatureHelp(
      CgScriptLanguageTarget target, string uri, int column)
   {
      var p = new SignatureHelpParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = new Uri(uri) },
         Position     = new Position(0, column),
      };
      return target.OnSignatureHelp(p);
   }

   [Fact]
   public void FunctionSignatureHelp_PlainTextClient_SignatureDocIsPlainText()
   {
      const string uri    = "file:///test.cgs";
      const string source = "myFunc(";
      var loader = new TestLoader(functions: new() { ["myFunc"] = OldStyleFn });
      var target = MakeTarget(loader, source, uri);

      var sig  = GetSignatureHelp(target, uri, source.Length);
      var info = sig?.Signatures?.FirstOrDefault();
      var doc  = GetSignatureDoc(info!);

      Assert.NotNull(doc);
      Assert.Equal(MarkupKind.PlainText, doc.Kind);
      Assert.DoesNotContain("**", doc.Value);
      Assert.DoesNotContain("`", doc.Value);
   }

   [Fact]
   public void FunctionSignatureHelp_PlainTextClient_ParameterDocIsPlainText()
   {
      const string uri    = "file:///test.cgs";
      const string source = "myFunc(";
      var loader = new TestLoader(functions: new() { ["myFunc"] = OldStyleFn });
      var target = MakeTarget(loader, source, uri);

      var sig   = GetSignatureHelp(target, uri, source.Length);
      var info  = sig?.Signatures?.FirstOrDefault();
      var param = info?.Parameters?.FirstOrDefault();
      var doc   = GetParamDoc(param!);

      // Old-style param doc is "(optional)" or "" — no markdown to strip,
      // but the kind must be correct.
      Assert.NotNull(doc);
      Assert.Equal(MarkupKind.PlainText, doc.Kind);
   }

   [Fact]
   public void FunctionSignatureHelp_MarkdownClient_SignatureDocIsMarkdown()
   {
      const string uri    = "file:///test.cgs";
      const string source = "myFunc(";
      var loader = new TestLoader(functions: new() { ["myFunc"] = OldStyleFn });
      var target = MakeTarget(loader, source, uri, MarkdownSignatureParams());

      var sig  = GetSignatureHelp(target, uri, source.Length);
      var info = sig?.Signatures?.FirstOrDefault();
      var doc  = GetSignatureDoc(info!);

      Assert.NotNull(doc);
      Assert.Equal(MarkupKind.Markdown, doc.Kind);
      Assert.Contains("`", doc.Value);  // signature contains inline code
   }

   [Fact]
   public void FunctionSignatureHelp_NewStyleVariant_PlainTextClient_StripsMarkdown()
   {
      const string uri    = "file:///test.cgs";
      const string source = "myNewFunc(";
      var loader = new TestLoader(functions: new() { ["myNewFunc"] = NewStyleFn });
      var target = MakeTarget(loader, source, uri);

      var sig   = GetSignatureHelp(target, uri, source.Length);
      var info  = sig?.Signatures?.FirstOrDefault();
      var doc   = GetSignatureDoc(info!);
      var param = info?.Parameters?.FirstOrDefault();
      var pdoc  = GetParamDoc(param!);

      // Signature doc
      Assert.NotNull(doc);
      Assert.Equal(MarkupKind.PlainText, doc.Kind);
      Assert.DoesNotContain("**", doc.Value);
      Assert.Contains("variant", doc.Value);  // "First variant doc." stripped of bold

      // Parameter doc
      Assert.NotNull(pdoc);
      Assert.Equal(MarkupKind.PlainText, pdoc.Kind);
      Assert.DoesNotContain("**", pdoc.Value);
      Assert.Contains("Param", pdoc.Value);
   }

   [Fact]
   public void FunctionSignatureHelp_NewStyleVariant_MarkdownClient_PreservesMarkdown()
   {
      const string uri    = "file:///test.cgs";
      const string source = "myNewFunc(";
      var loader = new TestLoader(functions: new() { ["myNewFunc"] = NewStyleFn });
      var target = MakeTarget(loader, source, uri, MarkdownSignatureParams());

      var sig   = GetSignatureHelp(target, uri, source.Length);
      var info  = sig?.Signatures?.FirstOrDefault();
      var doc   = GetSignatureDoc(info!);
      var pdoc  = GetParamDoc(info?.Parameters?.FirstOrDefault()!);

      Assert.Equal(MarkupKind.Markdown, doc?.Kind);
      Assert.Contains("**variant**", doc?.Value);
      Assert.Equal(MarkupKind.Markdown, pdoc?.Kind);
      Assert.Contains("**a**", pdoc?.Value);
   }

   // ── Signature help — method ────────────────────────────────────────────────

   [Fact]
   public void MethodSignatureHelp_PlainTextClient_SignatureDocIsPlainText()
   {
      const string uri    = "file:///test.cgs";
      const string source = "myObj.DoIt(";
      var loader = new TestLoader(
         objects: new() { ["MyObj"] = ObjDef },
         globals: new() { ["myObj"] = "MyObj" });
      var target = MakeTarget(loader, source, uri);

      var sig  = GetSignatureHelp(target, uri, source.Length);
      var info = sig?.Signatures?.FirstOrDefault();
      var doc  = GetSignatureDoc(info!);

      Assert.NotNull(doc);
      Assert.Equal(MarkupKind.PlainText, doc.Kind);
      Assert.DoesNotContain("**", doc.Value);
      Assert.Contains("something important", doc.Value);
   }

   [Fact]
   public void MethodSignatureHelp_PlainTextClient_ParameterDocIsPlainText()
   {
      const string uri    = "file:///test.cgs";
      const string source = "myObj.DoIt(";
      var loader = new TestLoader(
         objects: new() { ["MyObj"] = ObjDef },
         globals: new() { ["myObj"] = "MyObj" });
      var target = MakeTarget(loader, source, uri);

      var sig   = GetSignatureHelp(target, uri, source.Length);
      var param = sig?.Signatures?.FirstOrDefault()?.Parameters?.FirstOrDefault();
      var pdoc  = GetParamDoc(param!);

      Assert.NotNull(pdoc);
      Assert.Equal(MarkupKind.PlainText, pdoc.Kind);
      Assert.DoesNotContain("`", pdoc.Value);
      Assert.Contains("number", pdoc.Value);  // "The `number`." → "The number."
   }

   [Fact]
   public void MethodSignatureHelp_MarkdownClient_SignatureDocIsMarkdown()
   {
      const string uri    = "file:///test.cgs";
      const string source = "myObj.DoIt(";
      var loader = new TestLoader(
         objects: new() { ["MyObj"] = ObjDef },
         globals: new() { ["myObj"] = "MyObj" });
      var target = MakeTarget(loader, source, uri, MarkdownSignatureParams());

      var sig  = GetSignatureHelp(target, uri, source.Length);
      var info = sig?.Signatures?.FirstOrDefault();
      var doc  = GetSignatureDoc(info!);

      Assert.NotNull(doc);
      Assert.Equal(MarkupKind.Markdown, doc.Kind);
      Assert.Contains("**something**", doc.Value);
   }

   [Fact]
   public void MethodSignatureHelp_MarkdownClient_ParameterDocIsMarkdown()
   {
      const string uri    = "file:///test.cgs";
      const string source = "myObj.DoIt(";
      var loader = new TestLoader(
         objects: new() { ["MyObj"] = ObjDef },
         globals: new() { ["myObj"] = "MyObj" });
      var target = MakeTarget(loader, source, uri, MarkdownSignatureParams());

      var param = GetSignatureHelp(target, uri, source.Length)
         ?.Signatures?.FirstOrDefault()?.Parameters?.FirstOrDefault();
      var pdoc = GetParamDoc(param!);

      Assert.NotNull(pdoc);
      Assert.Equal(MarkupKind.Markdown, pdoc.Kind);
      Assert.Contains("`number`", pdoc.Value);
   }

   // ── Signature help — constructor ───────────────────────────────────────────

   [Fact]
   public void ConstructorSignatureHelp_PlainTextClient_SignatureDocIsPlainText()
   {
      const string uri    = "file:///test.cgs";
      const string source = "new MyObj(";
      var loader = new TestLoader(objects: new() { ["MyObj"] = ObjDef });
      var target = MakeTarget(loader, source, uri);

      var sig  = GetSignatureHelp(target, uri, source.Length);
      var info = sig?.Signatures?.FirstOrDefault();
      var doc  = GetSignatureDoc(info!);

      Assert.NotNull(doc);
      Assert.Equal(MarkupKind.PlainText, doc.Kind);
      Assert.DoesNotContain("**", doc.Value);
      Assert.Contains("new", doc.Value.ToLower());
   }

   [Fact]
   public void ConstructorSignatureHelp_PlainTextClient_ParameterDocIsPlainText()
   {
      const string uri    = "file:///test.cgs";
      const string source = "new MyObj(";
      var loader = new TestLoader(objects: new() { ["MyObj"] = ObjDef });
      var target = MakeTarget(loader, source, uri);

      var param = GetSignatureHelp(target, uri, source.Length)
         ?.Signatures?.FirstOrDefault()?.Parameters?.FirstOrDefault();
      var pdoc = GetParamDoc(param!);

      Assert.NotNull(pdoc);
      Assert.Equal(MarkupKind.PlainText, pdoc.Kind);
      Assert.DoesNotContain("`", pdoc.Value);
      Assert.Contains("size", pdoc.Value);  // "Initial `size`." → "Initial size."
   }

   [Fact]
   public void ConstructorSignatureHelp_MarkdownClient_SignatureDocIsMarkdown()
   {
      const string uri    = "file:///test.cgs";
      const string source = "new MyObj(";
      var loader = new TestLoader(objects: new() { ["MyObj"] = ObjDef });
      var target = MakeTarget(loader, source, uri, MarkdownSignatureParams());

      var sig  = GetSignatureHelp(target, uri, source.Length);
      var info = sig?.Signatures?.FirstOrDefault();
      var doc  = GetSignatureDoc(info!);

      Assert.NotNull(doc);
      Assert.Equal(MarkupKind.Markdown, doc.Kind);
      Assert.Contains("**new**", doc.Value);
   }

   [Fact]
   public void ConstructorSignatureHelp_MarkdownClient_ParameterDocIsMarkdown()
   {
      const string uri    = "file:///test.cgs";
      const string source = "new MyObj(";
      var loader = new TestLoader(objects: new() { ["MyObj"] = ObjDef });
      var target = MakeTarget(loader, source, uri, MarkdownSignatureParams());

      var param = GetSignatureHelp(target, uri, source.Length)
         ?.Signatures?.FirstOrDefault()?.Parameters?.FirstOrDefault();
      var pdoc = GetParamDoc(param!);

      Assert.NotNull(pdoc);
      Assert.Equal(MarkupKind.Markdown, pdoc.Kind);
      Assert.Contains("`size`", pdoc.Value);
   }
}
