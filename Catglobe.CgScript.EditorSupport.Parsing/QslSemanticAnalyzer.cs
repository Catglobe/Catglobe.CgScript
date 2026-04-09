using Antlr4.Runtime;
using Antlr4.Runtime.Misc;

namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>
/// Single-pass QSL semantic analyzer.
/// Collects symbol definitions and label references in one tree walk,
/// then validates references and property names after the walk completes.
/// </summary>
public sealed class QslSemanticAnalyzer : QslParserBaseVisitor<object?>
{
   // ── Valid property names (delegate to QslPropertySets) ─────────────────────────

   private static readonly HashSet<string> QnaireProps   = QslPropertySets.ToHashSet(QslPropertySets.QnaireProps);
   private static readonly HashSet<string> QuestionProps = QslPropertySets.ToHashSet(QslPropertySets.QuestionProps);
   private static readonly HashSet<string> SqProps       = QslPropertySets.ToHashSet(QslPropertySets.SqProps);
   private static readonly HashSet<string> AoProps       = QslPropertySets.ToHashSet(QslPropertySets.AoProps);

   // ── Accumulated state ────────────────────────────────────────────────────────

   private readonly Dictionary<string, QslSymbol>       _symbols =
      new(StringComparer.OrdinalIgnoreCase);

   // Tracks first-definition positions for duplicate detection.
   private readonly Dictionary<string, (int Line, int Column)> _firstDef =
      new(StringComparer.OrdinalIgnoreCase);

   private readonly List<QslTokenRef>       _refs         = new();
   private readonly List<Diagnostic>        _diagnostics  = new();
   private readonly List<QslFoldingRange>   _foldingRanges = new();

   // Maps a label referenced in ON_PAGE to the PAGE question that contains it.
   private readonly Dictionary<string, string> _onPageMap =
      new(StringComparer.OrdinalIgnoreCase);

   // Label of the question currently being visited (set in VisitQuestion).
   private string _currentQuestionLabel = string.Empty;

   // ── Constructor / static entry ───────────────────────────────────────────────

   private QslSemanticAnalyzer() { }

   /// <summary>Runs semantic analysis on a fully-parsed QSL tree.</summary>
   public static QslAnalysis Analyze(QslParser.RootContext tree)
   {
      var analyzer = new QslSemanticAnalyzer();
      analyzer.Visit(tree);
      analyzer.ValidateRefs();
      return new QslAnalysis(
         analyzer._symbols,
         analyzer._refs,
         analyzer._diagnostics,
         analyzer._foldingRanges,
         analyzer._onPageMap);
   }

   // ── Visitor overrides ────────────────────────────────────────────────────────

   /// <inheritdoc/>
   public override object? VisitQuestion([NotNull] QslParser.QuestionContext ctx)
   {
      // The Label token is always the second token (index 1 child of the question rule).
      var labelToken = ctx.Label();
      if (labelToken?.Symbol is { } tok)
      {
         string name = tok.Text;
         string type = DetectQuestionType(ctx);
         string displayText = ExtractFirstStringLiteral(ctx);

         if (_firstDef.TryGetValue(name, out var prev))
         {
            _diagnostics.Add(new Diagnostic(
               DiagnosticSeverity.Warning,
               $"Duplicate label '{name}' — first defined at line {prev.Line}",
               tok.Line, tok.Column, name.Length,
               "QSL004"));
         }
         else
         {
            _firstDef[name] = (tok.Line, tok.Column);
            _symbols[name] = new QslSymbol(
               Name:        name,
               Kind:        "question",
               QuestionType: type,
               DisplayText: displayText,
               Line:        tok.Line,
               Column:      tok.Column,
               Length:      name.Length);
         }

         _refs.Add(new QslTokenRef(name, IsDefinition: true,
            tok.Line, tok.Column, name.Length));

         // Track the current question label so VisitQproperty can use it for ON_PAGE.
         _currentQuestionLabel = name;
      }

      var result = base.VisitQuestion(ctx);
      _currentQuestionLabel = string.Empty;

      // Emit a folding range for the entire question block.
      int startLine = ctx.Start.Line - 1;
      int endLine   = GetStopLine(ctx.Stop);
      if (startLine != endLine)
         _foldingRanges.Add(new QslFoldingRange(startLine, endLine, "region"));

      return result;
   }

