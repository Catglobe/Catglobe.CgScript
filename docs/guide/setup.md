# CgScript — Setup Guide

Catglobe.ScriptDeployer helps you develop and deploy CgScripts to your Catglobe site. Three NuGet packages provide the runtime (`Catglobe.CgScript.Runtime`), deployment sync (`Catglobe.CgScript.Deployment`), and a Roslyn source generator (`Catglobe.CgScript.EditorSupport.SourceGenerator`) that reads your `.cgs` files at compile time and emits strongly-typed C# wrapper methods. No more hand-written request records, manual `Execute(path, new(...), callType, returnType)` calls, or boilerplate.

**NuGet packages**:
- [Catglobe.CgScript.Runtime](https://www.nuget.org/packages/Catglobe.CgScript.Runtime)
- [Catglobe.CgScript.Deployment](https://www.nuget.org/packages/Catglobe.CgScript.Deployment)
- [Catglobe.CgScript.EditorSupport.SourceGenerator](https://www.nuget.org/packages/Catglobe.CgScript.EditorSupport.SourceGenerator)

**Requires**: .NET 8 SDK or later, SDK-style `.csproj`, a Catglobe site (on-premise or cloud).

## For Humans

CgScript lets you write business logic in a Catglobe-specific scripting language and run it on your Catglobe site. This toolchain brings those scripts into your .NET project:

- **Runtime** (`Catglobe.CgScript.Runtime`) — lets your app call CgScripts dynamically, executing them on the Catglobe server with the logged-in user's credentials.
- **Deployment** (`Catglobe.CgScript.Deployment`) — syncs your local `.cgs` files to the Catglobe server so they are available in production, staging, and development environments.
- **Source generator** (`Catglobe.CgScript.EditorSupport.SourceGenerator`) — reads your `.cgs` files at compile time and generates strongly-typed C# extension methods. Instead of `await client.Execute("Company/GetCompanyId", new(id), callType, returnType)`, you write `await client.GetCompanyId(id)`. Errors become exceptions. No boilerplate.

Setup takes five minutes: tell the agent your Catglobe site URL, pick where to install MCP and skill files, point at your `.cgs` folder, and the agent handles NuGet packages, MSBuild configuration, serializer context, and a skill file for future AI sessions.

**Re-running is safe.** Every configuration step checks what already exists before modifying anything. No duplicate entries, no overwritten settings, no broken builds from running the setup twice.

## For LLM Agents

Paste this prompt into your AI agent:

```
Add Catglobe.CgScript to this project using the instructions here:
https://raw.githubusercontent.com/Catglobe/Catglobe.ScriptDeployer/main/docs/guide/setup.md
```

If you are an LLM agent helping a user set up Catglobe.CgScript, follow these steps in order.

---

> **For the AI agent executing this guide**: determine the correct file paths and configuration formats for YOUR specific agent runtime (OpenCode, Claude Desktop, VS Code, Cursor, etc.). Skill files follow the skill spec: a folder containing `SKILL.md` (e.g., `my-skill/SKILL.md` inside your agent's skills directory).

### Step 0: Discover the current project state

**Resolution rule**: The discovery phase resolves EVERY question it can answer from the filesystem. The user is only asked questions discovery CANNOT answer, and even then recommendations are provided. An agent that reaches Step 1 without running discovery has already failed.

**0.0 Check the .NET SDK version**

Run `dotnet --list-sdks` and verify that .NET 8 or later is available.

**0.1 Locate the solution and SDK-style projects**

**0.2 Discover existing Catglobe.CgScript PackageReferences**

**0.3 Discover Existing AdditionalFiles patterns for *.cgs**

**0.4 Discover existing CgScriptRootFolder MSBuild property**

**0.5 Discover existing `[CgScriptSerializer]` attribute**

**0.6 Discover existing .cgs files and their folder location**

**0.7 Aggregate summary**

```powershell
# --- 0.0 Check .NET SDK version ---
$sdks = dotnet --list-sdks
if (-not $sdks) {
    Write-Output "ERROR: .NET SDK not found. Install .NET 8 or later from https://dotnet.microsoft.com/download"
    exit 1
}
Write-Output "Installed .NET SDKs:"
$sdks | ForEach-Object { Write-Output "  $_" }
$has8Plus = $sdks | Where-Object { $_ -match '^([8-9]|\d{2,})\.' }
if (-not $has8Plus) {
    Write-Output "WARNING: .NET 8+ SDK not found. This project requires .NET 8 or later."
    Write-Output "Install from: https://dotnet.microsoft.com/download"
    exit 1
}

# --- Initialize variables ---
$cwd = (Get-Location).Path
$cgsRefs = @()
$additionalFilesFound = @()
$rootFolderFound = $false
$serializerFound = $false
$cgsFiles = @()

# --- 0.1 Locate solutions and SDK-style projects ---
$slnFiles  = @(Get-ChildItem -Recurse -Filter *.sln  | Select-Object -First 10 FullName)
$slnxFiles = @(Get-ChildItem -Recurse -Filter *.slnx | Select-Object -First 10 FullName)
$allSolutions = $slnFiles + $slnxFiles
if ($allSolutions.Count -eq 0) { Write-Output "No .sln or .slnx found — this repo may be solution-less" }
elseif ($allSolutions.Count -gt 1) {
    Write-Output "Found $($allSolutions.Count) solution files:"
    $allSolutions | ForEach-Object { Write-Output "  $($_.FullName)" }
    Write-Output "MULTI-SOLUTION: Ask user which one to configure."
}
$projects = Get-ChildItem -Recurse -Filter *.csproj
$sdkProjects = $projects | Where-Object { Select-String -Path $_.FullName -Pattern 'Microsoft.NET.Sdk' -Quiet }
$nonSdkProjects = $projects | Where-Object { $_ -notin $sdkProjects }
if ($nonSdkProjects) {
    Write-Output "NON-SDK-STYLE PROJECTS FOUND (not supported):"
    $nonSdkProjects | ForEach-Object { Write-Output "  $($_.FullName)" }
}
foreach ($p in $sdkProjects) {
    $tfmMatches = Select-String -Path $p.FullName -Pattern '<TargetFramework[^s]'
    $tfm = if ($tfmMatches) { ($tfmMatches | Select-Object -First 1).Line.Trim() } else { "No TFM found" }
    $rel = $p.FullName.Replace($cwd + '\', '')
    Write-Output "  $rel  |  $tfm"
}
if ($sdkProjects.Count -eq 0) { Write-Output "No SDK-style projects found. Nothing to configure."; exit }

# --- 0.2 Discover existing Catglobe.CgScript PackageReferences ---
foreach ($p in $sdkProjects) {
    $content = Get-Content $p.FullName -Raw
    $matches = [regex]::Matches($content, 'Catglobe\.CgScript\.\w+')
    if ($matches.Count -gt 0) {
        $rel = $p.FullName.Replace($cwd + '\', '')
        foreach ($m in $matches) { $cgsRefs += "$rel : $($m.Value)" }
    }
}
if ($cgsRefs.Count -gt 0) {
    Write-Output "=== Existing Catglobe.CgScript PackageReferences ==="
    $cgsRefs | ForEach-Object { Write-Output "  $_" }
} else {
    Write-Output "No existing Catglobe.CgScript PackageReferences found."
}

# --- 0.3 Discover Existing AdditionalFiles patterns for *.cgs ---
foreach ($p in $sdkProjects) {
    $content = Get-Content $p.FullName -Raw
    $afMatches = [regex]::Matches($content, '<AdditionalFiles\s+Include="([^"]*\.cgs[^"]*)"')
    if ($afMatches.Count -gt 0) {
        $rel = $p.FullName.Replace($cwd + '\', '')
        foreach ($m in $afMatches) { $additionalFilesFound += "$rel : $($m.Groups[1].Value)" }
    }
}
if ($additionalFilesFound.Count -gt 0) {
    Write-Output "=== Existing AdditionalFiles patterns ==="
    $additionalFilesFound | ForEach-Object { Write-Output "  $_" }
} else {
    Write-Output "No AdditionalFiles patterns for *.cgs found."
}

# --- 0.4 Discover existing CgScriptRootFolder MSBuild property ---
foreach ($p in $sdkProjects) {
    $content = Get-Content $p.FullName -Raw
    if ($content -match '<CgScriptRootFolder>([^<]+)</CgScriptRootFolder>') {
        Write-Output "CgScriptRootFolder = '$($matches[1])' in $($p.FullName.Replace($cwd + '\', ''))"
        $rootFolderFound = $true
    }
}
if (-not $rootFolderFound) { Write-Output "No CgScriptRootFolder property found (default: CgScript)." }

# --- 0.5 Discover existing [CgScriptSerializer] attribute ---
$csFiles = Get-ChildItem -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' }
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw
    if ($content -match '\[CgScriptSerializer\]') {
        Write-Output "[CgScriptSerializer] found in: $($f.FullName.Replace($cwd + '\', ''))"
        $serializerFound = $true
        if ($content -match 'partial\s+class\s+\w+\s*:\s*JsonSerializerContext') {
            Write-Output "  -> Extends JsonSerializerContext (correct)"
        }
    }
}
if (-not $serializerFound) { Write-Output "No [CgScriptSerializer] attribute found. One needs to be created." }

# --- 0.6 Discover existing .cgs files and their folder location ---
$cgsFiles = Get-ChildItem -Recurse -Filter *.cgs |
    Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' }
if ($cgsFiles.Count -gt 0) {
    Write-Output "Found $($cgsFiles.Count) .cgs files:"
    $cgsFiles | ForEach-Object {
        $rel = $_.FullName.Replace($cwd + '\', '')
        Write-Output "  $rel"
    }
    $folders = $cgsFiles | ForEach-Object {
        $rel = $_.FullName.Replace($cwd + '\', '')
        $parts = $rel -split '\\'
        $parts[0]
    } | Select-Object -Unique
    Write-Output "Top-level folder(s): $($folders -join ', ')"
} else {
    Write-Output "No .cgs files found on disk."
}

# --- 0.7 Aggregate summary ---
Write-Output "=== Discovery Summary ==="
Write-Output "SDK-style projects: $($sdkProjects.Count)"
Write-Output "Existing CgScript packages: $($cgsRefs.Count -gt 0 ? $cgsRefs.Count : '0')"
Write-Output "AdditionalFiles patterns for *.cgs: $($additionalFilesFound.Count -gt 0 ? $additionalFilesFound.Count : '0')"
Write-Output "CgScriptRootFolder set: $($rootFolderFound ? 'yes' : 'no (default CgScript)')"
Write-Output "[CgScriptSerializer] found: $($serializerFound ? 'yes' : 'no')"
Write-Output ".cgs files on disk: $($cgsFiles.Count)"
Write-Output "Target frameworks:"
$sdkProjects | ForEach-Object {
    $tfmMatches = Select-String -Path $_.FullName -Pattern '<TargetFramework[^s]'
    $tfm = if ($tfmMatches) { ($tfmMatches | Select-Object -First 1).Line.Trim() } else { "No TFM found" }
    $rel = $_.FullName.Replace($cwd + '\', '')
    Write-Output "  $rel -> $tfm"
}
```

---

### Step 1: Ask configuration questions

Present the discovery summary to the user, then ask the four questions below.

**Discovery summary to present first:**

```
Here's what I found in your project:
- __PROJECT_COUNT__ SDK-style projects
- __CGS_COUNT__ .cgs files in __CGS_FOLDER__
- Existing CgScript packages: __PACKAGE_COUNT__
- [CgScriptSerializer] attribute: __SERIALIZER_STATUS__
- CgScriptRootFolder: __ROOT_FOLDER__
```

If any item is zero or not found, adjust the wording accordingly.

---

#### Q1: What is your Catglobe site URL?

```
What is your Catglobe site URL?
(e.g. https://mycompany.catglobe.com)
```

The user gives you a domain like `https://mycompany.catglobe.com`. From this you derive the MCP URL as `{siteUrl}/api/cgscript/mcp`.

**You MUST validate the MCP URL** by sending a JSON-RPC `initialize` request:

```json
POST {siteUrl}/api/cgscript/mcp
Content-Type: application/json

{
  "jsonrpc": "2.0",
  "method": "initialize",
  "params": {
    "protocolVersion": "0.1.0",
    "capabilities": {},
    "clientInfo": {
      "name": "cgscript-setup",
      "version": "1.0.0"
    }
  },
  "id": 1
}
```

If the request succeeds (HTTP 200 with a valid JSON-RPC response), store the MCP URL for later steps.

If it fails (timeout, HTTP error, invalid response), report the full URL that was attempted and ask the user to confirm it:

```
I tried connecting to the MCP endpoint at:
  {siteUrl}/api/cgscript/mcp

but got: {error message}

Is the site URL correct? You can type it again, or if your Catglobe site uses
a different MCP path, let me know.
```

Do NOT retry automatically after a failure. Wait for the user to confirm or correct the URL.

---

#### Q2: Where should the MCP be installed?

> "Where should the MCP configuration be saved so AI agents can call CgScripts on your Catglobe site?
>
> 1. **Globally** — Available across all projects (your agent's global MCP config)
> 2. **Locally** — Only this project (per-project MCP config)
> 3. **Embedded in skill** — Included in the SKILL.md frontmatter (choose this if you want MCP config and skill in one file)"

| User says | Action |
|-----------|--------|
| "1" / "global" / "globally" | Write to your agent's global MCP configuration file |
| "2" / "local" / "project" | Write to a per-project MCP configuration file (e.g., `.mcp.json` in the project root) |
| "3" / "skill" / "embedded" | MCP config goes into the SKILL.md frontmatter (Step 5-6). **Only works when the skill is installed globally AND the agent runtime supports skill-embedded MCP.** If the user chose local skill install in Q3, this option is not available — fall back to option 1 or 2. |

> **For the AI agent executing this**: determine the correct MCP configuration file path and format for YOUR specific agent runtime (OpenCode, Claude Desktop, VS Code, Cursor, etc.).

The MCP entry to add:

```json
{
  "mcpServers": {
    "cgscript": {
      "type": "url",
      "url": "__SITE_URL__/api/cgscript/mcp"
    }
  }
}
```

---

#### Q3: Where should the skill be installed?

> "I can save a skill file so future AI sessions remember this project's CgScript configuration — site URL, script folder, and all. Where should I save it?
>
> 1. **Globally** — Available across all projects (your agent's global skills directory)
> 2. **Locally** — Only this repo (project-level skills directory)"

| User says | Action |
|-----------|--------|
| "1" / "global" / "globally" | Save to your agent's global skills directory |
| "2" / "local" / "project" | Save to the project-level skills directory (e.g., `.opencode/skills/` in the project root) |

> **For the AI agent executing this**: determine the correct path for YOUR specific agent. Per the skill spec, skills are saved as a folder containing a `SKILL.md` file — e.g., `cgscript-coder/SKILL.md` inside the skills directory. The folder name is up to you but must be unique. For OpenCode, skills go in `~/.config/opencode/skills/` (global) or `.opencode/skills/` (project).

---

#### Q4: Where do your .cgs script files live?

> "Where are your CgScript files located? (relative path from the project root)"
>
> Default: `CgScript`

If discovery found `.cgs` files in Step 0.6, report the detected folder and ask for confirmation instead:

```
I found .cgs files in the __DETECTED_FOLDER__ folder. Is that correct?
(If not, tell me the relative path from the project root.)
```

Store the answer as `__SCRIPT_FOLDER__` for use in later steps.

---

### Step 2: Add NuGet packages

For each project that needs CgScript support, add the following PackageReferences. Check first whether they already exist (from Step 0.2).

If the project already has `Catglobe.CgScript.Runtime` and `Catglobe.CgScript.Deployment`, skip those. If `Catglobe.CgScript.EditorSupport.SourceGenerator` already exists, skip that too.

```xml
<ItemGroup>
  <!-- Runtime: required for calling CgScripts from your app -->
  <PackageReference Include="Catglobe.CgScript.Runtime" Version="2.*" />

  <!-- Deployment: required for syncing scripts to the Catglobe server -->
  <PackageReference Include="Catglobe.CgScript.Deployment" Version="2.*" />

  <!-- Source generator: analyzer-only, no runtime dependency.
       Reads .cgs files at compile time and emits strongly-typed wrappers. -->
  <PackageReference Include="Catglobe.CgScript.EditorSupport.SourceGenerator" Version="2.*">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>analyzers</IncludeAssets>
  </PackageReference>
</ItemGroup>
```

If Central Package Management is detected (`Directory.Packages.props`), use `<PackageVersion>` there instead and omit the `Version` attribute on `<PackageReference>`.

**Idempotency check**: Before adding any PackageReference, verify it doesn't already exist in the project. `dotnet add package` is generally safe (it upgrades), but editing `.csproj` directly is cleaner.

```bash
dotnet add __PROJECT_PATH__ package Catglobe.CgScript.Runtime
dotnet add __PROJECT_PATH__ package Catglobe.CgScript.Deployment
dotnet add __PROJECT_PATH__ package Catglobe.CgScript.EditorSupport.SourceGenerator
```

---

### Step 3: Add MSBuild elements

Add or ensure these elements in the project's `.csproj`. Check first whether they already exist (from Steps 0.3 and 0.4).

**AdditionalFiles glob** — makes `.cgs` files visible to the source generator:

```xml
<ItemGroup>
  <!-- Source generator reads .cgs files as AdditionalFiles -->
  <AdditionalFiles Include="__SCRIPT_FOLDER__\**\*.cgs" />
</ItemGroup>
```

**None + CopyToPublishDirectory** — ensures `.cgs` files are deployed with the app. Keep any existing `<None>` entry for `.cgs` files; add `CopyToPublishDirectory` if missing:

```xml
<ItemGroup>
  <!-- Scripts must be published alongside the app for runtime execution -->
  <None Include="__SCRIPT_FOLDER__\**\*.cgs" CopyToPublishDirectory="PreserveNewest" />
</ItemGroup>
```

**CgScriptRootFolder property** — controls how script paths are passed to `Execute()`. Without it, the full relative path is used (e.g. `CgScript/Company/GetCompanyId`). With it, the prefix is stripped (e.g. `Company/GetCompanyId`):

```xml
<PropertyGroup>
  <!-- Strip this prefix when passing script paths to Execute() -->
  <CgScriptRootFolder>__SCRIPT_FOLDER__</CgScriptRootFolder>
</PropertyGroup>
```

**CompilerVisibleProperty** — required only when using project references instead of NuGet packages. The NuGet package handles this automatically. If using project references, add:

```xml
<ItemGroup>
  <!-- Required when using project references (NuGet package handles this automatically) -->
  <CompilerVisibleProperty Include="MSBuildProjectDirectory" />
  <CompilerVisibleProperty Include="CgScriptRootFolder" />
</ItemGroup>
```

**Idempotency check**: Before inserting each element, verify it doesn't already exist in the `.csproj`:
- `<AdditionalFiles Include="__SCRIPT_FOLDER__\**\*.cgs" />` — skip if any AdditionalFiles pattern already covers `.cgs`
- `<None Include="__SCRIPT_FOLDER__\**\*.cgs"` — skip if a `<None>` entry already covers `.cgs`
- `<CgScriptRootFolder>` — skip if already set
- `<CompilerVisibleProperty Include="MSBuildProjectDirectory" />` — skip if already present

---

### Step 4: Add the `[CgScriptSerializer]` attribute

The source generator needs a marker attribute on a `partial` class that extends `JsonSerializerContext`. This tells the generator which JSON serialization context to use for deserializing return types.

**Constraint**: Exactly one class per assembly may carry `[CgScriptSerializer]`. If one already exists (from Step 0.5), skip this step.

If none exists, create a new file (e.g. `CgScriptSerializer.cs`) in the project:

```csharp
using Catglobe.CgScript;
using System.Text.Json.Serialization;

// The source generator uses this to resolve JsonTypeInfo for return types.
// Add one [JsonSerializable] entry per non-primitive return type your scripts use.
// Parameter/request types are handled inline by the generator.

[CgScriptSerializer]
[JsonSerializable(typeof(MyReturnType))]
// Add more [JsonSerializable(typeof(...))] as needed for each return type
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
internal partial class CgScriptSerializer : JsonSerializerContext;
```

**What the user needs to know**:

- The class **must** be `partial`.
- The class **must** extend `JsonSerializerContext`.
- **One per assembly** — do not add `[CgScriptSerializer]` on more than one class in the same project.
- **`[JsonSerializable]` for return types only** — the generator only needs `JsonTypeInfo` for return values it deserializes. Parameter/request types are serialized inline by the generated code and do not need registration.
- **Primitive return types** (`string`, `int`, `bool`, `double`, `Guid`, `DateTime`) are built into `JsonSerializerContext` already — no `[JsonSerializable]` needed for those.
- **Enum return types** need `[JsonSerializable(typeof(YourEnum))]` just like any other non-primitive.
- The class can be `internal` — it does not need to be public.

If Discovery Step 0.5 found an existing `[CgScriptSerializer]` class, report its location and verify it meets these requirements. If it is missing `[JsonSerializable]` entries for return types used by the project's `.cgs` files, warn the user.

---

### Step 5: Fetch and fill the skill template

**5a. Fetch the template**

Retrieve the skill template from the raw GitHub URL:

```
https://raw.githubusercontent.com/Catglobe/Catglobe.ScriptDeployer/main/templates/cgscript-skill-template.md
```

If the URL is not accessible, the template can also be found locally in the cloned repository at `templates/cgscript-skill-template.md`.

**5b. Fill in placeholders**

Replace every placeholder in the template with actual values from previous steps:

| Placeholder | Fill with | Example |
|-------------|-----------|---------|
| `__SCRIPT_FOLDER__` | .cgs folder path from Q4 | `CgScript` |

**If Q2 was "embedded" (option 3)**, also add the MCP configuration to the frontmatter and a `Use skill_mcp(...)` line immediately after it. The `__SITE_URL__` placeholder comes from Q1: `{siteUrl}/api/cgscript/mcp`. Note: this only works when the skill is installed globally (Q3 = "global").

```yaml
# Add to frontmatter:
mcp:
  cgscript:
    type: remote
    url: __SITE_URL__/api/cgscript/mcp

# Add as first line after the frontmatter ---:
Use `skill_mcp(mcp_name="cgscript")` to access the Catglobe MCP tools.
```

**5c. Show the final output**

Present the filled template to the user as a code block so they can review it before installation.

````
Here is the skill file that will be installed:

```markdown
---
name: cgscript-coder
...
```
````

---

### Step 6: Install the skill

**6a. Check if skill already exists**

Before writing, check whether a skill folder already exists at the chosen destination (from Q3). Per the skill spec, skills are folders containing a `SKILL.md` file — the full path will be `skills-folder/cgscript-coder/SKILL.md`.

- **Global**: Your agent's global skills directory
- **Local**: Your agent's project-level skills directory

> **For the AI agent**: determine the exact paths for YOUR runtime. For OpenCode: global is `~/.config/opencode/skills/`, local is `.opencode/skills/`. The skill is saved as `cgscript-coder/SKILL.md` inside that directory.

If it exists, inform the user and overwrite with the latest configuration.

**6b. Save the filled template**

Write the filled template from Step 5 to `skills-directory/cgscript-coder/SKILL.md`, creating the folder if needed.

**6c. Handle MCP configuration**

Based on Q2:

- **Global MCP**: Add the `cgscript` MCP entry to your agent's global MCP configuration. Read the existing config, merge, and write back.
- **Local MCP**: Add the `cgscript` MCP entry to the per-project MCP configuration (e.g., `.mcp.json` in the project root).
- **Skill-embedded MCP**: The MCP entry is already included in the SKILL.md frontmatter from Step 5. No separate MCP config needed. Only works with globally installed skills.

> **For the AI agent**: determine the correct MCP config file path and JSON format for YOUR specific agent runtime.

**Idempotency check**: Before modifying MCP config, check if the `cgscript` entry already exists. If it does and the URL matches, skip. If it exists but the URL differs, inform the user before updating.

---

### Step 7: Verify

Run the build to confirm everything compiles:

```bash
dotnet restore
dotnet build
```

The build must complete with zero errors.

**Common post-setup issues and how to fix them:**

| Symptom | Likely cause | Action |
|---------|-------------|--------|
| `CGS010` error: No `[CgScriptSerializer]` class found | Missing the serializer attribute | Complete Step 4 — add the attribute to a `JsonSerializerContext` subclass |
| `CGS010` error: More than one `[CgScriptSerializer]` class | Two classes carry the attribute | Remove the attribute from the extra class — exactly one per assembly |
| `CGS011` warning: Return type not registered | Missing `[JsonSerializable]` entry | Add `[JsonSerializable(typeof(YourType))]` to the serializer context |
| `CGS012` error: Ambiguous parameter type | A `.cgs` file parameter has `number`, `array`, or `object` without a `type=""` annotation | Add XML doc `type=""` annotations to the script (see README Step 4 for the reference table) |
| Source generator does not run / no wrappers emitted | AdditionalFiles pattern missing | Verify Step 3 — `.cgs` files need `<AdditionalFiles>` in `.csproj` |
| Script not found at runtime | Script folder mismatch or `CopyToPublishDirectory` missing | Verify the `<None>` entry with `CopyToPublishDirectory="PreserveNewest"` from Step 3 |
| MCP connection failure | Wrong site URL or MCP endpoint | Re-run Q1 with the correct Catglobe site URL. The MCP URL is `{siteUrl}/api/cgscript/mcp`. |
| Build error after re-running setup | Duplicate MSBuild elements | The setup checks before adding, but if duplicates somehow exist, remove the extras from `.csproj` |
| `NU1301` / NuGet restore error | NuGet source not configured or needs authentication | Run `dotnet nuget list source` to verify `nuget.org` is enabled |

Optionally, verify the MCP endpoint is reachable by testing `{siteUrl}/api/cgscript/mcp` — it should return a valid HTTP response.

### Idempotency

Re-running this setup is safe at every step:

- **PowerShell discovery**: Read-only. No files are modified during discovery.
- **NuGet packages**: Before adding a PackageReference, the setup checks whether it already exists. `dotnet add package` is a no-op for already-installed packages at the same version.
- **MSBuild elements**: Before inserting `<AdditionalFiles>`, `<None>`, or `<CgScriptRootFolder>`, the setup checks whether they already exist. Existing elements are never duplicated.
- **`[CgScriptSerializer]` class**: If the attribute already exists in the project, Step 4 is skipped entirely.
- **MCP configuration**: Before modifying `mcp.json`, the setup checks whether the `cgscript` MCP entry already exists. If it does and the URL matches, no changes are made.
- **Skill file**: If the skill file already exists at the destination, it is overwritten with the latest configuration values. Previous customizations (if any) are lost — warn the user.
- **Build**: `dotnet restore && dotnet build` is always safe to re-run.

If something goes wrong, fix the issue and run Step 7 again. The earlier steps will skip whatever is already configured.
