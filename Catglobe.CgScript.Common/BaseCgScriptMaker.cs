using System.Text;
using System.Text.RegularExpressions;

namespace Catglobe.CgScript.Common;

/// <summary>
/// Create a script from a script definition and a mapping
/// </summary>
public abstract partial class BaseCgScriptMaker(string environment, IReadOnlyDictionary<string, IScriptDefinition> definitions)
{
   /// <summary>
   /// Get the content of a script with all references replaced with the actual content.
   /// In development mode, this will be a self-contained script that can be dynamically executed.
   /// In production/staging mode, this will be a script that can be stored in the Catglobe site database.
   /// </summary>
   public async Task<string> GetContent(string scriptName)
   {
      var scriptDef     = definitions[scriptName];
      var preamble      = GetPreamble(scriptDef);
      var stringBuilder = new StringBuilder(preamble);
      await Generate(scriptDef, stringBuilder);
      return stringBuilder.ToString();
   }

   /// <summary>
   /// Generate the script
   /// </summary>
   protected abstract Task Generate(IScriptDefinition scriptDef, StringBuilder stringBuilder);

   /// <summary>
   /// Generate the preamble for the script
   /// </summary>
   protected abstract string GetPreamble(IScriptDefinition scriptDef);

   [GeneratedRegex(@"#IF\s+(?<env>Development|Production|Staging)\s+(?<script>.*?)\s*#ENDIF", RegexOptions.Singleline | RegexOptions.IgnoreCase, -1)]
   private static partial Regex EnvironmentRegex();
   [GeneratedRegex("""new WorkflowScript\s*\(\s*"(?<scriptName>[^"]+)"\s*\)""", RegexOptions.Singleline, -1)]
   protected static partial Regex FindWorkflowReferences();

   /// <summary>
   /// Get the script from storage and run the preprocessor on it
   /// </summary>
   private async Task<string> GetScriptAfterPreprocessor(IScriptDefinition scriptDef)
   {
      string rawScript;
      using (var reader = new StreamReader(await scriptDef.Content)) rawScript = await reader.ReadToEndAsync();

      rawScript = EnvironmentRegex().Replace(rawScript, e => e.Groups["env"].ValueSpan.Equals(environment, StringComparison.InvariantCultureIgnoreCase) ? e.Groups["script"].Value : string.Empty);
      return rawScript;
   }

   /// <summary>
   /// Process script references in the script - replacing them as needed
   /// </summary>
   protected async Task ProcessScriptReferences<T>(IScriptDefinition scriptDef, StringBuilder finalScript, Func<T, string, Task> processSingleReference, T state)
   {
      var rawScript = await GetScriptAfterPreprocessor(scriptDef);
      var lastIdx   = 0;
      foreach (Match match in FindWorkflowReferences().Matches(rawScript))
      {
         //add anything before the match to the sb
         finalScript.Append(rawScript.AsSpan(lastIdx, match.Index - lastIdx));
         //add the replacement to the sb
         await processSingleReference(state, match.Groups["scriptName"].Value);
         lastIdx = match.Index + match.Length;
      }
      //add rest
      finalScript.Append(rawScript.AsSpan(lastIdx));
   }
}
