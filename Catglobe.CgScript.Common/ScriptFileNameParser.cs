using System;

namespace Catglobe.CgScript.Common;

/// <summary>Parses security metadata from the leaf of a relative CgScript file path.</summary>
public static class ScriptFileNameParser
{
   /// <summary>Parses a path shaped as name[@&lt;userId&gt;[.pii][.public]].cgs, preserving whitespace.</summary>
   /// <exception cref="InvalidScriptFileNameException">The file name does not follow the naming grammar.</exception>
   public static ScriptFileName Parse(string fileName)
   {
      if (TryParse(fileName, out var result, out var reason))
         return result!;
      throw new InvalidScriptFileNameException(fileName, reason!);
   }

   /// <summary>Attempts to parse a relative file path, returning a reason on failure.</summary>
   /// <param name="fileName">The relative path, including its .cgs extension.</param>
   /// <param name="result">The parsed name and metadata, or null on failure.</param>
   /// <param name="reason">The failure reason, or null on success.</param>
   /// <returns>Whether the file name follows the naming grammar.</returns>
   public static bool TryParse(string fileName, out ScriptFileName? result, out string? reason)
   {
      var normalized = fileName.Replace('\\', '/');
      var leafStart = normalized.LastIndexOf('/') + 1;
      var leaf = normalized.Substring(leafStart);
      var hasExtension = leaf.EndsWith(".cgs", StringComparison.OrdinalIgnoreCase);
      var stem = hasExtension ? leaf.Substring(0, leaf.Length - 4) : leaf;
      var boundary = stem.LastIndexOf('@');
      var suffix = !hasExtension || stem.Length == 0 ? leaf : boundary >= 0 ? stem.Substring(boundary) : stem;

      result = null;
      reason = $"Invalid script file name '{fileName}': offending suffix '{suffix}'. Expected name[@<userId>[.pii][.public]].cgs; userId must be ASCII digits in 0..2147483647, markers require a nonzero userId, and the script name must be nonempty without control characters or double quotes.";
      if (!hasExtension)
         return false;

      var name = boundary >= 0 ? stem.Substring(0, boundary) : stem;
      if (name.Length == 0)
         return false;

      var scriptName = normalized.Substring(0, leafStart) + name;
      foreach (var character in scriptName)
         if (char.IsControl(character) || character == '"')
            return false;

      uint? impersonation = null;
      var canAccessPII = false;
      var allowExecuteWithoutLogin = false;
      if (boundary < 0)
      {
         if (stem.EndsWith(".pii", StringComparison.OrdinalIgnoreCase) || stem.EndsWith(".public", StringComparison.OrdinalIgnoreCase))
            return false;
      }
      else
      {
         var position = boundary + 1;
         var digitsStart = position;
         uint userId = 0;
         while (position < stem.Length && stem[position] >= '0' && stem[position] <= '9')
         {
            var digit = (uint)(stem[position] - '0');
            if (userId > (int.MaxValue - digit) / 10)
               return false;
            userId = userId * 10 + digit;
            position++;
         }
         if (position == digitsStart)
            return false;

         var markers = stem.Substring(position);
         if (markers.Equals(".pii.public", StringComparison.OrdinalIgnoreCase))
         {
            canAccessPII = true;
            allowExecuteWithoutLogin = true;
         }
         else if (markers.Equals(".pii", StringComparison.OrdinalIgnoreCase))
            canAccessPII = true;
         else if (markers.Equals(".public", StringComparison.OrdinalIgnoreCase))
            allowExecuteWithoutLogin = true;
         else if (markers.Length != 0)
            return false;

         if (userId == 0 && (canAccessPII || allowExecuteWithoutLogin))
            return false;
         impersonation = userId;
      }

      result = new ScriptFileName(scriptName, impersonation, allowExecuteWithoutLogin, canAccessPII);
      reason = null;
      return true;
   }
}
