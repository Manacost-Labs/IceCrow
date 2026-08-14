using System.Text.RegularExpressions;

namespace IceCrow.Architecture.Tests;

/// <summary>
/// Executable form of <c>docs/ui-performance-rules.md</c>. These rules exist so
/// the overlay stays cheap on mid-range machines by construction rather than by
/// review discipline.
/// </summary>
public sealed partial class UiPerformanceRuleTests
{
    private static readonly string[] ForbiddenRenderingTokens =
    [
        "BlurEffect",
        "BitmapEffect",
        "CompositionTarget.Rendering",
        "RepeatBehavior",
        "LayoutTransform",
        "BeginStoryboard",
        "<Storyboard",
    ];

    private static readonly string[] ForbiddenOverlayDomainTokens =
    [
        "EntityStore",
        "PowerLog",
        "TrackingSession",
        "Deckstring",
        "GameTag",
    ];

    /// <summary>The one file allowed to hold literal colour values.</summary>
    private static readonly string ColourTokenFile =
        Path.Combine("IceCrow.Overlay", "Design", "Colors.xaml");

    /// <summary>The one file allowed to declare a shadow.</summary>
    private static readonly string ShadowDeclarationFile =
        Path.Combine("IceCrow.Overlay", "Design", "Components.xaml");

    [Fact]
    public void ProductionUiAvoidsExpensivePerFrameRenderingPatterns()
    {
        foreach (var file in EnumerateUiFiles())
        {
            var source = File.ReadAllText(file);
            foreach (var token in ForbiddenRenderingTokens)
            {
                Assert.False(
                    source.Contains(token, StringComparison.Ordinal),
                    $"{file} uses the forbidden rendering pattern '{token}'. " +
                    "See docs/ui-performance-rules.md.");
            }
        }
    }

    [Fact]
    public void ShadowsAreDeclaredOnceAndOnlyInTheComponentLibrary()
    {
        foreach (var file in EnumerateUiFiles())
        {
            if (file.EndsWith(ShadowDeclarationFile, StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal(1, CountOccurrences(File.ReadAllText(file), "<DropShadowEffect"));
                continue;
            }

            Assert.DoesNotContain(
                "DropShadowEffect",
                File.ReadAllText(file),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ColoursComeFromDesignTokensInsteadOfLiteralsInsideControls()
    {
        foreach (var file in EnumerateUiFiles(".xaml"))
        {
            if (file.EndsWith(ColourTokenFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var literals = HexColourPattern()
                .Matches(File.ReadAllText(file))
                .Select(match => match.Value)
                .ToArray();

            Assert.True(
                literals.Length == 0,
                $"{file} hardcodes colours ({string.Join(", ", literals)}). " +
                "Use the IceCrow.Brush.* design tokens.");
        }
    }

    [Fact]
    public void OverlayRendersViewStateInsteadOfInterpretingGameState()
    {
        var overlayDirectory = Path.Combine(FindRepositoryRoot(), "src", "IceCrow.Overlay");
        foreach (var file in EnumerateFiles(overlayDirectory, ".cs", ".xaml"))
        {
            var source = File.ReadAllText(file);
            foreach (var token in ForbiddenOverlayDomainTokens)
            {
                Assert.DoesNotContain(token, source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void DeveloperDesignPreviewIsExcludedFromReleaseBuilds()
    {
        var projectFile = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "IceCrow.App",
            "IceCrow.App.csproj");
        var project = File.ReadAllText(projectFile);

        Assert.Contains("'$(Configuration)' != 'Debug'", project, StringComparison.Ordinal);
        Assert.Contains("<Page Remove=\"DesignPreview\\**\" />", project, StringComparison.Ordinal);
        Assert.Contains("<Compile Remove=\"DesignPreview\\**\" />", project, StringComparison.Ordinal);
    }

    [GeneratedRegex("#[0-9A-Fa-f]{3,8}\\b")]
    private static partial Regex HexColourPattern();

    private static IEnumerable<string> EnumerateUiFiles(params string[] extensions)
    {
        var root = Path.Combine(FindRepositoryRoot(), "src");
        return EnumerateFiles(root, extensions.Length == 0 ? [".cs", ".xaml"] : extensions);
    }

    private static IEnumerable<string> EnumerateFiles(string directory, params string[] extensions) =>
        Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path =>
                extensions.Any(extension =>
                    path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) &&
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static int CountOccurrences(string source, string value)
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
