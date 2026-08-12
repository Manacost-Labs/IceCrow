using System.Text;
using IceCrow.Hearthstone.Logs;

namespace IceCrow.Hearthstone.Logs.Tests;

public sealed class LogConfigManagerTests
{
    [Fact]
    public async Task EnsurePowerLoggingPreservesUnrelatedConfigurationAndIsIdempotent()
    {
        using var directory = new TemporaryLogDirectory();
        var path = directory.GetPath("log.config");
        const string original =
            "# user comment\r\n" +
            "[Network]\r\n" +
            "FilePrinting=false\r\n" +
            "CustomValue=keep-me\r\n" +
            "\r\n" +
            "[Power]\r\n" +
            "LogLevel=0 ; keep comment\r\n" +
            "FilePrinting=False\r\n" +
            "ScreenPrinting=true\r\n";
        await File.WriteAllTextAsync(path, original, new UTF8Encoding(false));

        using var manager = new LogConfigManager();
        var firstChanged = await manager.EnsurePowerLoggingAsync(path);
        var firstResult = await File.ReadAllTextAsync(path);
        var secondChanged = await manager.EnsurePowerLoggingAsync(path);
        var secondResult = await File.ReadAllTextAsync(path);

        Assert.True(firstChanged);
        Assert.False(secondChanged);
        Assert.Equal(firstResult, secondResult);
        Assert.Contains("# user comment", secondResult, StringComparison.Ordinal);
        Assert.Contains("CustomValue=keep-me", secondResult, StringComparison.Ordinal);
        Assert.Contains("ScreenPrinting=true", secondResult, StringComparison.Ordinal);
        Assert.Contains("LogLevel=1; keep comment", secondResult, StringComparison.Ordinal);
        Assert.Contains("FilePrinting=true", secondResult, StringComparison.Ordinal);
        Assert.Contains("Verbose=true", secondResult, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(secondResult, "[Power]"));
    }

    [Fact]
    public async Task EnsurePowerLoggingCreatesOnePowerSectionAcrossRepeatedRuns()
    {
        using var directory = new TemporaryLogDirectory();
        var path = directory.GetPath("nested", "log.config");
        using var manager = new LogConfigManager();

        Assert.True(await manager.EnsurePowerLoggingAsync(path));
        Assert.False(await manager.EnsurePowerLoggingAsync(path));

        var result = await File.ReadAllTextAsync(path);
        Assert.Equal(1, CountOccurrences(result, "[Power]"));
        Assert.Equal(1, CountOccurrences(result, "LogLevel=1"));
        Assert.Equal(1, CountOccurrences(result, "FilePrinting=true"));
        Assert.Equal(1, CountOccurrences(result, "Verbose=true"));
        Assert.Empty(Directory.EnumerateFiles(
            System.IO.Path.GetDirectoryName(path)!,
            "*.tmp",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void LocatorRejectsProductUidPathTraversal()
    {
        using var directory = new TemporaryLogDirectory();
        var locator = new HearthstoneLogLocator(directory.Path, []);

        Assert.Throws<ArgumentException>(() => locator.GetLogConfigPath("..\\other"));
    }

    private static int CountOccurrences(string value, string target)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(target, offset, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            offset += target.Length;
        }

        return count;
    }
}
