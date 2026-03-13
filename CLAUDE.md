# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

EmmyLua-Unity-Cli generates EmmyLua type definition files from Unity C# projects using the Roslyn compiler API. It supports XLua and ToLua binding frameworks.

## Build Commands

```bash
# Build (debug)
dotnet build

# Build (release) — run after substantial changes
dotnet build -c Release

# Restore dependencies
dotnet restore

# Format code
dotnet format

# Publish self-contained executable
dotnet publish -c Release -o ./publish

# Publish for a specific platform
dotnet publish -r win-x64 -c Release -o ./artifact
# Other targets: linux-x64, osx-x64, osx-arm64
```

There are no automated tests in this repo; validation is done by running against a real Unity solution.

## CLI Usage

```bash
unity --solution YourProject.sln --bind XLua|ToLua --output ./lua_definitions [options]
```

- `--solution (-s)`: Path to `.sln` file (required)
- `--bind (-b)`: Binding framework — `XLua`, `ToLua`, or `Puerts` (required)
- `--output (-o)`: Output directory for generated `.lua` files (required)
- `--properties (-p)`: MSBuild properties as `key=value` (repeatable)
- `--xlua-export-all`: Export all public types (XLua only)

## Architecture

### Pipeline

```
Program.cs
  └─ CSharpDocGenerator.Run()
       ├─ CSharpWorkspace.OpenSolutionAsync()   # Roslyn MSBuild workspace
       ├─ XLuaClassFinder / ToLuaClassFinder    # Identifies exported symbols
       ├─ CSharpAnalyzer                        # Extracts type metadata → CSType objects
       ├─ GenericTypeManager                    # Deduplicates generic instances
       ├─ TypeReferenceTracker                  # Tracks unexported referenced types
       └─ XLuaDumper / ToLuaDumper             # Writes EmmyLua .lua annotation files
```

### Key Components

| File | Role |
|------|------|
| [Program.cs](EmmyLua.Unity.Cli/Program.cs) | Entry point; initializes MSBuild locator before any Roslyn calls |
| [Generator/CSharpDocGenerator.cs](EmmyLua.Unity.Cli/Generator/CSharpDocGenerator.cs) | Main orchestrator |
| [Generator/CSharpAnalyzer.cs](EmmyLua.Unity.Cli/Generator/CSharpAnalyzer.cs) | Roslyn-based type analysis; produces `CSType` data models |
| [Generator/CSObject.cs](EmmyLua.Unity.Cli/Generator/CSObject.cs) | Data model hierarchy (`CSClassType`, `CSEnumType`, `CSInterface`, `CSDelegate`) |
| [Generator/CSharpClassFinder.cs](EmmyLua.Unity.Cli/Generator/CSharpClassFinder.cs) | Dispatcher to framework-specific finders |
| [Generator/GenericTypeManager.cs](EmmyLua.Unity.Cli/Generator/GenericTypeManager.cs) | Merges `List<int>`, `List<string>` → `List<T>` to reduce output noise |
| [Generator/TypeReferenceTracker.cs](EmmyLua.Unity.Cli/Generator/TypeReferenceTracker.cs) | Emits `*_noexport_types.lua` aliases for types referenced but not exported |
| [Generator/LuaAnnotationFormatter.cs](EmmyLua.Unity.Cli/Generator/LuaAnnotationFormatter.cs) | Formats EmmyLua `---@class`, `---@field`, `---@alias` annotations |
| [Generator/Util.cs](EmmyLua.Unity.Cli/Generator/Util.cs) | `LuaTypeConverter`: maps C# primitives to Lua types (`integer`, `number`, `string`, `boolean`, `any`) |
| [Generator/XLua/XLuaClassFinder.cs](EmmyLua.Unity.Cli/Generator/XLua/XLuaClassFinder.cs) | Finds types marked with `[LuaCallCSharp]` |
| [Generator/ToLua/ToLuaClassFinder.cs](EmmyLua.Unity.Cli/Generator/ToLua/ToLuaClassFinder.cs) | Parses `CustomSettings.customTypeList` for `_GT(typeof(...))` entries |
| [Generator/XLua/XLuaDumper.cs](EmmyLua.Unity.Cli/Generator/XLua/XLuaDumper.cs) | Writes XLua output files (split at 500 KB) |
| [Generator/ToLua/ToLuaDumper.cs](EmmyLua.Unity.Cli/Generator/ToLua/ToLuaDumper.cs) | Writes ToLua output files |

### Data Model

```
CSTypeBase (abstract)
└── CSType (abstract, implements IHasNamespace)
    ├── CSClassType   — classes & structs; Fields, Methods, GenericTypes, BaseClass
    ├── CSEnumType    — enum with constant field values
    ├── CSInterface   — interfaces; Fields (properties), Methods
    └── CSDelegate    — function signatures; Params, ReturnTypeName
```

### Important Constraints

- **MSBuild must be initialized before any Roslyn workspace calls.** `Program.cs` registers MSBuild via `Microsoft.Build.Locator` at startup; do not move or delay this.
- **Output must be deterministic.** Use stable ordering (sort by name) to minimize diff noise.
- **Keep nullable annotations accurate.** Avoid `!` null-forgiving operators unless truly safe.
- **Keep target framework at `net8.0`** in the `.csproj`.
