using System;

namespace Catglobe.CgScript.Common;

/// <summary>A script file path does not follow the script naming grammar.</summary>
public class InvalidScriptFileNameException : Exception
{
   /// <summary>Creates an error identifying the file and the reason its name is invalid.</summary>
   /// <param name="fileName">The original relative file path.</param>
   /// <param name="message">The failure reason, including the offending suffix and expected shape.</param>
   public InvalidScriptFileNameException(string fileName, string message) : base(message)
   {
      FileName = fileName;
   }

   /// <summary>The original relative path that failed parsing.</summary>
   public string FileName { get; }
}
