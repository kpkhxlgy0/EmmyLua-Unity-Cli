using CommandLine;
using EmmyLua.Unity.Generator;
using Microsoft.Build.Locator;

Console.WriteLine("Try Location MSBuild ...");

// 尝试使用 RegisterDefaults，如果失败则手动查找 .NET SDK
var located = false;
try
{
    MSBuildLocator.RegisterDefaults();
    located = true;
}
catch (Exception)
{
    located = false;
}
if (!located)
{
    Console.WriteLine("Trying to locate .NET SDK MSBuild manually...");
    var dotnetSdk = MSBuildLocator.QueryVisualStudioInstances()
        .FirstOrDefault(v => v.Name.Contains(".NET") || v.Name.Contains("SDK"));

    if (dotnetSdk != null)
    {
        Console.WriteLine($"Found .NET SDK: {dotnetSdk.MSBuildPath}");
        MSBuildLocator.RegisterInstance(dotnetSdk);
    }
    else
    {
        Console.WriteLine("Error: Could not find MSBuild.");
        Console.WriteLine("Please ensure you have installed:");
        Console.WriteLine("  - .NET SDK (https://dotnet.microsoft.com/download)");
        Console.WriteLine("Or set environment variable: DOTNET_ROOT or MSBuildExtensionsPath");
        Environment.Exit(1);
    }
}

Parser.Default
    .ParseArguments<GenerateOptions>(args)
    .WithParsed((o) =>
    {
        var docGenerator = new CSharpDocGenerator(o);
        var exitCode = docGenerator.Run();
        Environment.Exit(exitCode.GetAwaiter().GetResult());
    });