namespace IceCrow.ProfileSync.Collection;

/// <summary>
/// Finds complete schema-v3 exports produced by the Manacost HDT Collection
/// Exporter. The locator never creates HDT directories and never scans outside
/// the two documented export locations.
/// </summary>
public sealed class HdtCollectionExportLocator
{
    public const string FullExportPrefix = "hearthstone-collection-";
    public const string FullExportExtension = ".json";
    public const string SharedBaselineFileName = "last-collection-export.json";

    private readonly string _hdtDataDirectory;
    private readonly string _documentsDirectory;

    public HdtCollectionExportLocator(string hdtDataDirectory, string documentsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hdtDataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentsDirectory);
        _hdtDataDirectory = Path.GetFullPath(hdtDataDirectory);
        _documentsDirectory = Path.GetFullPath(documentsDirectory);
    }

    public static HdtCollectionExportLocator CreateDefault()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return new HdtCollectionExportLocator(
            Path.Combine(roaming, "HearthstoneDeckTracker"),
            documents);
    }

    /// <summary>Returns the newest existing full export, or null when none exists.</summary>
    public string? FindLatest()
    {
        var candidates = new List<FileInfo>(3);
        AddIfPresent(candidates, Path.Combine(
            _hdtDataDirectory,
            "HdtCollectionExporter",
            SharedBaselineFileName));
        AddIfPresent(candidates, Path.Combine(
            _hdtDataDirectory,
            "HdtCollectionExporterRu",
            SharedBaselineFileName));

        var exportDirectory = Path.Combine(_documentsDirectory, "HDT Collection Exports");
        if (Directory.Exists(exportDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(
                         exportDirectory,
                         $"{FullExportPrefix}*{FullExportExtension}",
                         SearchOption.TopDirectoryOnly))
            {
                if (IsFullExportFileName(Path.GetFileName(path)))
                {
                    AddIfPresent(candidates, path);
                }
            }
        }

        return candidates
            .OrderByDescending(static candidate => candidate.LastWriteTimeUtc)
            .ThenBy(static candidate => candidate.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(static candidate => candidate.FullName)
            .FirstOrDefault();
    }

    public static bool IsFullExportFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (string.Equals(fileName, SharedBaselineFileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return fileName.StartsWith(FullExportPrefix, StringComparison.OrdinalIgnoreCase) &&
               fileName.EndsWith(FullExportExtension, StringComparison.OrdinalIgnoreCase) &&
               !fileName.Contains("-changes-", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddIfPresent(List<FileInfo> candidates, string path)
    {
        var candidate = new FileInfo(path);
        if (candidate.Exists)
        {
            candidates.Add(candidate);
        }
    }
}
