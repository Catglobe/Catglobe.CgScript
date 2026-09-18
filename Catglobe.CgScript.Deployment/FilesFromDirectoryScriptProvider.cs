using Catglobe.CgScript.Common;
using Microsoft.Extensions.Options;

namespace Catglobe.CgScript.Deployment;

/// <summary>
/// Default implementation of <see cref="IScriptDefinition"/> that fetches *.cgs scripts from a file folder.
/// Files in sub folders will get script names that include the folder name. Example: MyFolder\MyScript@123.cgs will get the scriptName MyFolder/MyScript.
/// Two files that map to the same script name are rejected with an <see cref="InvalidOperationException"/> naming both files.
/// </summary>
public class FilesFromDirectoryScriptProvider(IOptions<DeploymentOptions> options) : IScriptProvider
{
   ///<inheritdoc/>
   public Task<IReadOnlyDictionary<string, IScriptDefinition>> GetAll()
   {
      var directory = options.Value.ScriptFolder;
      var scripts = Directory.EnumerateFiles(directory, "*.cgs", SearchOption.AllDirectories)
                            .Select(file => (File: file, Script: new ScriptFromFileOnDisk(file, Path.GetRelativePath(directory, file))))
                            .ToList();

      var duplicate = scripts.GroupBy(entry => entry.Script.ScriptName)
                             .FirstOrDefault(group => group.Count() > 1);
      if (duplicate is not null)
         throw new InvalidOperationException(
            $"Duplicate script name '{duplicate.Key}': the files {string.Join(", ", duplicate.Select(entry => $"'{entry.File}'"))} all map to that script name. Rename or remove one of these files so that every script name is unique.");

      return Task.FromResult<IReadOnlyDictionary<string, IScriptDefinition>>
         (scripts.ToDictionary(entry => entry.Script.ScriptName, IScriptDefinition (entry) => entry.Script));
   }
}
