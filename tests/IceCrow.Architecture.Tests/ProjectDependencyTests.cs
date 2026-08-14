using System.Xml.Linq;

namespace IceCrow.Architecture.Tests;

public sealed class ProjectDependencyTests
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedProductionReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["IceCrow.App"] =
            [
                "IceCrow.Hearthstone.Logs",
                "IceCrow.Hearthstone.Data",
                "IceCrow.Hearthstone.Decks",
                "IceCrow.Infrastructure.ManacostApi",
                "IceCrow.Live",
                "IceCrow.Overlay",
                "IceCrow.Platform.Windows",
                "IceCrow.Presentation",
                "IceCrow.Recording",
                "IceCrow.Telemetry",
            ],
            ["IceCrow.Battlegrounds"] =
            [
                "IceCrow.Hearthstone.Entities",
                "IceCrow.Hearthstone.Protocol",
            ],
            ["IceCrow.Battlegrounds.Memory"] = ["IceCrow.Battlegrounds"],
            ["IceCrow.Hearthstone.Entities"] = ["IceCrow.Hearthstone.Protocol"],
            ["IceCrow.Hearthstone.ClientState"] = [],
            ["IceCrow.Hearthstone.Data"] = [],
            ["IceCrow.Hearthstone.Decks"] = ["IceCrow.Hearthstone.Data"],
            ["IceCrow.Hearthstone.Logs"] = [],
            ["IceCrow.Hearthstone.Protocol"] = [],
            ["IceCrow.Live"] =
            [
                "IceCrow.Hearthstone.Logs",
                "IceCrow.Hearthstone.Protocol",
                "IceCrow.Tracking",
            ],
            ["IceCrow.Overlay"] =
            [
                "IceCrow.Platform.Windows",
                "IceCrow.Presentation",
            ],
            ["IceCrow.Platform.Windows"] = [],
            ["IceCrow.Presentation"] = ["IceCrow.Hearthstone.Data", "IceCrow.Tracking"],
            ["IceCrow.Infrastructure.ManacostApi"] = ["IceCrow.Hearthstone.Data"],
            ["IceCrow.Recording"] =
            [
                "IceCrow.Battlegrounds",
                "IceCrow.Battlegrounds.Memory",
                "IceCrow.Hearthstone.Entities",
                "IceCrow.Hearthstone.Protocol",
                "IceCrow.Tracking",
            ],
            ["IceCrow.Tracking"] =
            [
                "IceCrow.Battlegrounds",
                "IceCrow.Battlegrounds.Memory",
                "IceCrow.Hearthstone.Entities",
                "IceCrow.Hearthstone.Protocol",
            ],
            ["IceCrow.Telemetry"] = ["IceCrow.Tracking"],
        };

    private static readonly string[] DomainProjects =
    [
        "IceCrow.Battlegrounds",
        "IceCrow.Battlegrounds.Memory",
        "IceCrow.Hearthstone.Entities",
        "IceCrow.Hearthstone.ClientState",
        "IceCrow.Hearthstone.Data",
        "IceCrow.Hearthstone.Decks",
        "IceCrow.Hearthstone.Logs",
        "IceCrow.Hearthstone.Protocol",
        "IceCrow.Live",
        "IceCrow.Presentation",
        "IceCrow.Recording",
        "IceCrow.Tracking",
        "IceCrow.Telemetry",
    ];

    private static readonly string[] NetworkIndependentProjects =
    [
        "IceCrow.Battlegrounds",
        "IceCrow.Battlegrounds.Memory",
        "IceCrow.Hearthstone.ClientState",
        "IceCrow.Hearthstone.Data",
        "IceCrow.Hearthstone.Entities",
        "IceCrow.Hearthstone.Decks",
        "IceCrow.Hearthstone.Logs",
        "IceCrow.Hearthstone.Protocol",
        "IceCrow.Live",
        "IceCrow.Overlay",
        "IceCrow.Platform.Windows",
        "IceCrow.Presentation",
        "IceCrow.Recording",
        "IceCrow.Telemetry",
        "IceCrow.Tracking",
    ];

    private static readonly string[] ForbiddenDomainSourceTokens =
    [
        "System.Windows",
        "WindowInteropHelper",
        "HwndSource",
        "DllImport(",
        "LibraryImport(",
        "user32.dll",
        "HWND",
    ];

    [Fact]
    public void ProductionProjectsFollowTheAllowedDependencyGraph()
    {
        var graph = LoadProductionGraph();

        Assert.Equal(
            AllowedProductionReferences.Keys.Order(StringComparer.Ordinal),
            graph.Keys.Order(StringComparer.Ordinal));

        foreach (var (project, expectedReferences) in AllowedProductionReferences)
        {
            Assert.Equal(
                expectedReferences.Order(StringComparer.Ordinal),
                graph[project].Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void ProductionProjectGraphIsAcyclic()
    {
        var graph = LoadProductionGraph();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();

        foreach (var project in graph.Keys)
        {
            Visit(project, graph, visited, visiting, path);
        }
    }

    [Fact]
    public void DomainProjectsDoNotUseWpfOrNativeWindowInterop()
    {
        var root = FindRepositoryRoot();

        foreach (var project in DomainProjects)
        {
            var projectDirectory = Path.Combine(root, "src", project);
            var projectFile = Path.Combine(projectDirectory, $"{project}.csproj");
            var projectXml = XDocument.Load(projectFile);

            Assert.DoesNotContain(
                projectXml.Descendants(),
                element =>
                    element.Name.LocalName == "UseWPF" &&
                    string.Equals(element.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));

            Assert.DoesNotContain(
                projectXml.Descendants(),
                element =>
                    (element.Name.LocalName is "PackageReference" or "FrameworkReference" or "Reference") &&
                    IsWindowsPresentationReference(element.Attribute("Include")?.Value));

            foreach (var sourceFile in EnumerateSourceFiles(projectDirectory))
            {
                var source = File.ReadAllText(sourceFile);
                foreach (var token in ForbiddenDomainSourceTokens)
                {
                    Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void TestProjectsDoNotCoupleMultipleProductionModules()
    {
        var root = FindRepositoryRoot();
        var testProjects = Directory.EnumerateFiles(
            Path.Combine(root, "tests"),
            "*.Tests.csproj",
            SearchOption.AllDirectories);

        foreach (var testProject in testProjects)
        {
            var references = ReadProjectReferences(testProject);
            Assert.DoesNotContain(references, reference => reference.EndsWith(".Tests", StringComparison.Ordinal));
            Assert.True(
                references.Count <= 1,
                $"{Path.GetFileNameWithoutExtension(testProject)} directly references multiple production modules: " +
                string.Join(", ", references));
        }
    }

    [Fact]
    public void HearthMirrorTypesStayInsideTheOptionalAdapterBoundary()
    {
        var root = FindRepositoryRoot();
        var productionProjects = Directory.EnumerateFiles(
            Path.Combine(root, "src"),
            "*.csproj",
            SearchOption.AllDirectories);

        foreach (var projectFile in productionProjects)
        {
            var project = Path.GetFileNameWithoutExtension(projectFile);
            if (string.Equals(
                    project,
                    "IceCrow.Hearthstone.ClientState.HearthMirror",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var projectXml = XDocument.Load(projectFile);
            Assert.DoesNotContain(
                projectXml.Descendants(),
                element =>
                    (element.Name.LocalName is "PackageReference" or "Reference" or "ProjectReference") &&
                    element.Attribute("Include")?.Value.Contains(
                        "HearthMirror",
                        StringComparison.OrdinalIgnoreCase) == true);

            foreach (var sourceFile in EnumerateSourceFiles(Path.GetDirectoryName(projectFile)!))
            {
                var source = File.ReadAllText(sourceFile);
                Assert.DoesNotContain("using HearthMirror", source, StringComparison.Ordinal);
                Assert.DoesNotContain("HearthMirror.", source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void NativeImportsStayInsidePlatformAdapters()
    {
        var root = FindRepositoryRoot();
        var productionProjects = Directory.EnumerateFiles(
            Path.Combine(root, "src"),
            "*.csproj",
            SearchOption.AllDirectories);

        foreach (var projectFile in productionProjects)
        {
            var project = Path.GetFileNameWithoutExtension(projectFile);
            if (project is "IceCrow.Platform.Windows" or
                "IceCrow.Hearthstone.ClientState.HearthMirror")
            {
                continue;
            }

            foreach (var sourceFile in EnumerateSourceFiles(Path.GetDirectoryName(projectFile)!))
            {
                var source = File.ReadAllText(sourceFile);
                Assert.DoesNotContain("DllImport(", source, StringComparison.Ordinal);
                Assert.DoesNotContain("LibraryImport(", source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void TrackingAndDomainProjectsDoNotUseHttpClientsOrManacostAdapters()
    {
        var root = FindRepositoryRoot();
        foreach (var project in NetworkIndependentProjects)
        {
            foreach (var sourceFile in EnumerateSourceFiles(Path.Combine(root, "src", project)))
            {
                var source = File.ReadAllText(sourceFile);
                Assert.DoesNotContain("System.Net.Http", source, StringComparison.Ordinal);
                Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
                Assert.DoesNotContain("Infrastructure.ManacostApi", source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void DeveloperToolsAreNotReferencedByRuntimeProjects()
    {
        var root = FindRepositoryRoot();
        var productionProjects = Directory.EnumerateFiles(
            Path.Combine(root, "src"),
            "*.csproj",
            SearchOption.AllDirectories);

        foreach (var projectFile in productionProjects)
        {
            Assert.DoesNotContain(
                ReadProjectReferences(projectFile),
                reference => reference.Contains("FixtureTool", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void ProductionCodeUsesOnlyBoundedChannels()
    {
        var root = FindRepositoryRoot();
        foreach (var sourceFile in EnumerateSourceFiles(Path.Combine(root, "src")))
        {
            var source = File.ReadAllText(sourceFile);
            Assert.DoesNotContain("Channel.CreateUnbounded", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BlockingTaskWaitsStayAtTheWpfExitBoundary()
    {
        var root = FindRepositoryRoot();
        foreach (var sourceFile in EnumerateSourceFiles(Path.Combine(root, "src")))
        {
            var source = File.ReadAllText(sourceFile);
            if (sourceFile.EndsWith(
                    Path.Combine("IceCrow.App", "App.xaml.cs"),
                    StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal(1, Count(source, ".GetAwaiter().GetResult()"));
                continue;
            }

            Assert.DoesNotContain(".GetAwaiter().GetResult()", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GenericDumpingGroundProjectsAreNotIntroduced()
    {
        var projectNames = LoadProductionGraph().Keys;
        Assert.DoesNotContain(projectNames, name =>
            name.EndsWith(".Common", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".Utils", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".Utilities", StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, IReadOnlyList<string>> LoadProductionGraph()
    {
        var root = FindRepositoryRoot();
        return Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .ToDictionary(
                project => Path.GetFileNameWithoutExtension(project)!,
                project => (IReadOnlyList<string>)ReadProjectReferences(project),
                StringComparer.Ordinal);
    }

    private static List<string> ReadProjectReferences(string projectFile)
    {
        var document = XDocument.Load(projectFile);
        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(static include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(include!))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static void Visit(
        string project,
        IReadOnlyDictionary<string, IReadOnlyList<string>> graph,
        ISet<string> visited,
        ISet<string> visiting,
        IList<string> path)
    {
        if (visited.Contains(project))
        {
            return;
        }

        Assert.True(
            visiting.Add(project),
            $"Circular project dependency detected: {string.Join(" -> ", path.Append(project))}");
        path.Add(project);

        foreach (var dependency in graph[project])
        {
            Assert.True(graph.ContainsKey(dependency), $"Unknown production dependency: {dependency}");
            Visit(dependency, graph, visited, visiting, path);
        }

        path.RemoveAt(path.Count - 1);
        visiting.Remove(project);
        visited.Add(project);
    }

    private static bool IsWindowsPresentationReference(string? reference)
    {
        return reference is not null &&
               (reference.Contains("PresentationFramework", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("PresentationCore", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("WindowsBase", StringComparison.OrdinalIgnoreCase) ||
                reference.Contains("Microsoft.WindowsDesktop.App", StringComparison.OrdinalIgnoreCase));
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static IEnumerable<string> EnumerateSourceFiles(string projectDirectory)
    {
        return Directory
            .EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IceCrow.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the IceCrow repository root.");
    }
}
