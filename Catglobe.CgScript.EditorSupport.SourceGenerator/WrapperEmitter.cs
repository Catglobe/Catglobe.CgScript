using System.Text;

namespace Catglobe.CgScript.EditorSupport.SourceGenerator;

/// <summary>
/// Emits C# source for a typed AOT-safe wrapper method for one .cgs script.
///
/// Params are serialised via a generated <c>JsonMetadataServices.CreateObjectInfo</c> with an inline
/// <c>SerializeHandler</c> — no STJ source generator involvement needed for the params type.
/// The return-type <c>JsonTypeInfo</c> is resolved from the assembly's <c>[CgScriptSerializer]</c>
/// context (a <c>JsonSerializerContext</c> the user declares and annotates with
/// <c>[JsonSerializable(typeof(TR))]</c>).
/// </summary>
internal static class WrapperEmitter
{
   private const string JMeta   = "global::System.Text.Json.Serialization.Metadata.JsonMetadataServices";
   private const string JObjVal = "global::System.Text.Json.Serialization.Metadata.JsonObjectInfoValues";
   private const string JWriter = "global::System.Text.Json.Utf8JsonWriter";

   /// <summary>
   /// Generates the partial class body (without namespace/class wrapper) for one script.
   /// </summary>
   public static string Emit(ScriptMetadata meta, string wrapperNamespace, string contextFullName)
   {
      var sb = new StringBuilder();

      var lastSlash   = meta.ScriptName.LastIndexOf('/');
      var methodName  = ToPascalCase(lastSlash >= 0
                           ? meta.ScriptName.Substring(lastSlash + 1)
                           : meta.ScriptName);
      var paramsClass = methodName + "Params";
      var isVoid      = meta.ReturnType == "void";
      var returnCs    = isVoid ? "object" : meta.ReturnType;
      var hasParams   = meta.Parameters.Count > 0;
      var emitParams  = hasParams || meta.NeedsEmptyParams;

      // ── XML doc comment ──────────────────────────────────────────────────────
      sb.AppendLine($"    // Generated from {meta.ScriptName}.cgs");
      sb.AppendLine("    /// <summary>");
      sb.AppendLine($"    /// {XmlEscape(meta.Summary ?? $"Calls the <c>{meta.ScriptName}</c> CgScript.")}");
      sb.AppendLine("    /// </summary>");
      foreach (var p in meta.Parameters)
      {
         if (p.Doc != null)
            sb.AppendLine($"    /// <param name=\"{ToCamelCase(p.Name)}\">{XmlEscape(p.Doc)}</param>");
      }
      if (meta.ReturnDoc != null)
         sb.AppendLine($"    /// <returns>{XmlEscape(meta.ReturnDoc)}</returns>");
      if (isVoid)
         sb.Append($"    public static async global::System.Threading.Tasks.Task {methodName}(");
      else
         sb.Append($"    public static async global::System.Threading.Tasks.Task<{returnCs}> {methodName}(");
      sb.Append("this global::Catglobe.CgScript.Runtime.ICgScriptApiClient client");
      foreach (var p in meta.Parameters)
      {
         if (p.IsOptional)
            sb.Append($", {MakeNullable(p.CsType)} {ToCamelCase(p.Name)} = null");
         else
            sb.Append($", {p.CsType} {ToCamelCase(p.Name)}");
      }
      sb.AppendLine(", global::System.Threading.CancellationToken ct = default)");
      sb.AppendLine("    {");
      sb.AppendLine($"        var ctx = global::{contextFullName}.Default;");

      if (emitParams)
      {
         // Build the params type info using CreateObjectInfo + inline SerializeHandler.
         // This is AOT-safe: the trimmer can see every type accessed via the handler.
         sb.AppendLine($"        var paramsInfo = {JMeta}.CreateObjectInfo<{paramsClass}>(ctx.Options,");
         sb.AppendLine($"            new {JObjVal}<{paramsClass}>");
         sb.AppendLine("            {");
         sb.AppendLine("                ObjectCreator = null, // serialisation-only");
         sb.AppendLine($"                SerializeHandler = ({JWriter} w, {paramsClass} v) =>");
         sb.AppendLine("                {");
         sb.AppendLine("                    w.WriteStartObject();");
         foreach (var p in meta.Parameters)
         {
            var prop    = ToPascalCase(p.Name);
            var jsonKey = p.Name;
            sb.AppendLine(WritePropertyLine(jsonKey, prop, p.CsType, p.IsOptional));
         }
         sb.AppendLine("                    w.WriteEndObject();");
         sb.AppendLine("                }");
         sb.AppendLine("            });");
      }

      // Resolve return-type info from the user's serializer context via the STJ-generated property.
      // Using the property directly (rather than GetTypeInfo) gives a natural C# compile error if the
      // type is not registered with [JsonSerializable], and CGS011 explains why.
      sb.AppendLine($"        var resultInfo = ctx.{ToStjPropertyName(returnCs)};");

      var executeExpr = emitParams
         ? $"client.Execute<{paramsClass}, {returnCs}>(\"{meta.ScriptName}\", new {paramsClass}({string.Join(", ", meta.Parameters.Select(p => ToCamelCase(p.Name)))}), paramsInfo, resultInfo, cancellationToken: ct)"
         : $"client.Execute<{returnCs}>(\"{meta.ScriptName}\", resultInfo, cancellationToken: ct)";
      if (isVoid)
         sb.AppendLine($"        (await {executeExpr}).GetValueOrThrowError();");
      else
         sb.AppendLine($"        return (await {executeExpr}).GetValueOrThrowError();");

      sb.AppendLine("    }");
      sb.AppendLine();

      // ── params record ────────────────────────────────────────────────────────
      if (emitParams)
      {
         sb.Append($"    private record {paramsClass}(");
         sb.Append(string.Join(", ", meta.Parameters.Select(p =>
            p.IsOptional
               ? $"{MakeNullable(p.CsType)} {ToPascalCase(p.Name)} = null"
               : $"{p.CsType} {ToPascalCase(p.Name)}")));
         sb.AppendLine(");");
      }

      return sb.ToString();
   }

