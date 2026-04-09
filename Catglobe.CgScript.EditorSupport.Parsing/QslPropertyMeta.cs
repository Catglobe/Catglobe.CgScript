namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>The expected value type of a QSL property.</summary>
public enum QslValueType
{
   /// <summary>Boolean literal: <c>true</c> or <c>false</c>.</summary>
   Bool,
   /// <summary>Integer numeric literal.</summary>
   Int,
   /// <summary>Double-quoted string literal.</summary>
   String,
   /// <summary>Script string (CG_SCRIPT / JAVA_SCRIPT) — contains embedded code.</summary>
   Script,
   /// <summary>Comma- or semicolon-separated list of question labels (ON_PAGE).</summary>
   LabelList,
   /// <summary>Range expression <c>[n-m, …]</c>.</summary>
   Ranges,
   /// <summary>Either an integer literal or a range expression (DEFAULT_ANSWER).</summary>
   NumberOrRanges,
}

/// <summary>Metadata for a known QSL property: name, expected value type, and documentation.</summary>
public sealed record QslPropertyInfo(string Name, QslValueType ValueType, string Doc);

/// <summary>Registry of all known QSL property names with their metadata.</summary>
public static class QslPropertyMeta
{
   /// <summary>Case-insensitive lookup of all known properties by name.</summary>
   public static readonly IReadOnlyDictionary<string, QslPropertyInfo> All;

