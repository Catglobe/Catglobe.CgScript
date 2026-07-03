---
name: cgscript-coder
description: "Use when writing, editing, or validating CgScript (.cgs) scripts. Provides Catglobe symbol lookup, syntax validation, source-generator compatibility rules, and the Catglobe core-concept knowledge base (domain concepts like sampling, panel, questionnaire, quotas, CATI, etc.). Triggers: .cgs files, CgScript, cgscript, Catglobe scripting."
---

# CgScript

> This skill covers everything you need to write, validate, and deploy source-generator-compatible CgScript (.cgs) files. Follow it instead of searching the codebase or documentation for CgScript conventions.

## The 4-step CgScript MCP workflow

Always use the Catglobe MCP in this order.

### Step 0: Get syntax info

```
get_cgscript_syntax_info()
```

No parameters. Call this once per conversation to get the latest CgScript syntax rules, built-in types, and reserved keywords. Syntax rules can change between Catglobe site versions, so always start here.

### Step 1: Search symbols

```
search_symbols(query: string regex, includeDescriptions?: bool = true, includeObsolete?: bool = false)
```

Uses regex patterns. Short patterns work best:

- `copy|clone` finds all symbols mentioning copy or clone
- `^Mail_` finds symbols starting with Mail_
- `Panel` finds symbols with Panel in the name
- `Questionnaire_|Qnaire_` finds both naming conventions

The `includeDescriptions` option (default true) also searches descriptions. Set `includeObsolete` to true only if you need deprecated symbols.

### Step 2: Get symbol details

```
get_symbol_details(name: string, kind?: string | null)
```

Case-sensitive, exact name match. Use the exact name from Step 1 results. The optional `kind` parameter filters by symbol kind: `Class`, `Function`, `Constant`, `Enum`, `EnumValue`, `GlobalVariable`, `WhereFunction`.

### Step 3: Parse and validate

```
parse_and_validate(scriptContent: string)
```

Mandatory before delivering any script. Returns `CGSxxx` diagnostics with line/column positions and descriptive messages. Fix all errors before delivering — the diagnostic messages explain what's wrong and how to fix it.

## Concepts available

Call `list_core_concepts()` via the MCP to discover all available Catglobe domain concept slugs. Use `get_core_concept(slug)` to retrieve the full markdown for any concept you need. The MCP is always the source of truth — never assume which concepts exist.

## How to write CgScript for the source generator

### Type annotation reference

The source generator reads XML doc comments at the top of each .cgs file to emit strongly-typed C# wrappers. `string` and `bool` are unambiguous. Other CgScript types need explicit annotations:

| CgScript type | Annotation needed? | Example annotation |
|---|---|---|
| `string` | No | -- |
| `bool` | No | -- |
| `number` | **Yes** | `type="int"`, `type="double"`, `type="Guid"` |
| `array` | **Yes** | `type="IReadOnlyCollection<MyType>"`, `type="Guid[]"` |
| `object` / `Dictionary` | **Yes** | `type="MyRecord"`, `type="object"` |
| `question` | **Yes** | `type="MyType"` |
| void (no return) | No | omit `<returns>` |

Enum types use the C# enum name directly in `type=""`. Add `[JsonSerializable(typeof(YourEnum))]` to the `[CgScriptSerializer]` class for enum return types.

### Three workflow patterns

The generator detects these automatically:

**Pattern A (Invoke)** -- function with `.Invoke()`:
```cgscript
/// <summary>Finds a company by name.</summary>
/// <param name="companyFolderId" type="int">Root folder ID.</param>
/// <returns type="int">Company resource ID.</returns>
function(number companyFolderId, string companyName) {
    ...
}.Invoke(Workflow_getParameters()[0]);
```

**Pattern B (dict reads)** -- direct `params[0]["key"]` access:
```cgscript
/// <summary>Gets order details.</summary>
/// <param name="orderId" type="Guid">Order identifier.</param>
/// <returns type="OrderDetails">Order details.</returns>
number orderId = params[0]["orderId"];
...
```