   /// <summary>
   /// Wraps all emitted methods in a single partial class file.
   /// </summary>
   public static string WrapInPartialClass(string wrapperNamespace, string body)
   {
      var sb = new StringBuilder();
      sb.AppendLine("// <auto-generated />");
      sb.AppendLine("#nullable enable");
      sb.AppendLine();
      sb.AppendLine($"namespace {wrapperNamespace}");
      sb.AppendLine("{");
      sb.AppendLine("    public static partial class CgScriptExtensions");
      sb.AppendLine("    {");
      sb.AppendLine(body);
      sb.AppendLine("    }");
      sb.AppendLine("}");
      return sb.ToString();
   }

   // ── Utf8JsonWriter call per C# param type ────────────────────────────────

   private static string WritePropertyLine(string jsonKey, string propName, string csType, bool isOptional)
   {
      const string I = "                    "; // 20 spaces

      if (!isOptional)
         return BuildPropertyWriteContent(jsonKey, $"v.{propName}", csType, I);

      // Optional param: wrap write in a null check.
      // Value types (bool, int, …) use .HasValue/.Value; reference types use != null.
      bool isValueType = IsKnownValueType(csType);
      string condition  = isValueType ? $"v.{propName}.HasValue" : $"v.{propName} != null";
      string valueExpr  = isValueType ? $"v.{propName}.Value"    : $"v.{propName}!";
      string content    = BuildPropertyWriteContent(jsonKey, valueExpr, csType, I + "    ");

      return $"{I}if ({condition})\n{I}{{\n{content}\n{I}}}";
   }

