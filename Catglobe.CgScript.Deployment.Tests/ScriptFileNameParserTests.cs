using Catglobe.CgScript.Common;
using Xunit;

namespace Catglobe.CgScript.Deployment.Tests;

public class ScriptFileNameParserTests
{
   [Theory]
   [InlineData("Report.cgs", "Report", null, false, false)]
   [InlineData("Report@0.cgs", "Report", 0, false, false)]
   [InlineData("Report@123.cgs", "Report", 123, false, false)]
   [InlineData("Report@123.pii.cgs", "Report", 123, false, true)]
   [InlineData("Report@123.public.cgs", "Report", 123, true, false)]
   [InlineData("Report@123.pii.public.cgs", "Report", 123, true, true)]
   [InlineData("Dir.with.dots/Report.v2@123.pii.cgs", "Dir.with.dots/Report.v2", 123, false, true)]
   [InlineData("REPORT@00123.PUBLIC.CGS", "REPORT", 123, true, false)]
   [InlineData("a@b@123.pii.cgs", "a@b", 123, false, true)]
   [InlineData("Dir@42.pii/Report.cgs", "Dir@42.pii/Report", null, false, false)]
   [InlineData("Dir@42.pii\\Report@123.PII.PUBLIC.CGS", "Dir@42.pii/Report", 123, true, true)]
   [InlineData("Dir.with.dots\\Report.v2.cgs", "Dir.with.dots/Report.v2", null, false, false)]
   [InlineData(" Report .cgs", " Report ", null, false, false)]
   [InlineData(" Report @123.cgs", " Report ", 123, false, false)]
   [InlineData("Report@2147483647.pii.public.cgs", "Report", 2147483647, true, true)]
   [InlineData("Report@00000000000000000000000123.cgs", "Report", 123, false, false)]
   [InlineData("Report@000.cgs", "Report", 0, false, false)]
   [InlineData("Dir@abc.public.pii/Report.cgs", "Dir@abc.public.pii/Report", null, false, false)]
   [InlineData("Report.pii.v2.cgs", "Report.pii.v2", null, false, false)]
   public void ParsesValidScriptFileName(string fileName, string scriptName, int? impersonation, bool isPublic, bool canAccessPII)
   {
      var parsed = ScriptFileNameParser.Parse(fileName);
      AssertMetadata(parsed, scriptName, impersonation, isPublic, canAccessPII);

      Assert.True(ScriptFileNameParser.TryParse(fileName, out var result, out var reason));
      Assert.Null(reason);
      AssertMetadata(Assert.IsType<ScriptFileName>(result), scriptName, impersonation, isPublic, canAccessPII);
   }

   [Theory]
   [InlineData("Report@123.secret.cgs", "@123.secret")]
   [InlineData("Report@123.publc.cgs", "@123.publc")]
   [InlineData("Report@123.public.pii.cgs", "@123.public.pii")]
   [InlineData("Report@123.pii.pii.cgs", "@123.pii.pii")]
   [InlineData("Report@123.public.public.cgs", "@123.public.public")]
   [InlineData("Report@123..pii.cgs", "@123..pii")]
   [InlineData("Report@abc.cgs", "@abc")]
   [InlineData("Report@-1.cgs", "@-1")]
   [InlineData("Report@2147483648.cgs", "@2147483648")]
   [InlineData("Report", "Report")]
   [InlineData("Report@0.pii.cgs", "@0.pii")]
   [InlineData("Report@0.public.cgs", "@0.public")]
   [InlineData("Report.public.cgs", ".public")]
   [InlineData("Report.pii.cgs", ".pii")]
   [InlineData(".cgs", ".cgs")]
   [InlineData("@123.cgs", "@123")]
   [InlineData("Report.cgs\n", "Report.cgs\n")]
   [InlineData("Line\nBreak@123.public.cgs", "@123.public")]
   [InlineData("Report@\u0661\u0662\u0663.cgs", "@\u0661\u0662\u0663")]
   [InlineData("Report@\uff11\uff12\uff13.cgs", "@\uff11\uff12\uff13")]
   [InlineData("Report@+1.cgs", "@+1")]
   [InlineData("Report@ 1.cgs", "@ 1")]
   [InlineData("Report@1 .cgs", "@1 ")]
   [InlineData("Report@.pii.cgs", "@.pii")]
   [InlineData("Report@.cgs", "@")]
   [InlineData("Report@1.pii.public.pii.cgs", "@1.pii.public.pii")]
   [InlineData("Report@1.pii..public.cgs", "@1.pii..public")]
   [InlineData("Report@1..cgs", "@1.")]
   [InlineData("Report@000.pii.public.cgs", "@000.pii.public")]
   [InlineData("Report.PII.CGS", ".PII")]
   [InlineData("Report.PUBLIC.CGS", ".PUBLIC")]
   [InlineData("Report.cgs ", "Report.cgs ")]
   [InlineData("Line\rBreak.cgs", "Line\rBreak")]
   [InlineData("Line\0Break.cgs", "Line\0Break")]
   [InlineData("Line\u007fBreak.cgs", "Line\u007fBreak")]
   [InlineData("Line\u0085Break.cgs", "Line\u0085Break")]
   [InlineData("Line\"Break.cgs", "Line\"Break")]
   [InlineData("Dir\nName/Report.cgs", "Report")]
   [InlineData("Dir\"Name/Report.cgs", "Report")]
   [InlineData("Dir/.cgs", ".cgs")]
   [InlineData("Dir/@123.cgs", "@123")]
   [InlineData("Report@999999999999999999999999999999.cgs", "@999999999999999999999999999999")]
   [InlineData("", "")]
   public void RejectsInvalidScriptFileName(string fileName, string suffix)
   {
      var exception = Assert.Throws<InvalidScriptFileNameException>(() => ScriptFileNameParser.Parse(fileName));
      Assert.Equal(fileName, exception.FileName);
      Assert.Contains(fileName, exception.Message);
      Assert.Contains(suffix, exception.Message);
      Assert.Contains("name[@<userId>[.pii][.public]].cgs", exception.Message);

      Assert.False(ScriptFileNameParser.TryParse(fileName, out var result, out var reason));
      Assert.Null(result);
      Assert.Equal(exception.Message, reason);
   }

   private static void AssertMetadata(ScriptFileName parsed, string scriptName, int? impersonation, bool isPublic, bool canAccessPII)
   {
      Assert.Equal(scriptName, parsed.ScriptName);
      Assert.Equal((uint?)impersonation, parsed.Impersonation);
      Assert.Equal(isPublic, parsed.AllowExecuteWithoutLogin);
      Assert.Equal(canAccessPII, parsed.CanAccessPII);
   }
}