**Pattern C (var dict)** -- explicit dictionary variable:
```cgscript
/// <summary>Updates user profile.</summary>
/// <param name="userId" type="int">User ID.</param>
/// <returns type="bool">Success flag.</returns>
var p = Workflow_getParameters()[0];
int userId = p["userId"];
...
```

### File naming

| Pattern | Example | Purpose |
|---------|---------|---------|
| Standard | `GetCompany.cgs` | Normal script, runs as authenticated user |
| Impersonation | `GetCompany@123.cgs` | Script runs impersonating user 123 |
| Public | `GetCompany@123.public.cgs` | Public script, no login required |

## How to create a new .cgs script

1. **Create the file** at `__SCRIPT_FOLDER__/YourPath/Name.cgs`
2. **Add XML doc annotations** at the top: `<summary>`, `<param>` with `type=""` for ambiguous types, `<returns>` with `type=""` for non-void returns
3. **Add the script body** using Pattern A, B, or C from the workflow patterns above
4. **Validate** via MCP `parse_and_validate()` -- fix all errors
5. **Add deployment config** if needed. The `.csproj` should already have `<AdditionalFiles>` and `<None>` entries for `__SCRIPT_FOLDER__/**/*.cgs`. For new return types, add `[JsonSerializable]` to the `[CgScriptSerializer]` class in C#.

### How the source generator exposes scripts in .NET

For each `.cgs` file, the source generator emits a **static extension method** on `ICgScriptApiClient` (from `Catglobe.CgScript.Runtime`). The generated code lives in a `public static partial class CgScriptExtensions` in your project's namespace.

**What gets generated** (one method per `.cgs` file):

```csharp
// From GetCompany.cgs (returns int, takes int + string):
public static async Task<int> GetCompany(
    this ICgScriptApiClient client,
    int companyFolderId,
    string companyName,
    CancellationToken ct = default) { ... }

// From RunReport.cgs (void return, no params):
public static async Task RunReport(
    this ICgScriptApiClient client,
    CancellationToken ct = default) { ... }
```

**How to call from C#**:

```csharp
using Catglobe.CgScript.Runtime;

// client is your injected/created ICgScriptApiClient
var companyId = await client.GetCompany(companyFolderId: 42, companyName: "Acme");
await client.RunReport();
```

**Mapping rules**:
- The method name comes from the script filename, converted to PascalCase
- Each `<param>` becomes a C# method parameter — the `type=""` annotation determines the C# type
- `<returns type="">` becomes the `Task<T>` return type; no `<returns>` means `Task` (void)
- Optional parameters (Pattern C with `= null`) become nullable C# parameters
- Parameter names are camelCase; the JSON key sent to the server uses the script's parameter name

## Using the MCP to look up symbols

The Catglobe MCP is your source of truth for symbol discovery. Do not guess API names or parameter types.

1. **Search broadly first**: `search_symbols("Panel")` to find all panel-related symbols
2. **Narrow with regex**: `search_symbols("^Panel_")` for symbols starting with Panel_
3. **Get details**: `get_symbol_details("Panel_create", "Function")` to see parameters and return type
4. **Compose your script** from the discovered symbols

Regex tips for search_symbols:
- `|` for alternatives: `create|clone|copy`
- `^` anchors start: `^Mail_` finds Mail_send, Mail_template, etc.
- `.` matches any char. Escape as `\.` for literal dots
- Use `includeDescriptions=true` to search across descriptions too

## Do NOT

- Do NOT edit MSBuild files (.csproj, Directory.Build.props) unless explicitly asked -- deployment config is managed at the project level
- Do NOT guess symbol APIs, parameter names, return types, or function signatures without MCP lookup -- always use `search_symbols()` and `get_symbol_details()`
- Do NOT hardcode type annotations in XML doc comments -- each script's parameter and return types come from the actual business needs and the MCP's symbol inventory
- Do NOT deliver a script without calling `parse_and_validate()` first -- all errors must be fixed before delivery
- Do NOT search the codebase `obj/`, `bin/`, or generated `*.g.cs` files -- the source generator produces those at build time
- Do NOT change `[CgScriptSerializer]` or `[JsonSerializable]` attributes unless adding a new return type
- Do NOT remove or rename .cgs files unless explicitly asked