   private static string BuildPropertyWriteContent(string jsonKey, string valueExpr, string csType, string indent) =>
      csType.ToLowerInvariant() switch
      {
         "string"                               => $"{indent}w.WriteString(\"{jsonKey}\", {valueExpr});",
         "double" or "float"                    => $"{indent}w.WriteNumber(\"{jsonKey}\", {valueExpr});",
         "int" or "long" or "short" or "byte"   => $"{indent}w.WriteNumber(\"{jsonKey}\", {valueExpr});",
         "bool"                                 => $"{indent}w.WriteBoolean(\"{jsonKey}\", {valueExpr});",
         // Dynamic object type — use reflection-based serialization (not AOT-safe, but object is genuinely dynamic)
         "object"                               =>
            $"{indent}w.WritePropertyName(\"{jsonKey}\");\n" +
            $"{indent}global::System.Text.Json.JsonSerializer.Serialize(w, {valueExpr});",
         _ =>
            $"{indent}w.WritePropertyName(\"{jsonKey}\");\n" +
            $"{indent}global::System.Text.Json.JsonSerializer.Serialize(w, {valueExpr}, ctx.{ToStjPropertyName(csType)});",
      };

   private static string MakeNullable(string csType)
   {
      if (csType.EndsWith("?")) return csType;
      return csType + "?";
   }

   /// <summary>
   /// Returns true for C# value types that require <c>.HasValue</c>/<c>.Value</c>
   /// when made nullable, rather than the <c>!= null</c> reference-type pattern.
   /// </summary>
   private static bool IsKnownValueType(string csType) =>
      csType is "bool" or "int" or "double" or "float" or "long" or "short" or "byte" or "Guid";

   /// <summary>
   /// Converts a C# type name to the property name generated by the STJ source generator on a
   /// <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>.
   /// e.g. "IEnumerable&lt;TagItem&gt;" → "IEnumerableTagItem", "string" → "String"
   /// </summary>
   internal static string ToStjPropertyName(string csType)
   {
      // Nullable value types: "T?" → "NullableT" (e.g. "int?" → "NullableInt32")
      if (csType.EndsWith("?"))
         return "Nullable" + ToStjPropertyName(csType.Substring(0, csType.Length - 1));

      // C# keyword aliases → CLR names used by STJ
      switch (csType)
      {
         case "string":  return "String";
         case "bool":    return "Boolean";
         case "int":     return "Int32";
         case "double":  return "Double";
         case "float":   return "Single";
         case "long":    return "Int64";
         case "short":   return "Int16";
         case "byte":    return "Byte";
         case "object":  return "Object";
      }

      // Generic type: "Outer<Inner>" → "Outer" + ToStjPropertyName("Inner")
      var lt = csType.IndexOf('<');
      if (lt >= 0 && csType[csType.Length - 1] == '>')
      {
         var outerRaw  = csType.Substring(0, lt);
         var dot       = outerRaw.LastIndexOf('.');
         var outerName = dot >= 0 ? outerRaw.Substring(dot + 1) : outerRaw;
         var innerType = csType.Substring(lt + 1, csType.Length - lt - 2);
         return outerName + ToStjPropertyName(innerType);
      }

      // Simple user-defined type — strip any namespace prefix
      var lastDot = csType.LastIndexOf('.');
      return lastDot >= 0 ? csType.Substring(lastDot + 1) : csType;
   }

   // ── helpers ──────────────────────────────────────────────────────────────────

   internal static string ToPascalCase(string s)
   {
      if (string.IsNullOrEmpty(s)) return s;
      var sb = new StringBuilder(s.Length);
      bool capitalize = true;
      foreach (var c in s)
      {
         if (char.IsLetterOrDigit(c) || c == '_')
         {
            sb.Append(capitalize && char.IsLetter(c) ? char.ToUpperInvariant(c) : c);
            capitalize = false;
         }
         else
         {
            capitalize = true; // capitalize next valid char after separator
         }
      }
      if (sb.Length > 0 && char.IsDigit(sb[0]))
         sb.Insert(0, '_');
      return sb.Length > 0 ? sb.ToString() : "_";
   }

   private static string ToCamelCase(string s)
   {
      if (string.IsNullOrEmpty(s)) return s;
      return char.ToLowerInvariant(s[0]) + s.Substring(1);
   }

   private static string XmlEscape(string s) =>
      s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