   /// <inheritdoc/>
   public override object? VisitGroup([NotNull] QslParser.GroupContext ctx)
   {
      var labelToken = ctx.Label();
      if (labelToken?.Symbol is { } tok)
      {
         string name = tok.Text;

         if (_firstDef.TryGetValue(name, out var prev))
         {
            _diagnostics.Add(new Diagnostic(
               DiagnosticSeverity.Warning,
               $"Duplicate label '{name}' — first defined at line {prev.Line}",
               tok.Line, tok.Column, name.Length,
               "QSL004"));
         }
         else
         {
            _firstDef[name] = (tok.Line, tok.Column);
            _symbols[name] = new QslSymbol(
               Name:        name,
               Kind:        "group",
               QuestionType: "",
               DisplayText: "",
               Line:        tok.Line,
               Column:      tok.Column,
               Length:      name.Length);
         }

         _refs.Add(new QslTokenRef(name, IsDefinition: true,
            tok.Line, tok.Column, name.Length));
      }

      var result = base.VisitGroup(ctx);

      // Emit a folding range for the GROUP … END block.
      int startLine = ctx.Start.Line - 1;
      int endLine   = GetStopLine(ctx.Stop);
      if (startLine != endLine)
         _foldingRanges.Add(new QslFoldingRange(startLine, endLine, "region"));

      return result;
   }

   /// <inheritdoc/>
   public override object? VisitQproperties([NotNull] QslParser.QpropertiesContext ctx)
   {
      var result = base.VisitQproperties(ctx);

      // Emit a folding range for the property block only when it spans multiple lines.
      int startLine = ctx.Start.Line - 1;
      int endLine   = GetStopLine(ctx.Stop);
      if (startLine != endLine)
         _foldingRanges.Add(new QslFoldingRange(startLine, endLine, "region"));

      return result;
   }

   /// <inheritdoc/>
   public override object? VisitPropValue([NotNull] QslParser.PropValueContext ctx)
   {
      // Emit a folding range for multi-line string literals inside property blocks.
      var strNode = ctx.StringLiteral();
      if (strNode?.Symbol is { } tok)
      {
         int startLine = tok.Line - 1;
         int endLine   = startLine + tok.Text.Count(c => c == '\n');
         if (startLine != endLine)
            _foldingRanges.Add(new QslFoldingRange(startLine, endLine, "region"));
      }

      return base.VisitPropValue(ctx);
   }

   /// <inheritdoc/>
   public override object? VisitBranch([NotNull] QslParser.BranchContext ctx)
   {
      var tok = ctx.Label()?.Symbol;
      if (tok is not null)
         _refs.Add(new QslTokenRef(tok.Text, IsDefinition: false,
            tok.Line, tok.Column, tok.Text.Length));

      return base.VisitBranch(ctx);
   }

   /// <inheritdoc/>
   public override object? VisitLocation([NotNull] QslParser.LocationContext ctx)
   {
      // Only AFTER and BEFORE take a label argument.
      if (ctx.AFTER() is not null || ctx.BEFORE() is not null)
      {
         var tok = ctx.Label()?.Symbol;
         if (tok is not null)
            _refs.Add(new QslTokenRef(tok.Text, IsDefinition: false,
               tok.Line, tok.Column, tok.Text.Length));
      }

      return base.VisitLocation(ctx);
   }

   /// <inheritdoc/>
   public override object? VisitClearTarget([NotNull] QslParser.ClearTargetContext ctx)
   {
      foreach (var labelNode in ctx.Label())
      {
         var tok = labelNode.Symbol;
         _refs.Add(new QslTokenRef(tok.Text, IsDefinition: false,
            tok.Line, tok.Column, tok.Text.Length));
      }

      return base.VisitClearTarget(ctx);
   }

   /// <inheritdoc/>
   public override object? VisitCondition([NotNull] QslParser.ConditionContext ctx)
   {
      // INC_AO_FROM / EXC_AO_FROM carry a Label as source question.
      if (ctx.INC_AO_FROM() is not null || ctx.EXC_AO_FROM() is not null)
      {
         var tok = ctx.Label()?.Symbol;
         if (tok is not null)
            _refs.Add(new QslTokenRef(tok.Text, IsDefinition: false,
               tok.Line, tok.Column, tok.Text.Length));
      }

      return base.VisitCondition(ctx);
   }

