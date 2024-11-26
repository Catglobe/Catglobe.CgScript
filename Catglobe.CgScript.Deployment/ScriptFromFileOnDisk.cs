﻿using System.Text.RegularExpressions;
using Catglobe.CgScript.Common;

namespace Catglobe.CgScript.Deployment;

/// <summary>
/// Script file from disk.
/// Impersonation is inferred from the file name. If file is named MyFolder\MyScript@123.cgs, impersonation will be 123.
/// If script is named MyScript@123.public.cgs, it will be possible to run the script without a user being logged in.
/// </summary>
public partial class ScriptFromFileOnDisk : IScriptDefinition
{
   private readonly string _fullPath;

   /// <summary>
   /// Script file from disk.
   /// </summary>
   /// <param name="fullPath">Full path on disk to load file</param>
   /// <param name="relativePath">Path that is interpreted as scriptName</param>
   public ScriptFromFileOnDisk(string fullPath, string relativePath)
   {
      _fullPath     = fullPath;
      var match = GetScriptNameAndImpersonation().Match(relativePath);
      ScriptName               = match.Groups["scriptName"].Value.Replace('\\', '/');
      Impersonation            = match.Groups["impersonation"] is {Success: true} g ? uint.Parse(g.Value) : null;
      AllowExecuteWithoutLogin = match.Groups["AllowExecuteWithoutLogin"] is {Success: true};
   }

   [GeneratedRegex(@"^(?<scriptName>.*?)(?:@(?<impersonation>\d+)(?:\.(?<AllowExecuteWithoutLogin>public)?))?\.cgs$", RegexOptions.Singleline | RegexOptions.IgnoreCase, -1)]
   private static partial Regex GetScriptNameAndImpersonation();

   ///<inheritdoc/>
   public string ScriptName { get; }
   ///<inheritdoc/>
   public uint?  Impersonation { get; }
   ///<inheritdoc/>
   public bool AllowExecuteWithoutLogin { get; }
   ///<inheritdoc/>
   public Task<Stream> Content => Task.FromResult<Stream>(File.OpenRead(_fullPath));
}