   static QslPropertyMeta()
   {
      var list = new[]
      {
         // ── Bool properties ────────────────────────────────────────────────────
         P("REQUIRED",                            QslValueType.Bool,
            "Whether an answer is required before the respondent can advance to the next page."),
         P("DUMMY",                               QslValueType.Bool,
            "Marks the question as a dummy — data is collected internally but never shown to the respondent."),
         P("DONE",                                QslValueType.Bool,
            "Marks the question as completed; used in CgScript to check whether a question has been answered."),
         P("DISCRETE",                            QslValueType.Bool,
            "Enables discrete (non-continuous) mode on a SCALE question so each option is shown as a distinct button."),
         P("REVERSE",                             QslValueType.Bool,
            "Reverses the display order of answer options (highest-to-lowest)."),
         P("BACK_BUTTON_VISIBLE",                 QslValueType.Bool,
            "Shows (true) or hides (false) the Back navigation button on this question's page."),
         P("CLOSE_BUTTON_VISIBLE",                QslValueType.Bool,
            "Shows (true) or hides (false) the Close navigation button on this question's page."),
         P("NEXT_BUTTON_VISIBLE",                 QslValueType.Bool,
            "Shows (true) or hides (false) the Next navigation button on this question's page."),
         P("RESET_BUTTON_VISIBLE",                QslValueType.Bool,
            "Shows (true) or hides (false) the Reset button on this question's page."),
         P("AUTO_ARRANGE_ANSWER_OPTIONS",         QslValueType.Bool,
            "Automatically arranges answer options into a grid with the column count set by ANSWER_OPTION_COLUMNS."),
         P("DECIMAL_ANSWER_ALLOWED",              QslValueType.Bool,
            "Allows decimal (floating-point) numeric answers for NUMBER or grid questions."),
         P("OPEN_ANSWER_REQUIRED",                QslValueType.Bool,
            "Requires the respondent to enter open text when an open answer option is selected."),
         P("HTML",                                QslValueType.Bool,
            "Enables HTML rendering of the question display text (true) or treats it as plain text (false)."),
         P("DELETE_WHEN_ANONYMIZING",             QslValueType.Bool,
            "Deletes the answer to this question when the response is anonymized for GDPR compliance."),
         P("RANDOMIZE_ANSWER_OPTION",             QslValueType.Bool,
            "Randomizes the order of answer options each time the question is displayed."),
         P("ANSWER_OPTION_UNIQUE_CHOICE",         QslValueType.Bool,
            "Prevents the same answer option from being selected more than once across grid rows."),
         P("OPEN_ANSWER",                         QslValueType.Bool,
            "Marks this answer option as an open (free-text) answer option; shows a text box alongside it."),
         P("ROTATE_ANSWER_OPTION",                QslValueType.Bool,
            "Rotates answer options across respondents to reduce order bias."),
         P("NO_MULTI",                            QslValueType.Bool,
            "Prevents this answer option from being selected in a MULTI question (exclusive option)."),
         P("RANDOMIZE_SUB_QUESTION",              QslValueType.Bool,
            "Randomizes the order of sub-questions in a grid question."),
         P("NUMERICAL_INTERVAL_ALLOWED",          QslValueType.Bool,
            "Allows a numeric interval answer in a SCALEGRID sub-question."),
         P("DECIMAL_INTERVAL_ALLOWED",            QslValueType.Bool,
            "Allows a decimal interval answer in a SCALEGRID sub-question."),
         P("ROTATE_SUB_QUESTION",                 QslValueType.Bool,
            "Rotates sub-questions across respondents to reduce order bias."),

         // ── Int properties ─────────────────────────────────────────────────────
         P("ANSWER_OPTION_COLUMNS",               QslValueType.Int,
            "Number of columns used to arrange answer options (requires AUTO_ARRANGE_ANSWER_OPTIONS = true)."),
         P("ANSWER_OPTION_ROWS",                  QslValueType.Int,
            "Number of rows used to arrange answer options in a grid layout."),
         P("MAX_VALUE",                           QslValueType.Int,
            "Maximum allowed numeric value for a NUMBER question or SCALEGRID sub-question."),
         P("MIN_VALUE",                           QslValueType.Int,
            "Minimum allowed numeric value for a NUMBER question or SCALEGRID sub-question."),
         P("STEP",                                QslValueType.Int,
            "Step interval between valid numeric values (e.g. 2 accepts only even numbers)."),
         P("POINTS",                              QslValueType.Int,
            "Score points awarded when the correct answer is selected (for quiz/test questionnaires)."),
         P("COUNT_DOWN",                          QslValueType.Int,
            "Number of seconds for a countdown timer displayed on this question's page."),
         P("ANSWER_SHEET_STATUS",                 QslValueType.Int,
            "Controls whether the running answer sheet is shown: 0 = hidden, 1 = visible."),
         P("EXPORT_POSITION",                     QslValueType.Int,
            "Starting column position when exporting data in fixed-width format."),
         P("EXPORT_LENGTH",                       QslValueType.Int,
            "Field length when exporting data in fixed-width format."),
         P("DATA_PRIVACY_LEVEL",                  QslValueType.Int,
            "Data privacy level: 0 = public, 1 = internal, 2 = confidential, 3 = sensitive."),
         P("BACKGROUND_DATA",                     QslValueType.Int,
            "Marks the question as background/panel data with the given category identifier."),

         // ── NumberOrRanges properties ──────────────────────────────────────────
         P("DEFAULT_ANSWER",                      QslValueType.NumberOrRanges,
            "Default pre-selected answer: either a single integer or a range expression [n, m-p] of answer option indices."),

         // ── String properties ──────────────────────────────────────────────────
         P("MIN_TEXT",                            QslValueType.String,
            "Label text displayed at the minimum (low) end of a SCALE or SCALEGRID question."),
         P("MAX_TEXT",                            QslValueType.String,
            "Label text displayed at the maximum (high) end of a SCALE or SCALEGRID question."),
         P("BACK_BUTTON_TEXT",                    QslValueType.String,
            "Custom display text for the Back navigation button (overrides the theme default)."),
         P("CLOSE_BUTTON_TEXT",                   QslValueType.String,
            "Custom display text for the Close navigation button (overrides the theme default)."),
         P("NEXT_BUTTON_TEXT",                    QslValueType.String,
            "Custom display text for the Next navigation button (overrides the theme default)."),
         P("RESET_BUTTON_TEXT",                   QslValueType.String,
            "Custom display text for the Reset button (overrides the theme default)."),
         P("REQUIRED_TEXT",                       QslValueType.String,
            "Validation message shown when a required question is left unanswered."),
         P("MIN_REQUIRED_TEXT",                   QslValueType.String,
            "Validation message shown when fewer than the minimum number of options are selected."),
         P("MAX_REQUIRED_TEXT",                   QslValueType.String,
            "Validation message shown when more than the maximum number of options are selected."),
         P("NUMBER_REQUIRED_TEXT",                QslValueType.String,
            "Validation message shown when the answer is not a valid number."),
         P("INTEGER_REQUIRED_TEXT",               QslValueType.String,
            "Validation message shown when the answer is not a valid integer."),
         P("NUMBER_OVERFLOW_TEXT",                QslValueType.String,
            "Validation message shown when the numeric answer exceeds the representable range."),
         P("MIN_VALUE_REQUIRED_TEXT",             QslValueType.String,
            "Validation message shown when the numeric answer is below MIN_VALUE."),
         P("MAX_VALUE_REQUIRED_TEXT",             QslValueType.String,
            "Validation message shown when the numeric answer exceeds MAX_VALUE."),
         P("GRID_REQUIRED_TEXT",                  QslValueType.String,
            "Validation message shown when a grid question has unanswered rows."),
         P("MIN_VALUE_IN_GRID_REQUIRED_TEXT",     QslValueType.String,
            "Validation message shown when a grid cell value is below MIN_VALUE."),
         P("MAX_VALUE_IN_GRID_REQUIRED_TEXT",     QslValueType.String,
            "Validation message shown when a grid cell value exceeds MAX_VALUE."),
         P("ILLEGAL_TYPE_TEXT",                   QslValueType.String,
            "Validation message shown when the answer value has an unexpected or illegal type."),
         P("NO_SAME_VALUE_TEXT",                  QslValueType.String,
            "Validation message shown when duplicate values are entered in a grid."),
         P("POINTS_TRANSACTION_TEXT",             QslValueType.String,
            "Text displayed to the respondent when points are awarded for answering this question."),
         P("UNIT",                                QslValueType.String,
            "Unit label displayed alongside a numeric answer field (e.g. kg, years)."),
         P("QUESTION_STYLE_SHEET",               QslValueType.String,
            "CSS rules injected into a `<style>` block for this question's page — equivalent to writing a `<style>` element. " +
            "Use standard CSS selectors and declarations to style the question's rendered output."),
         P("LAYOUT",                              QslValueType.String,
            "Layout template name used when rendering this question."),
         P("QUESTION_DESCRIPTION",               QslValueType.String,
            "Extended description text displayed below the question title."),
         P("QUESTIONNAIRE_NOT_OPEN_TEXT",         QslValueType.String,
            "Message shown when a respondent accesses the questionnaire before it has opened."),
         P("QUESTIONNAIRE_UNAUTHORIZED_ACCESS_TEXT", QslValueType.String,
            "Message shown when a respondent is not authorised to participate in the questionnaire."),
         P("QUESTIONNAIRE_CLOSED_TEXT",           QslValueType.String,
            "Message shown when a respondent accesses the questionnaire after it has been closed."),
         P("QUESTIONNAIRE_END_TEXT",              QslValueType.String,
            "Message shown on the final page after the respondent completes all questions."),
         P("QUESTIONNAIRE_PAUSE_TEXT",            QslValueType.String,
            "Message shown when the respondent session is paused or saved for later."),
         P("QUESTIONNAIRE_COMPLETED_TEXT",        QslValueType.String,
            "Message shown when a respondent who has already completed the questionnaire tries to start again."),
         P("QUESTIONNAIRE_BEFORE_START_DATE_TEXT",QslValueType.String,
            "Message shown when a respondent accesses the questionnaire before its scheduled start date."),
         P("QUESTIONNAIRE_AFTER_END_DATE_TEXT",   QslValueType.String,
            "Message shown when a respondent accesses the questionnaire after its scheduled end date."),

         // ── Script properties ──────────────────────────────────────────────────
         P("CG_SCRIPT",                           QslValueType.Script,
            "CgScript code executed server-side when the question is rendered. " +
            "The script has access to the current respondent context and the questionnaire answer sheet."),
         P("JAVA_SCRIPT",                         QslValueType.Script,
            "JavaScript code injected into the question page and executed client-side in the browser when the page loads. " +
            "Use for client-side UI manipulation or validation."),

         // ── LabelList properties ───────────────────────────────────────────────
         P("ON_PAGE",                             QslValueType.LabelList,
            "Comma- or semicolon-separated list of question labels displayed together on this PAGE question. " +
            "Questions listed here appear as a block on the same page rather than on separate pages."),
      };

      var dict = new Dictionary<string, QslPropertyInfo>(StringComparer.OrdinalIgnoreCase);
      foreach (var info in list)
         dict[info.Name] = info;
      All = dict;
   }

   private static QslPropertyInfo P(string name, QslValueType type, string doc) =>
      new QslPropertyInfo(name, type, doc);
}