   /// <inheritdoc/>
   public override object? VisitQnaireProperty([NotNull] QslParser.QnairePropertyContext ctx)
   {
      ValidatePropName(ctx.Label()?.Symbol, QnaireProps);
      ValidatePropValue(ctx.Label()?.Symbol, ctx.propValue());
      return base.VisitQnaireProperty(ctx);
   }

   /// <inheritdoc/>
   public override object? VisitQproperty([NotNull] QslParser.QpropertyContext ctx)
   {
      var nameTok = ctx.Label()?.Symbol;
      ValidatePropName(nameTok, QuestionProps);
      ValidatePropValue(nameTok, ctx.propValue());
      HandleOnPageProperty(nameTok, ctx.propValue());
      return base.VisitQproperty(ctx);
   }

   /// <inheritdoc/>
   public override object? VisitSqproperty([NotNull] QslParser.SqpropertyContext ctx)
   {
      ValidatePropName(ctx.Label()?.Symbol, SqProps);
      ValidatePropValue(ctx.Label()?.Symbol, ctx.propValue());
      return base.VisitSqproperty(ctx);
   }

   /// <inheritdoc/>
   public override object? VisitAoproperty([NotNull] QslParser.AopropertyContext ctx)
   {
      ValidatePropName(ctx.Label()?.Symbol, AoProps);
      ValidatePropValue(ctx.Label()?.Symbol, ctx.propValue());
      return base.VisitAoproperty(ctx);
   }

   // ── Helpers ──────────────────────────────────────────────────────────────────

   private void ValidatePropName(IToken? tok, HashSet<string> valid)
   {
      if (tok is null) return;
      if (!valid.Contains(tok.Text))
      {
         _diagnostics.Add(new Diagnostic(
            DiagnosticSeverity.Warning,
            $"Unknown property '{tok.Text}'",
            tok.Line, tok.Column, tok.Text.Length,
            "QSL003"));
      }
   }

   /// <summary>
   /// Validates that the property value matches the expected <see cref="QslValueType"/>.
   /// Only emits a diagnostic for clear type mismatches (e.g. Bool property with a string literal).
   /// </summary>
   private void ValidatePropValue(IToken? nameTok, QslParser.PropValueContext? valuCtx)
   {
      if (nameTok is null || valuCtx is null) return;
      if (!QslPropertyMeta.All.TryGetValue(nameTok.Text, out var info)) return;

      bool isBoolToken   = valuCtx.TRUE() is not null || valuCtx.FALSE() is not null;
      bool isStringToken = valuCtx.StringLiteral() is not null;

      string? mismatch = info.ValueType switch
      {
         QslValueType.Bool =>
            isStringToken ? "true or false" : null,
         QslValueType.Int =>
            (isStringToken || isBoolToken) ? "an integer" : null,
         QslValueType.Ranges =>
            (isStringToken || isBoolToken) ? "a range expression [...]" : null,
         QslValueType.NumberOrRanges =>
            (isStringToken || isBoolToken) ? "an integer or range expression [...]" : null,
         _ => null, // String/Script/LabelList: no strict validation
      };

      if (mismatch is not null)
      {
         _diagnostics.Add(new Diagnostic(
            DiagnosticSeverity.Warning,
            $"Property '{nameTok.Text}' expects {mismatch}",
            nameTok.Line, nameTok.Column, nameTok.Text.Length,
            "QSL006"));
      }
   }

   /// <summary>
   /// Handles the special <c>ON_PAGE</c> property: parses the label list from the string
   /// value, creates label references for each entry, and populates the ON_PAGE map.
   /// </summary>
   private void HandleOnPageProperty(IToken? nameTok, QslParser.PropValueContext? valuCtx)
   {
      if (nameTok is null || valuCtx is null) return;
      if (!string.Equals(nameTok.Text, "ON_PAGE", StringComparison.OrdinalIgnoreCase)) return;

      var strNode = valuCtx.StringLiteral();
      if (strNode?.Symbol is not { } strTok) return;

      string pageLabel = _currentQuestionLabel;
      string raw       = strTok.Text; // includes surrounding double quotes
      if (raw.Length < 2) return;
      string unquoted  = raw.Substring(1, raw.Length - 2);

      // Support both comma and semicolon as separators.
      bool isMultiLine = unquoted.Contains('\n');

      int offset = 0;
      foreach (string part in unquoted.Split(new[] { ',', ';' }, StringSplitOptions.None))
      {
         string label = part.Trim();
         if (label.Length > 0)
         {
            // Populate the ON_PAGE map regardless of whether it spans multiple lines.
            if (!string.IsNullOrEmpty(pageLabel))
               _onPageMap[label] = pageLabel;

            // Create label references only for single-line values (positions are reliable).
            if (!isMultiLine)
            {
               // Find the start of this label within the unquoted string.
               int labelStart = offset;
               while (labelStart < unquoted.Length && (unquoted[labelStart] == ' ' || unquoted[labelStart] == '\t'))
                  labelStart++;

               int refLine   = strTok.Line;           // 1-based ANTLR line
               int refCol    = strTok.Column + 1 + labelStart; // +1 skips opening quote

               _refs.Add(new QslTokenRef(label, IsDefinition: false,
                  refLine, refCol, label.Length));
            }
         }

         offset += part.Length + 1; // +1 for the separator character
      }
   }

