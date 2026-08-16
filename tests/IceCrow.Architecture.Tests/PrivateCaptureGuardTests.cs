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

    [GeneratedRegex(@"\b[A-Za-z][A-Za-z0-9]{2,11}#[0-9]{3,7}\b")]
    private static partial Regex BattleTagLike();

    [GeneratedRegex(@"\b[A-Za-z]:\\(?:Users|Hearthstone|Games|Program Files)[^\s`""')\]]*")]
    private static partial Regex LocalInstallationPath();

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
    public void ReleaseCompositionUsesANullCaptureObserver()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "IceCrow.App",
            "Runtime",
            "IceCrowRuntime.cs"));

        var debugGuard = source.IndexOf("#if DEBUG", StringComparison.Ordinal);
        var construction = source.IndexOf("new RecordingRuntime", StringComparison.Ordinal);
        var releaseBranch = source.IndexOf("#else", StringComparison.Ordinal);

        Assert.True(debugGuard >= 0, "IceCrowRuntime must guard capture composition with #if DEBUG.");
        Assert.True(
            debugGuard < construction && construction < releaseBranch,
            "RecordingRuntime must be constructed only inside the #if DEBUG branch.");
        Assert.Equal(
            construction,
            source.LastIndexOf("new RecordingRuntime", StringComparison.Ordinal));
        Assert.Contains("_recording = null;", source, StringComparison.Ordinal);
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

    [Fact]
    public void RealClientReportsAndFixturesContainNoPrivateIdentifiers()
    {
        var root = FindRepositoryRoot();
        var protectedFiles = Directory
            .EnumerateFiles(Path.Combine(root, "docs"), "real-client-*.md")
            .Concat(Directory.EnumerateFiles(
                Path.Combine(root, "tests", "fixtures", "battlegrounds"),
                "*",
                SearchOption.AllDirectories));

        var violations = new List<string>();
        foreach (var file in protectedFiles)
        {
            var relative = Path.GetRelativePath(root, file);
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (BattleTagLike().IsMatch(lines[i]))
                {
                    violations.Add($"{relative}:{i + 1}: BattleTag-like identifier");
                }

                if (LocalInstallationPath().IsMatch(lines[i]))
                {
                    violations.Add($"{relative}:{i + 1}: local installation path");
                }
            }
        }

        // Report only the file, line, and category — never the matched value.
        Assert.Empty(violations);
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
