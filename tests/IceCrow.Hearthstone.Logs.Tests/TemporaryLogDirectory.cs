namespace IceCrow.Hearthstone.Logs.Tests;

internal sealed class TemporaryLogDirectory : IDisposable
{
    private static readonly string TestsRoot = System.IO.Path.GetFullPath(
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IceCrow.Tests"));

    public TemporaryLogDirectory()
    {
        Directory.CreateDirectory(TestsRoot);
        Path = System.IO.Path.Combine(TestsRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string GetPath(params string[] components)
    {
        return components.Aggregate(Path, System.IO.Path.Combine);
    }

    public void Dispose()
    {
        var fullPath = System.IO.Path.GetFullPath(Path);
        var allowedPrefix = TestsRoot.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to delete a directory outside the IceCrow test root.");
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }
}
