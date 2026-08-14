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
                "IceCrow.Overlay",
                "IceCrow.Platform.Windows",
                "IceCrow.Recording",
            ],
            ["IceCrow.Battlegrounds"] =
            [
                "IceCrow.Hearthstone.Entities",
                "IceCrow.Hearthstone.Protocol",
            ],
            ["IceCrow.Battlegrounds.Memory"] = ["IceCrow.Battlegrounds"],
            ["IceCrow.Hearthstone.Entities"] = ["IceCrow.Hearthstone.Protocol"],
            ["IceCrow.Hearthstone.Logs"] = [],
            ["IceCrow.Hearthstone.Protocol"] = [],
            ["IceCrow.Overlay"] =
            [
                "IceCrow.Battlegrounds",
                "IceCrow.Battlegrounds.Memory",
                "IceCrow.Platform.Windows",
            ],
            ["IceCrow.Platform.Windows"] = [],
            ["IceCrow.Recording"] =
            [
                "IceCrow.Battlegrounds",
                "IceCrow.Battlegrounds.Memory",
                "IceCrow.Hearthstone.Entities",
                "IceCrow.Hearthstone.Logs",
                "IceCrow.Hearthstone.Protocol",
            ],
        };

    private static readonly string[] DomainProjects =
    [
        "IceCrow.Battlegrounds",
        "IceCrow.Battlegrounds.Memory",
        "IceCrow.Hearthstone.Entities",
        "IceCrow.Hearthstone.Logs",
        "IceCrow.Hearthstone.Protocol",
        "IceCrow.Recording",
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
