using CommandLine;

namespace EmmyLua.Unity.Generator;

/// <summary>
/// Command line options for the EmmyLua Unity generator
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class GenerateOptions
{
    [Option('s', "solution", Required = true, HelpText = "The path to the solution file (.sln or .slnx).")]
    public string Solution { get; set; } = string.Empty;

    [Option('p', "properties", Required = false, HelpText = "The MSBuild properties (format: key=value).")]
    public IEnumerable<string> Properties { get; set; } = new List<string>();

    [Option('b', "bind", Required = true, HelpText = "Generate XLua/ToLua binding.")]
    public LuaBindingType BindingType { get; set; } = LuaBindingType.None;

    [Option('o', "output", Required = true, HelpText = "The output path.")]
    public string Output { get; set; } = string.Empty;

    [Option('e', "export", Required = false, HelpText = "Export type (Json/Lua).")]
    public LuaExportType ExportType { get; set; } = LuaExportType.None;

    [Option("xlua-export-all", Required = false,
        HelpText = "XLua only: export all public types from compilation, ignoring LuaCallCSharp list.")]
    public bool XLuaExportAll { get; set; } = false;

    /// <summary>
    /// Validate the options
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Solution))
        {
            errors.Add("Solution path is required.");
        }
        else
        {
            var resolved = ResolveSolutionPath(Solution);
            if (!File.Exists(resolved))
                errors.Add($"Solution file not found: {Solution}");
            else if (!IsSupportedSolutionExtension(resolved))
                errors.Add("Solution path must point to a .sln or .slnx file.");
            else
                Solution = resolved;
        }

        if (string.IsNullOrWhiteSpace(Output)) errors.Add("Output path is required.");

        if (BindingType == LuaBindingType.None) errors.Add("Binding type must be specified.");

        // Validate properties format
        foreach (var property in Properties)
            if (!property.Contains('='))
                errors.Add($"Invalid property format: {property}. Expected format: key=value");

        return errors;
    }

    private static bool IsSupportedSolutionExtension(string path) =>
        path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolve the actual solution file to open. The input is treated as a stem when it does
    /// not already end in .sln/.slnx (e.g. "D:/repo/sg" resolves "sg.sln"/"sg.slnx" beside it).
    /// When both .sln and .slnx exist for the stem, the most recently modified one is picked.
    /// Falls back to the original input when neither sibling can be found.
    /// </summary>
    private static string ResolveSolutionPath(string input)
    {
        string dir, stem;
        if (IsSupportedSolutionExtension(input))
        {
            dir = Path.GetDirectoryName(input) ?? "";
            stem = Path.GetFileNameWithoutExtension(input);
        }
        else
        {
            // No recognized extension — treat the whole input as <dir>/<stem>.
            dir = Path.GetDirectoryName(input) ?? "";
            stem = Path.GetFileName(input);
        }
        if (string.IsNullOrEmpty(dir)) dir = ".";
        if (string.IsNullOrEmpty(stem)) return input;

        var sln = Path.Combine(dir, stem + ".sln");
        var slnx = Path.Combine(dir, stem + ".slnx");

        var slnExists = File.Exists(sln);
        var slnxExists = File.Exists(slnx);

        if (slnExists && slnxExists)
            return File.GetLastWriteTimeUtc(slnx) >= File.GetLastWriteTimeUtc(sln) ? slnx : sln;
        if (slnxExists) return slnx;
        if (slnExists) return sln;
        return input;
    }
}

/// <summary>
/// Type of Lua binding framework
/// </summary>
public enum LuaBindingType
{
    None,
    XLua,
    ToLua,
    Puerts
}

/// <summary>
/// Export format for the generated definitions
/// </summary>
public enum LuaExportType
{
    None,
    Json,
    Lua
}