   private void ValidateRefs()
   {
      foreach (var r in _refs)
      {
         if (r.IsDefinition) continue;
         if (_symbols.TryGetValue(r.Label, out var sym))
         {
            if (sym.Kind == "group")
            {
               _diagnostics.Add(new Diagnostic(
                  DiagnosticSeverity.Warning,
                  $"Label '{r.Label}' refers to a GROUP, which cannot be used as a GOTO/AFTER/BEFORE target",
                  r.Line, r.Column, r.Length,
                  "QSL005"));
            }
            // question — valid target, no diagnostic
         }
         else
         {
            _diagnostics.Add(new Diagnostic(
               DiagnosticSeverity.Warning,
               $"Undefined label '{r.Label}' — may be defined in the full questionnaire",
               r.Line, r.Column, r.Length,
               "QSL002"));
         }
      }
   }

   // ── Static utilities ─────────────────────────────────────────────────────────

   /// <summary>
   /// Returns the 0-based end line of <paramref name="token"/>, accounting for newlines
   /// embedded in multi-line string literal tokens.
   /// </summary>
   private static int GetStopLine(IToken token) =>
      token.Line - 1 + token.Text.Count(c => c == '\n');

   private static string DetectQuestionType(QslParser.QuestionContext ctx)
   {
      if (ctx.PAGE()       is not null) return "PAGE";
      if (ctx.SINGLE()     is not null) return "SINGLE";
      if (ctx.MULTI()      is not null) return "MULTI";
      if (ctx.SCALE()      is not null) return "SCALE";
      if (ctx.NUMBER()     is not null) return "NUMBER";
      if (ctx.TEXT()       is not null) return "TEXT";
      if (ctx.OPEN()       is not null) return "OPEN";
      if (ctx.MULTIMEDIA() is not null) return "MULTIMEDIA";
      if (ctx.SINGLEGRID() is not null) return "SINGLEGRID";
      if (ctx.MULTIGRID()  is not null) return "MULTIGRID";
      if (ctx.SCALEGRID()  is not null) return "SCALEGRID";
      if (ctx.TEXTGRID()   is not null) return "TEXTGRID";
      return "";
   }

   private static string ExtractFirstStringLiteral(QslParser.QuestionContext ctx)
   {
      var strLit = ctx.StringLiteral();
      if (strLit is null) return "";
      return UnquoteStringLiteral(strLit.GetText());
   }

   internal static string UnquoteStringLiteral(string raw)
   {
      if (raw.Length < 2) return raw;
      // Strip surrounding double quotes (netstandard2.0-compatible)
      var inner = raw.Substring(1, raw.Length - 2);
      // Process escape sequences manually
      var sb = new System.Text.StringBuilder(inner.Length);
      int i = 0;
      while (i < inner.Length)
      {
         if (inner[i] == '\\' && i + 1 < inner.Length)
         {
            switch (inner[i + 1])
            {
               case 'n':  sb.Append('\n'); i += 2; break;
               case 'r':  sb.Append('\r'); i += 2; break;
               case 't':  sb.Append('\t'); i += 2; break;
               case 'b':  sb.Append('\b'); i += 2; break;
               case 'f':  sb.Append('\f'); i += 2; break;
               case '"':  sb.Append('"');  i += 2; break;
               case '\'': sb.Append('\''); i += 2; break;
               case '\\': sb.Append('\\'); i += 2; break;
               default:   sb.Append(inner[i]); i++; break;
            }
         }
         else
         {
            sb.Append(inner[i]);
            i++;
         }
      }
      return sb.ToString();
   }
}
