using System.IO;
using System.Linq;
using Catglobe.CgScript.EditorSupport.Lsp.Handlers;
using Catglobe.CgScript.EditorSupport.Parsing;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using ParseDiagnosticSeverity = Catglobe.CgScript.EditorSupport.Parsing.DiagnosticSeverity;

namespace Catglobe.CgScript.EditorSupport.Lsp.Tests;

/// <summary>
/// Exercises both parse-level correctness and LSP features (hover, definition, references, rename)
/// against real-world QSL sample files, plus inline cross-reference scenarios.
/// </summary>
public class QslRealWorldTests
{
   // NOTE: "Questionnaire\u00A0Demographic.qsl" uses a non-breaking space (U+00A0).
   private const string DemographicFile  = "Questionnaire\u00A0Demographic.qsl";
   private const string PanelBusFile     = "Qnaire panel bus template.qsl";

   private static readonly string QslSamplesDir =
      Path.Combine(
         Path.GetDirectoryName(typeof(QslRealWorldTests).Assembly.Location)!,
         "..", "..", "..", "..",
         "demos", "BlazorWebApp", "BlazorWebApp", "Qsl");

   // ── Infrastructure ────────────────────────────────────────────────────────

   private static string ReadFile(string fileName) =>
      File.ReadAllText(Path.GetFullPath(Path.Combine(QslSamplesDir, fileName)));

   private static (QslLanguageTarget Target, Uri Uri, QslAnalysis Analysis) LoadTarget(string fileName)
   {
      var text     = ReadFile(fileName);
      var (_, analysis) = QslParseService.ParseAndAnalyze(text);
      var uri      = new Uri($"file:///qsl-test/{Uri.EscapeDataString(fileName)}");
      var target   = new QslLanguageTarget();
      target.OnDidOpen(new DidOpenTextDocumentParams
      {
         TextDocument = new TextDocumentItem
         {
            Uri        = uri,
            Text       = text,
            LanguageId = "qsl",
            Version    = 1,
         },
      });
      return (target, uri, analysis);
   }

   /// <summary>Returns the LSP Position (0-based line) of a label's definition token.</summary>
   private static Position SymbolPos(QslAnalysis analysis, string label)
   {
      var r = analysis.LabelRefs
         .First(r => string.Equals(r.Label, label, StringComparison.OrdinalIgnoreCase) && r.IsDefinition);
      return new Position(r.Line - 1, r.Column);   // ANTLR is 1-based; LSP is 0-based
   }

   private static string? HoverMarkdown(Hover? hover) =>
      hover?.Contents.TryGetThird(out var m) == true ? m?.Value : null;

   // ── Panel Bus Template — parse ────────────────────────────────────────────

   [Fact]
   public void PanelBusTemplate_HasNoSyntaxErrors()
   {
      var (parse, _) = QslParseService.ParseAndAnalyze(ReadFile(PanelBusFile));
      Assert.DoesNotContain(parse.Diagnostics, d => d.Severity == ParseDiagnosticSeverity.Error);
   }

   [Fact]
   public void PanelBusTemplate_FindsQuestions()
   {
      var (_, analysis) = QslParseService.ParseAndAnalyze(ReadFile(PanelBusFile));
      Assert.Contains(analysis.Symbols.Values, s => s.Kind == "question");
   }

   [Fact]
   public void PanelBusTemplate_NoUnknownProperties()
   {
      var (_, analysis) = QslParseService.ParseAndAnalyze(ReadFile(PanelBusFile));
      Assert.DoesNotContain(analysis.Diagnostics, d => d.Code == "QSL003");
   }

   [Fact]
   public void PanelBusTemplate_NoDuplicateLabels()
   {
      var (_, analysis) = QslParseService.ParseAndAnalyze(ReadFile(PanelBusFile));
      Assert.DoesNotContain(analysis.Diagnostics, d => d.Code == "QSL004");
   }

   // ── Demographic — parse ───────────────────────────────────────────────────

