using System.Text.Json;

namespace IceCrow.ProfileSync.History.Decks;

internal sealed class DeckLibraryFile
{
    internal const int MaximumFileBytes = 1024 * 1024;
    internal const int MaximumFamilies = 256;
    internal const int MaximumRevisions = 1024;
    internal const int MaximumNameLength = 80;

    private readonly string _path;

    public DeckLibraryFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<DeckLibraryCatalog> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return DeckLibraryCatalog.Empty;
        }

        await using var stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length is <= 0 or > MaximumFileBytes)
        {
            throw new InvalidDataException("The deck catalog file is outside its size limit.");
        }

        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        var catalog = JsonSerializer.Deserialize<DeckLibraryCatalog>(bytes, ProfileJson.Options)
            ?? throw new InvalidDataException("The deck catalog is empty.");
        Validate(catalog);
        return catalog;
    }

    public async Task SaveAsync(DeckLibraryCatalog catalog, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Validate(catalog);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(catalog, ProfileJson.Options);
        if (bytes.Length > MaximumFileBytes)
        {
            throw new InvalidDataException("The deck catalog exceeded its storage limit.");
        }

        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("The deck catalog path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void Validate(DeckLibraryCatalog catalog)
    {
        if (catalog.FormatVersion != DeckLibraryCatalog.CurrentFormatVersion ||
            catalog.Families.Length > MaximumFamilies ||
            catalog.Families.Sum(static family => family.Revisions.Length) > MaximumRevisions)
        {
            throw new InvalidDataException("The deck catalog is outside its contract limits.");
        }

        var familyIds = new HashSet<Guid>();
        var revisionKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var family in catalog.Families)
        {
            ValidateFamily(family, familyIds, revisionKeys);
        }

        if (catalog.ActiveRevisionKey is not null &&
            (catalog.ActiveRevisionKey.Length != 64 || !revisionKeys.Contains(catalog.ActiveRevisionKey)))
        {
            throw new InvalidDataException("The active deck revision is not present in the catalog.");
        }
    }

    private static void ValidateFamily(
        DeckFamilyDefinition family,
        HashSet<Guid> familyIds,
        HashSet<string> revisionKeys)
    {
        if (family.Id == Guid.Empty || !familyIds.Add(family.Id) ||
            family.Format is not ("standard" or "wild") ||
            string.IsNullOrWhiteSpace(family.Name) ||
            family.Name.Length > MaximumNameLength ||
            family.Name.Any(char.IsControl) ||
            (family.HeroCardId is { Length: > 64 } || family.HeroCardId?.Any(char.IsControl) == true) ||
            family.Revisions.IsDefaultOrEmpty)
        {
            throw new InvalidDataException("A deck family is outside its contract limits.");
        }

        foreach (var revision in family.Revisions)
        {
            revision.Validate();
            if (!string.Equals(revision.Format, family.Format, StringComparison.Ordinal) ||
                !revisionKeys.Add(revision.Key))
            {
                throw new InvalidDataException("A deck revision belongs to multiple or incompatible families.");
            }
        }
    }
}
