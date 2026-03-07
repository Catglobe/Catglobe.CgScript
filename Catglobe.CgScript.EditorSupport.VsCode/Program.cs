using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

// Navigate from bin/Debug/net10.0/ up to the extension root
var extensionDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));

// The test file to open — matches the one in launchSettings.json
const string testFile = @"D:\VoxTest\VoxTest\CgScript\AppProductsAdmin\createCompany.cgs";

var codeArgs = $"""--extensionDevelopmentPath="{extensionDir}" "{testFile}" """;

ProcessStartInfo psi;
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    // code is a .cmd script; run via cmd /c so PATH is resolved
    psi = new ProcessStartInfo("cmd.exe", $"/c code {codeArgs}") { UseShellExecute = true };
}
else
{
    psi = new ProcessStartInfo("code", codeArgs) { UseShellExecute = true };
}

Process.Start(psi);