   [Fact]
   public void Demographic_HasNoSyntaxErrors()
   {
      var (parse, _) = QslParseService.ParseAndAnalyze(ReadFile(DemographicFile));
      Assert.DoesNotContain(parse.Diagnostics, d => d.Severity == ParseDiagnosticSeverity.Error);
   }

   [Fact]
   public void Demographic_FindsQuestions()
   {
      var (_, analysis) = QslParseService.ParseAndAnalyze(ReadFile(DemographicFile));
      Assert.Contains(analysis.Symbols.Values, s => s.Kind == "question");
   }

   [Fact]
   public void Demographic_NoUnknownProperties()
   {
      var (_, analysis) = QslParseService.ParseAndAnalyze(ReadFile(DemographicFile));
      Assert.DoesNotContain(analysis.Diagnostics, d => d.Code == "QSL003");
   }

   [Fact]
   public void Demographic_NoDuplicateLabels()
   {
      var (_, analysis) = QslParseService.ParseAndAnalyze(ReadFile(DemographicFile));
      Assert.DoesNotContain(analysis.Diagnostics, d => d.Code == "QSL004");
   }

   // ── Hover — real files ────────────────────────────────────────────────────

   [Fact]
   public void PanelBus_Hover_AtQuestionLabel_ShowsTypeInfo()
   {
      var (target, uri, analysis) = LoadTarget(PanelBusFile);
      var pos = SymbolPos(analysis, "CHECK_SCREENING");

      var md = HoverMarkdown(target.OnHover(new TextDocumentPositionParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = uri },
         Position     = pos,
      }));

      Assert.NotNull(md);
      Assert.Contains("CHECK_SCREENING", md);
      Assert.Contains("PAGE", md);
   }

   [Fact]
   public void PanelBus_Hover_AtGroupLabel_ShowsGroupInfo()
   {
      var (target, uri, analysis) = LoadTarget(PanelBusFile);
      var pos = SymbolPos(analysis, "G_Intro");

      var md = HoverMarkdown(target.OnHover(new TextDocumentPositionParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = uri },
         Position     = pos,
      }));

      Assert.NotNull(md);
      Assert.Contains("G_Intro", md);
      Assert.Contains("GROUP", md);
   }

   [Fact]
   public void Demographic_Hover_AtQuestionLabel_ShowsTypeInfo()
   {
      var (target, uri, analysis) = LoadTarget(DemographicFile);
      var pos = SymbolPos(analysis, "Gender");

      var md = HoverMarkdown(target.OnHover(new TextDocumentPositionParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = uri },
         Position     = pos,
      }));

      Assert.NotNull(md);
      Assert.Contains("Gender", md);
      Assert.Contains("SINGLE", md);
   }

   [Fact]
   public void Demographic_Hover_AtUnknownPosition_ReturnsNull()
   {
      var (target, uri, _) = LoadTarget(DemographicFile);

      var result = target.OnHover(new TextDocumentPositionParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = uri },
         Position     = new Position(0, 0),   // before any label
      });

      Assert.Null(result);
   }

   // ── Definition — real files ───────────────────────────────────────────────

   [Fact]
   public void PanelBus_Definition_AtLabel_ReturnsSameLocation()
   {
      var (target, uri, analysis) = LoadTarget(PanelBusFile);
      var pos = SymbolPos(analysis, "D_GivePoints");

      var result = target.OnDefinition(new TextDocumentPositionParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = uri },
         Position     = pos,
      });

      Assert.NotNull(result);
      Assert.True(result.Value.TryGetFirst(out var loc));
      Assert.Equal(pos.Line,      loc.Range.Start.Line);
      Assert.Equal(pos.Character, loc.Range.Start.Character);
   }

   [Fact]
   public void Demographic_Definition_AtLabel_ReturnsSameLocation()
   {
      var (target, uri, analysis) = LoadTarget(DemographicFile);
      var pos = SymbolPos(analysis, "Age");

      var result = target.OnDefinition(new TextDocumentPositionParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = uri },
         Position     = pos,
      });

      Assert.NotNull(result);
      Assert.True(result.Value.TryGetFirst(out var loc));
      Assert.Equal(pos.Line,      loc.Range.Start.Line);
      Assert.Equal(pos.Character, loc.Range.Start.Character);
   }

   // ── References — real files ───────────────────────────────────────────────

   [Fact]
   public void PanelBus_References_AtLabel_IncludesDefinition()
   {
      var (target, uri, analysis) = LoadTarget(PanelBusFile);
      var pos = SymbolPos(analysis, "README");

      var result = target.OnReferences(new ReferenceParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = uri },
         Position     = pos,
         Context      = new ReferenceContext { IncludeDeclaration = true },
      });

      Assert.NotNull(result);
      Assert.NotEmpty(result);
      Assert.Contains(result, loc =>
         loc.Range.Start.Line == pos.Line && loc.Range.Start.Character == pos.Character);
   }

   [Fact]
   public void Demographic_References_AtLabel_IncludesDefinition()
   {
      var (target, uri, analysis) = LoadTarget(DemographicFile);
      var pos = SymbolPos(analysis, "ZipCode");

      var result = target.OnReferences(new ReferenceParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = uri },
         Position     = pos,
         Context      = new ReferenceContext { IncludeDeclaration = true },
      });

      Assert.NotNull(result);
      Assert.NotEmpty(result);
      Assert.Contains(result, loc =>
         loc.Range.Start.Line == pos.Line && loc.Range.Start.Character == pos.Character);
   }

   // ── Rename — real files ───────────────────────────────────────────────────

   [Fact]
   public void PanelBus_Rename_GeneratesEditsForAllOccurrences()
   {
      var (target, uri, analysis) = LoadTarget(PanelBusFile);
      var pos = SymbolPos(analysis, "D_Speeders_Start");

      var result = target.OnRename(new RenameParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = uri },
         Position     = pos,
         NewName      = "D_Speeders_Begin",
      });

      Assert.NotNull(result);
      Assert.NotNull(result.Changes);
      var edits = result.Changes.Values.SelectMany(e => e).ToList();
      Assert.NotEmpty(edits);
      Assert.All(edits, e => Assert.Equal("D_Speeders_Begin", e.NewText));
   }

   [Fact]
   public void Demographic_PrepareRename_AtLabel_ReturnsCorrectRange()
   {
      var (target, uri, analysis) = LoadTarget(DemographicFile);
      var pos = SymbolPos(analysis, "Civil_Status");

      var range = target.OnPrepareRename(new TextDocumentPositionParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = uri },
         Position     = pos,
      });

      Assert.NotNull(range);
      Assert.Equal(pos.Line,      range.Start.Line);
      Assert.Equal(pos.Character, range.Start.Character);
      Assert.Equal("Civil_Status".Length, range.End.Character - range.Start.Character);
   }

   // ── Document Symbols — real files ────────────────────────────────────────

   [Fact]
   public void PanelBus_DocumentSymbols_ContainsKnownLabels()
   {
      var (target, uri, _) = LoadTarget(PanelBusFile);

      var result = target.OnDocumentSymbol(new DocumentSymbolParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = uri },
      });

      Assert.NotNull(result);
      Assert.NotEmpty(result);
      var names = result.Select(s => s.Name).ToList();
      Assert.Contains(names, n => n.Contains("CHECK_SCREENING"));
      Assert.Contains(names, n => n.Contains("D_GivePoints"));
   }

   [Fact]
   public void Demographic_DocumentSymbols_ContainsManyQuestions()
   {
      var (target, uri, _) = LoadTarget(DemographicFile);

      var result = target.OnDocumentSymbol(new DocumentSymbolParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = uri },
      });

      Assert.NotNull(result);
      Assert.True(result.Length > 10, $"Expected >10 symbols, got {result.Length}");
   }

   // ── Cross-reference inline scenario ──────────────────────────────────────

   // Inline QSL with a GOTO that references Q1, to exercise multi-ref features.
   // No empty [] blocks — qproperties is optional and the preprocessor expects
   // multi-line [/] blocks (real QSL format); empty inline [] confuses it.
   private const string CrossRefQsl = """
      QUESTIONNAIRE
      QUESTION Q1 PAGE
      Page one
      GOTO Q1
      QUESTION Q2 SINGLE
      Choose one
      1:Option A
      2:Option B
      """;

   private static (QslLanguageTarget Target, Uri Uri, QslAnalysis Analysis) LoadCrossRefTarget()
   {
      var uri    = new Uri("file:///inline-crossref.qsl");
      var target = new QslLanguageTarget();
      target.OnDidOpen(new DidOpenTextDocumentParams
      {
         TextDocument = new TextDocumentItem { Uri = uri, Text = CrossRefQsl, LanguageId = "qsl", Version = 1 },
      });
      var (_, analysis) = QslParseService.ParseAndAnalyze(CrossRefQsl);
      return (target, uri, analysis);
   }

   [Fact]
   public void CrossRef_Hover_AtGotoRef_ShowsLabelInfo()
   {
      var (target, uri, analysis) = LoadCrossRefTarget();
      var gotoRef = analysis.LabelRefs
         .First(r => string.Equals(r.Label, "Q1", StringComparison.OrdinalIgnoreCase) && !r.IsDefinition);

      var md = HoverMarkdown(target.OnHover(new TextDocumentPositionParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = uri },
         Position     = new Position(gotoRef.Line - 1, gotoRef.Column),
      }));

      Assert.NotNull(md);
      Assert.Contains("Q1", md);
   }

   [Fact]
   public void CrossRef_Definition_FromGotoRef_JumpsToDefinitionLine()
   {
      var (target, uri, analysis) = LoadCrossRefTarget();
      var gotoRef = analysis.LabelRefs
         .First(r => string.Equals(r.Label, "Q1", StringComparison.OrdinalIgnoreCase) && !r.IsDefinition);
      var defRef = analysis.LabelRefs
         .First(r => string.Equals(r.Label, "Q1", StringComparison.OrdinalIgnoreCase) && r.IsDefinition);

      var result = target.OnDefinition(new TextDocumentPositionParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = uri },
         Position     = new Position(gotoRef.Line - 1, gotoRef.Column),
      });

      Assert.NotNull(result);
      Assert.True(result.Value.TryGetFirst(out var loc));
      Assert.Equal(defRef.Line - 1, loc.Range.Start.Line);
      Assert.Equal(defRef.Column,   loc.Range.Start.Character);
   }

   [Fact]
   public void CrossRef_References_ReturnsBothDefinitionAndGoto()
   {
      var (target, uri, analysis) = LoadCrossRefTarget();
      var defRef = analysis.LabelRefs
         .First(r => string.Equals(r.Label, "Q1", StringComparison.OrdinalIgnoreCase) && r.IsDefinition);

      var result = target.OnReferences(new ReferenceParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = uri },
         Position     = new Position(defRef.Line - 1, defRef.Column),
         Context      = new ReferenceContext { IncludeDeclaration = true },
      });

      Assert.NotNull(result);
      Assert.Equal(2, result.Length);   // definition + GOTO reference
   }

   [Fact]
   public void CrossRef_References_ExcludesDeclarationWhenRequested()
   {
      var (target, uri, analysis) = LoadCrossRefTarget();
      var defRef = analysis.LabelRefs
         .First(r => string.Equals(r.Label, "Q1", StringComparison.OrdinalIgnoreCase) && r.IsDefinition);

      var result = target.OnReferences(new ReferenceParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = uri },
         Position     = new Position(defRef.Line - 1, defRef.Column),
         Context      = new ReferenceContext { IncludeDeclaration = false },
      });

      Assert.NotNull(result);
      Assert.Single(result);   // GOTO only
      Assert.All(result, loc => Assert.NotEqual(defRef.Line - 1, loc.Range.Start.Line));
   }

   [Fact]
   public void CrossRef_Rename_UpdatesDefinitionAndGoto()
   {
      var (target, uri, analysis) = LoadCrossRefTarget();
      var defRef = analysis.LabelRefs
         .First(r => string.Equals(r.Label, "Q1", StringComparison.OrdinalIgnoreCase) && r.IsDefinition);

      var result = target.OnRename(new RenameParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = uri },
         Position     = new Position(defRef.Line - 1, defRef.Column),
         NewName      = "Intro",
      });

      Assert.NotNull(result);
      var edits = result.Changes!.Values.SelectMany(e => e).ToList();
      Assert.Equal(2, edits.Count);   // definition + GOTO
      Assert.All(edits, e => Assert.Equal("Intro", e.NewText));
   }

   [Fact]
   public void CrossRef_DocumentHighlight_MarksDefinitionAndGoto()
   {
      var (target, uri, analysis) = LoadCrossRefTarget();
      var defRef = analysis.LabelRefs
         .First(r => string.Equals(r.Label, "Q1", StringComparison.OrdinalIgnoreCase) && r.IsDefinition);

      var result = target.OnDocumentHighlight(new DocumentHighlightParams
      {
         TextDocument = new TextDocumentIdentifier { Uri = uri },
         Position     = new Position(defRef.Line - 1, defRef.Column),
      });

      Assert.NotNull(result);
      Assert.Equal(2, result.Length);
      Assert.Contains(result, h => h.Kind == DocumentHighlightKind.Write);    // definition
      Assert.Contains(result, h => h.Kind == DocumentHighlightKind.Read);     // GOTO
   }

   // ── QSL007 — unnecessary quotes ──────────────────────────────────────────

   [Fact]
   public void QuotedQtext_EmitsQsl007()
   {
      const string qsl = """
         QUESTION Q1 PAGE
         "Already quoted question text"
         """;
      var (_, analysis) = QslParseService.ParseAndAnalyze(qsl);
      Assert.Contains(analysis.Diagnostics, d => d.Code == "QSL007");
   }

   [Fact]
   public void UnquotedQtext_NoQsl007()
   {
      const string qsl = """
         QUESTION Q1 PAGE
         Plain question text
         """;
      var (_, analysis) = QslParseService.ParseAndAnalyze(qsl);
      Assert.DoesNotContain(analysis.Diagnostics, d => d.Code == "QSL007");
   }

   [Fact]
   public void QuotedAoText_EmitsQsl007()
   {
      const string qsl = """
         QUESTION Q1 SINGLE
         Choose one
         1: "Danmark"
         2: "Sverige"
         """;
      var (_, analysis) = QslParseService.ParseAndAnalyze(qsl);
      Assert.Equal(2, analysis.Diagnostics.Count(d => d.Code == "QSL007"));
   }

   [Fact]
   public void UnquotedAoText_NoQsl007()
   {
      const string qsl = """
         QUESTION Q1 SINGLE
         Choose one
         1: Danmark
         2: Sverige
         """;
      var (_, analysis) = QslParseService.ParseAndAnalyze(qsl);
      Assert.DoesNotContain(analysis.Diagnostics, d => d.Code == "QSL007");
   }

   [Fact]
   public void QuotedSqText_EmitsQsl007()
   {
      const string qsl = """
         QUESTION Q1 SINGLEGRID
         Rate items
         SQ: "Row one"
         SQ: "Row two"
         1: Low
         2: High
         """;
      var (_, analysis) = QslParseService.ParseAndAnalyze(qsl);
      Assert.Equal(2, analysis.Diagnostics.Count(d => d.Code == "QSL007"));
   }

   [Fact]
   public void UnquotedSqText_NoQsl007()
   {
      const string qsl = """
         QUESTION Q1 SINGLEGRID
         Rate items
         SQ: Row one
         SQ: Row two
         1: Low
         2: High
         """;
      var (_, analysis) = QslParseService.ParseAndAnalyze(qsl);
      Assert.DoesNotContain(analysis.Diagnostics, d => d.Code == "QSL007");
   }

   [Fact]
   public void RealWorldFiles_NoQsl007()
   {
      // The real-world QSL samples are hand-authored and should not use quoted text.
      var (_, dem)  = QslParseService.ParseAndAnalyze(ReadFile(DemographicFile));
      var (_, bus)  = QslParseService.ParseAndAnalyze(ReadFile(PanelBusFile));
      Assert.DoesNotContain(dem.Diagnostics,  d => d.Code == "QSL007");
      Assert.DoesNotContain(bus.Diagnostics,  d => d.Code == "QSL007");
   }

   // ── QSL007 — unnecessarily-quoted conditions ──────────────────────────────

   [Fact]
   public void QuotedIfCondition_EmitsQsl007()
   {
      // Wrong: IF ("expr") — condition expression already quoted inside the parens.
      const string qsl = """
         QUESTION Q1 SINGLE
         IF ("Q_MANY_AO == [1]")
         Choose one
         1: A
         2: B
         """;
      var (_, analysis) = QslParseService.ParseAndAnalyze(qsl);
      Assert.Contains(analysis.Diagnostics, d => d.Code == "QSL007");
   }

   [Fact]
   public void UnquotedIfCondition_NoQsl007()
   {
      // Correct: IF (expr)
      const string qsl = """
         QUESTION Q1 SINGLE
         IF (Q_MANY_AO == [1])
         Choose one
         1: A
         2: B
         """;
      var (_, analysis) = QslParseService.ParseAndAnalyze(qsl);
      Assert.DoesNotContain(analysis.Diagnostics, d => d.Code == "QSL007");
   }

   [Fact]
   public void QuotedGotoCondition_EmitsQsl007()
   {
      // Wrong: GOTO Q2 IF "expr" — already quoted without the outer parens.
      const string qsl = """
         QUESTION Q1 PAGE
         Page one
         GOTO Q2 IF "Q_MANY_AO == [1]"
         QUESTION Q2 PAGE
         Page two
         """;
      var (_, analysis) = QslParseService.ParseAndAnalyze(qsl);
      Assert.Contains(analysis.Diagnostics, d => d.Code == "QSL007");
   }

   [Fact]
   public void UnquotedGotoCondition_NoQsl007()
   {
      // Correct: GOTO Q2 IF (expr)
      const string qsl = """
         QUESTION Q1 PAGE
         Page one
         GOTO Q2 IF (Q_MANY_AO == [1])
         QUESTION Q2 PAGE
         Page two
         """;
      var (_, analysis) = QslParseService.ParseAndAnalyze(qsl);
      Assert.DoesNotContain(analysis.Diagnostics, d => d.Code == "QSL007");
   }

   // ── ON_PAGE: only comma allowed as separator ──────────────────────────────

   [Fact]
   public void OnPage_SemicolonSeparator_TreatedAsSingleLabel()
   {
      // Semicolon is NOT a valid separator — "Q1;Q2" is one (unknown) label, not two.
      const string qsl = """
         QUESTION P PAGE
         [
         ON_PAGE = "Q1;Q2";
         ]
         Page text
         QUESTION Q1 SINGLE
         Choose
         1: A
         QUESTION Q2 SINGLE
         Choose
         1: B
         """;
      var (_, analysis) = QslParseService.ParseAndAnalyze(qsl);
      // The semicolon-joined value is not a known label → QSL002 for "Q1;Q2".
      Assert.Contains(analysis.Diagnostics,
         d => d.Code == "QSL002" && d.Message.Contains("Q1;Q2"));
   }

   [Fact]
   public void OnPage_CommaSeparator_ResolvesLabels()
   {
      // Comma IS the valid separator — both labels should resolve without QSL002.
      const string qsl = """
         QUESTION P PAGE
         [
         ON_PAGE = "Q1,Q2";
         ]
         Page text
         QUESTION Q1 SINGLE
         Choose
         1: A
         QUESTION Q2 SINGLE
         Choose
         1: B
         """;
      var (_, analysis) = QslParseService.ParseAndAnalyze(qsl);
      Assert.DoesNotContain(analysis.Diagnostics,
         d => d.Code == "QSL002" && (d.Message.Contains("Q1") || d.Message.Contains("Q2")));
   }
}
