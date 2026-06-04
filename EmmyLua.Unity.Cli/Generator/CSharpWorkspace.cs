using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace EmmyLua.Unity.Generator;

public static class CSharpWorkspace
{
    public static async Task<List<Compilation>> OpenSolutionAsync(string path,
        Dictionary<string, string> msbuildProperties)
    {
        var workspace = MSBuildWorkspace.Create(msbuildProperties);

        if (path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            await OpenSlnxAsync(workspace, path);
        else
            await workspace.OpenSolutionAsync(path);

        var projectCompilationList = new List<Compilation>();
        foreach (var project in workspace.CurrentSolution.Projects)
        {
            var compilation = await project.GetCompilationAsync(CancellationToken.None);
            if (compilation != null) projectCompilationList.Add(compilation);
        }

        return projectCompilationList;
    }

    /// <summary>
    /// Roslyn's MSBuildWorkspace.OpenSolutionAsync still routes .slnx through MSBuild's
    /// legacy SolutionFile parser, which only understands the .sln text format. Parse the
    /// .slnx with Microsoft.VisualStudio.SolutionPersistence and load each project individually.
    /// </summary>
    private static async Task OpenSlnxAsync(MSBuildWorkspace workspace, string slnxPath)
    {
        var serializer = SolutionSerializers.GetSerializerByMoniker(slnxPath)
            ?? throw new InvalidOperationException($"No solution serializer for: {slnxPath}");

        var solution = await serializer.OpenAsync(slnxPath, CancellationToken.None);
        var slnxDir = Path.GetDirectoryName(Path.GetFullPath(slnxPath)) ?? ".";

        foreach (var project in solution.SolutionProjects)
        {
            var projectPath = Path.IsPathRooted(project.FilePath)
                ? project.FilePath
                : Path.GetFullPath(Path.Combine(slnxDir, project.FilePath));

            if (!File.Exists(projectPath))
            {
                Console.WriteLine($"  Skipping missing project: {projectPath}");
                continue;
            }

            // OpenProjectAsync transitively loads referenced projects; skip ones already loaded.
            if (workspace.CurrentSolution.Projects.Any(p =>
                    string.Equals(p.FilePath, projectPath, StringComparison.OrdinalIgnoreCase)))
                continue;

            await workspace.OpenProjectAsync(projectPath);
        }
    }
}
