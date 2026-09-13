using System.Collections.Immutable;

namespace IceCrow.ProfileSync.History.Decks;

/// <summary>Pure catalog transformations; no file, WPF, or history mutation.</summary>
internal static class DeckLibraryCatalogEditor
{
    public static DeckLibraryCatalog Register(
        DeckLibraryCatalog catalog,
        string name,
        DeckRevisionIdentity identity,
        string? heroCardId)
    {
        var existing = catalog.Families.FirstOrDefault(family =>
            family.Revisions.Any(revision => string.Equals(revision.Key, identity.Key, StringComparison.Ordinal)));
        var definition = existing is null
            ? new DeckFamilyDefinition(
                DeckLibraryProjector.DeterministicId(identity.Key),
                name,
                identity.Format,
                heroCardId,
                [identity])
            : existing with
            {
                Name = name,
                HeroCardId = heroCardId ?? existing.HeroCardId,
            };

        return catalog with
        {
            Families = Replace(catalog.Families, definition),
            ActiveRevisionKey = identity.Key,
        };
    }

    public static DeckLibraryCatalog Merge(
        DeckLibraryCatalog catalog,
        DeckLibrarySnapshot workspace,
        Guid firstId,
        Guid secondId)
    {
        var first = Find(workspace, firstId, "Первая выбранная колода больше не существует.");
        var second = Find(workspace, secondId, "Вторая выбранная колода больше не существует.");
        if (!string.Equals(first.Format, second.Format, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Колоды Стандарта и Вольного режима нельзя объединить.");
        }

        var primary = ChoosePrimary(first, second);
        var secondary = primary.Id == first.Id ? second : first;
        var revisions = primary.Versions
            .Concat(secondary.Versions)
            .Select(static version => version.Identity)
            .DistinctBy(static revision => revision.Key, StringComparer.Ordinal)
            .ToImmutableArray();
        var definition = new DeckFamilyDefinition(
            primary.Id,
            primary.Name ?? secondary.Name ?? DefaultName(primary.Format),
            primary.Format,
            primary.HeroCardId ?? secondary.HeroCardId,
            revisions);
        var removedIds = new HashSet<Guid> { first.Id, second.Id };
        return catalog with
        {
            Families = catalog.Families
                .Where(family => !removedIds.Contains(family.Id))
                .Append(definition)
                .ToImmutableArray(),
        };
    }

    public static Func<DeckLibraryCatalog, DeckLibraryCatalog> ReplaceFamily(
        DeckFamilyDefinition definition) => catalog => catalog with
        {
            Families = Replace(catalog.Families, definition),
        };

    public static Func<DeckLibraryCatalog, DeckLibraryCatalog> SplitFamily(DeckFamilyStatistics family)
    {
        if (family.Versions.Length < 2)
        {
            throw new InvalidOperationException("У этой колоды только одна версия.");
        }

        var definitions = family.Versions.Select((version, index) => new DeckFamilyDefinition(
            DeckLibraryProjector.DeterministicId(version.Identity.Key),
            version.Identity.Key == family.CurrentRevisionKey
                ? family.Name ?? DefaultName(family.Format)
                : $"{family.Name ?? DefaultName(family.Format)} · версия {index + 1}",
            family.Format,
            version.HeroCardId ?? family.HeroCardId,
            [version.Identity])).ToImmutableArray();
        return catalog => catalog with
        {
            Families = catalog.Families
                .Where(definition => definition.Id != family.Id)
                .Concat(definitions)
                .ToImmutableArray(),
        };
    }

    public static DeckFamilyDefinition Materialize(DeckFamilyStatistics family) => new(
        family.Id,
        family.Name ?? DefaultName(family.Format),
        family.Format,
        family.HeroCardId,
        family.Versions.Select(static version => version.Identity).ToImmutableArray());

    private static ImmutableArray<DeckFamilyDefinition> Replace(
        ImmutableArray<DeckFamilyDefinition> families,
        DeckFamilyDefinition definition) => families
        .Where(family => family.Id != definition.Id)
        .Append(definition)
        .ToImmutableArray();

    private static DeckFamilyStatistics Find(
        DeckLibrarySnapshot workspace,
        Guid familyId,
        string message) => workspace.Families.FirstOrDefault(family => family.Id == familyId)
        ?? throw new InvalidOperationException(message);

    private static DeckFamilyStatistics ChoosePrimary(
        DeckFamilyStatistics first,
        DeckFamilyStatistics second)
    {
        if (first.IsActive != second.IsActive)
        {
            return first.IsActive ? first : second;
        }

        return first.LastPlayedAt >= second.LastPlayedAt ? first : second;
    }

    private static string DefaultName(string format) =>
        format == "standard" ? "Колода Стандарта" : "Колода Вольного режима";
}
