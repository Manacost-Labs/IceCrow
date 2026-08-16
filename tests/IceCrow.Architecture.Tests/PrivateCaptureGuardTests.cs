using System.Text.Json;
using System.Text.RegularExpressions;

namespace IceCrow.Architecture.Tests;

/// <summary>
/// Repository-level privacy guards for the developer match capture workflow:
/// private captures stay outside the repository and committed fixtures always
/// declare an explicit, reviewed source type.
/// </summary>
public sealed partial class PrivateCaptureGuardTests
{
    private static readonly string[] AllowedSourceTypes = ["synthetic", "real-anonymized"];

    [GeneratedRegex(@"^\d{8}T\d{6}Z_[0-9a-f]{32}\.icecrow\.json$")]
    private static partial Regex CaptureFileName();

    [Fact]
    public void RepositoryContainsNoPrivateCaptureDirectoriesOrFiles()
    {
        var root = FindRepositoryRoot();

        foreach (var directory in EnumerateRepositoryDirectories(root))
        {
            Assert.False(
                string.Equals(
                    Path.GetFileName(directory),
                    "private-captures",
                    StringComparison.OrdinalIgnoreCase),
                $"Private capture directory found inside the repository: {directory}");

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                Assert.False(
                    CaptureFileName().IsMatch(Path.GetFileName(file)),
                    $"Private capture file found inside the repository: {file}");
            }
        }
    }

    [Fact]
    public void GitIgnoreShieldsPrivateCaptureDirectories()
    {
        var gitIgnore = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), ".gitignore"));

        Assert.Contains("private-captures/", gitIgnore, StringComparison.Ordinal);
    }

    [Fact]
    public void CommittedFixturesDeclareAnExplicitReviewedSourceType()
    {
        var corpus = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "fixtures",
            "battlegrounds");
        var manifests = Directory.EnumerateFiles(
            corpus,
            "expected.json",
            SearchOption.AllDirectories);

        Assert.All(manifests, manifestPath =>
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var sourceType = manifest.RootElement.GetProperty("sourceType").GetString();
            Assert.Contains(sourceType, AllowedSourceTypes);
        });
    }

    private static IEnumerable<string> EnumerateRepositoryDirectories(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var current))
        {
            yield return current;
            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                if (Path.GetFileName(directory) is ".git" or "bin" or "obj" or ".vs")
                {
                    continue;
                }

                pending.Push(directory);
            }
        }
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
