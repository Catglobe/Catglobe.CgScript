namespace Catglobe.CgScript.EditorSupport.Parsing;

/// <summary>Valid property name sets for each QSL property context.</summary>
public static class QslPropertySets
{
   /// <summary>Valid property names inside a QUESTIONNAIRE […] block.</summary>
   public static readonly IReadOnlyList<string> QnaireProps = new[]
   {
      "ANSWER_OPTION_COLUMNS","ANSWER_OPTION_ROWS",
      "REQUIRED","BACK_BUTTON_VISIBLE","CLOSE_BUTTON_VISIBLE","NEXT_BUTTON_VISIBLE","RESET_BUTTON_VISIBLE",
      "TTS_ENABLED",
      "AUTO_ARRANGE_ANSWER_OPTIONS","HTML",
      "MIN_TEXT","MAX_TEXT","MIN_VALUE_REQUIRED_TEXT","MAX_VALUE_REQUIRED_TEXT",
      "BACK_BUTTON_TEXT","CLOSE_BUTTON_TEXT","NEXT_BUTTON_TEXT","JAVA_SCRIPT",
      "QUESTIONNAIRE_NOT_OPEN_TEXT","QUESTIONNAIRE_UNAUTHORIZED_ACCESS_TEXT",
      "QUESTIONNAIRE_CLOSED_TEXT","QUESTIONNAIRE_END_TEXT","QUESTIONNAIRE_PAUSE_TEXT",
      "QUESTIONNAIRE_COMPLETED_TEXT","QUESTIONNAIRE_BEFORE_START_DATE_TEXT",
      "QUESTIONNAIRE_AFTER_END_DATE_TEXT","RESET_BUTTON_TEXT","REQUIRED_TEXT",
      "MIN_REQUIRED_TEXT","MAX_REQUIRED_TEXT","NUMBER_REQUIRED_TEXT",
      "INTEGER_REQUIRED_TEXT","NUMBER_OVERFLOW_TEXT","GRID_REQUIRED_TEXT",
      "MIN_VALUE_IN_GRID_REQUIRED_TEXT","MAX_VALUE_IN_GRID_REQUIRED_TEXT",
      "ILLEGAL_TYPE_TEXT","NO_SAME_VALUE_TEXT",
      "TTS_BUTTON_TEXT",
      "TTS_PRESS_FOR_TEXT",
      "TTS_AO_TOO_MANY_TEXT",
      "TTS_ENTER_TO_NEXT_TEXT",
      "TTS_MULTI_SELECT_TEXT",
   };

   /// <summary>Valid property names inside a question […] block.</summary>
   public static readonly IReadOnlyList<string> QuestionProps = new[]
   {
      // bool
      "REQUIRED","DUMMY","DONE","DISCRETE","REVERSE",
      "TTS_ENABLED",
      "BACK_BUTTON_VISIBLE","CLOSE_BUTTON_VISIBLE","NEXT_BUTTON_VISIBLE","RESET_BUTTON_VISIBLE",
      "AUTO_ARRANGE_ANSWER_OPTIONS","DECIMAL_ANSWER_ALLOWED","OPEN_ANSWER_REQUIRED",
      "HTML","DELETE_WHEN_ANONYMIZING",
      // number
      "MAX_VALUE","MIN_VALUE","STEP","POINTS",
      "ANSWER_OPTION_COLUMNS","ANSWER_OPTION_ROWS",
      "COUNT_DOWN","ANSWER_SHEET_STATUS","EXPORT_POSITION","EXPORT_LENGTH",
      "DEFAULT_ANSWER","DATA_PRIVACY_LEVEL","BACKGROUND_DATA",
      // string
      "MIN_TEXT","MAX_TEXT",
      "BACK_BUTTON_TEXT","CLOSE_BUTTON_TEXT","NEXT_BUTTON_TEXT","RESET_BUTTON_TEXT",
      "JAVA_SCRIPT","REQUIRED_TEXT",
      "MIN_REQUIRED_TEXT","MAX_REQUIRED_TEXT","NUMBER_REQUIRED_TEXT",
      "INTEGER_REQUIRED_TEXT","NUMBER_OVERFLOW_TEXT",
      "MIN_VALUE_REQUIRED_TEXT","MAX_VALUE_REQUIRED_TEXT",
      "GRID_REQUIRED_TEXT","MIN_VALUE_IN_GRID_REQUIRED_TEXT","MAX_VALUE_IN_GRID_REQUIRED_TEXT",
      "ILLEGAL_TYPE_TEXT","NO_SAME_VALUE_TEXT",
      "POINTS_TRANSACTION_TEXT","UNIT","CG_SCRIPT",
      "QUESTION_STYLE_SHEET","ON_PAGE","LAYOUT","QUESTION_DESCRIPTION",
      "TTS_AO_SPEECH",
   };

   /// <summary>Valid property names inside a subquestion (SQ) […] block.</summary>
   public static readonly IReadOnlyList<string> SqProps = new[]
   {
      "DISCRETE","REVERSE","REQUIRED",
      "MAX_VALUE","MIN_VALUE","STEP",
      "MIN_TEXT","MAX_TEXT",
      "DEFAULT_ANSWER","NUMERICAL_INTERVAL_ALLOWED","DECIMAL_INTERVAL_ALLOWED",
      "DECIMAL_ANSWER_ALLOWED","RANDOMIZE_SUB_QUESTION","ROTATE_SUB_QUESTION",
   };

   /// <summary>Valid property names inside an answer option (Int:) […] block.</summary>
   public static readonly IReadOnlyList<string> AoProps = new[]
   {
      "ANSWER_OPTION_UNIQUE_CHOICE","OPEN_ANSWER","NO_MULTI",
      "RANDOMIZE_ANSWER_OPTION","ROTATE_ANSWER_OPTION",
   };

   /// <summary>Returns a case-insensitive set for fast lookup.</summary>
   public static HashSet<string> ToHashSet(IReadOnlyList<string> list) =>
      new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
}
