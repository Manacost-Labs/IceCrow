using IceCrow.FixtureTool;

namespace IceCrow.FixtureTool.Tests;

public sealed class GoldenCorpusTests
{
    [Fact]
    public async Task EveryCommittedBattlegroundsFixturePassesItsGoldenCheckpoints()
    {
        var corpus = Path.Combine(FindRepositoryRoot(), "tests", "fixtures", "battlegrounds");
        var fixtureDirectories = FixtureGoldenRunner.EnumerateFixtureDirectories(corpus).ToArray();

        Assert.NotEmpty(fixtureDirectories);
        var results = await FixtureGoldenRunner.RunCorpusAsync(corpus);
        Assert.Equal(fixtureDirectories.Length, results.Count);
        Assert.All(results, result => Assert.NotEmpty(result.Checkpoints));
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
