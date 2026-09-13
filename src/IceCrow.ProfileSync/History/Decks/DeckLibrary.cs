namespace IceCrow.ProfileSync.History.Decks;

/// <summary>
/// Owns reversible user grouping metadata over immutable match history. Match
/// records and exact revision identities are never rewritten by this module.
/// </summary>
public sealed class DeckLibrary : IDisposable
{
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly DeckLibraryFile _file;
    private DeckLibraryCatalog _catalog = DeckLibraryCatalog.Empty;
    private ProfileHistorySnapshot _history = ProfileHistorySnapshot.Empty;
    private DeckLibrarySnapshot _snapshot = DeckLibrarySnapshot.Empty;
    private bool _disposed;

    public DeckLibrary(string path)
    {
        _file = new DeckLibraryFile(path);
    }

    public event Action<DeckLibrarySnapshot>? SnapshotChanged;

    public DeckLibrarySnapshot Snapshot
    {
        get
        {
            lock (_stateLock)
            {
                return _snapshot;
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                var loaded = await _file.LoadAsync(cancellationToken).ConfigureAwait(false);
                Publish(loaded, "Каталог колод загружен.", false);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
            {
                Publish(DeckLibraryCatalog.Empty, "Каталог колод повреждён или недоступен. История матчей не изменена.", true);
            }
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public void ApplyHistory(ProfileHistorySnapshot history)
    {
        ArgumentNullException.ThrowIfNull(history);
        ThrowIfDisposed();
        DeckLibrarySnapshot snapshot;
        lock (_stateLock)
        {
            _history = history;
            _snapshot = DeckLibraryProjector.Create(history, _catalog, _snapshot.Message, _snapshot.IsError);
            snapshot = _snapshot;
        }

        SnapshotChanged?.Invoke(snapshot);
    }

    public Task<DeckLibraryOperationResult> RegisterSelectionAsync(
        string name,
        string format,
        string deckCode,
        string? heroCardId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmedName = name.Trim();
        if (trimmedName.Length > DeckLibraryFile.MaximumNameLength || trimmedName.Any(char.IsControl))
        {
            return Task.FromResult(Failure("Название колоды недопустимо."));
        }

        var identity = DeckRevisionIdentity.FromCode(format, deckCode);
        return MutateAsync(
            catalog => DeckLibraryCatalogEditor.Register(catalog, trimmedName, identity, heroCardId),
            "Колода добавлена в библиотеку и выбрана для следующего матча.",
            cancellationToken);
    }

    public Task<DeckLibraryOperationResult> ClearActiveAsync(CancellationToken cancellationToken = default) =>
        MutateAsync(
            catalog => catalog with { ActiveRevisionKey = null },
            "Активная колода сброшена. Сохранённые версии и матчи остались в библиотеке.",
            cancellationToken);

    public Task<DeckLibraryOperationResult> RenameAsync(
        Guid familyId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var trimmedName = name?.Trim();
        if (familyId == Guid.Empty || string.IsNullOrWhiteSpace(trimmedName) ||
            trimmedName.Length > DeckLibraryFile.MaximumNameLength || trimmedName.Any(char.IsControl))
        {
            return Task.FromResult(Failure("Введите корректное название колоды."));
        }

        return MutateWorkspaceAsync(
            familyId,
            family => DeckLibraryCatalogEditor.ReplaceFamily(
                DeckLibraryCatalogEditor.Materialize(family) with { Name = trimmedName }),
            "Колода переименована.",
            cancellationToken);
    }

    public Task<DeckLibraryOperationResult> MergeAsync(
        Guid firstFamilyId,
        Guid secondFamilyId,
        CancellationToken cancellationToken = default)
    {
        if (firstFamilyId == Guid.Empty || secondFamilyId == Guid.Empty || firstFamilyId == secondFamilyId)
        {
            return Task.FromResult(Failure("Для объединения выберите две разные колоды."));
        }

        return MutateAsync(catalog => DeckLibraryCatalogEditor.Merge(
                catalog,
                Snapshot,
                firstFamilyId,
                secondFamilyId),
            "Сборки объединены как версии одной колоды. История матчей сохранена без изменений.",
            cancellationToken);
    }

    public Task<DeckLibraryOperationResult> SeparateAsync(
        Guid familyId,
        CancellationToken cancellationToken = default) =>
        MutateWorkspaceAsync(
            familyId,
            DeckLibraryCatalogEditor.SplitFamily,
            "Версии снова разделены на самостоятельные колоды.",
            cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mutationGate.Dispose();
    }

    private async Task<DeckLibraryOperationResult> MutateWorkspaceAsync(
        Guid familyId,
        Func<DeckFamilyStatistics, Func<DeckLibraryCatalog, DeckLibraryCatalog>> mutation,
        string message,
        CancellationToken cancellationToken)
    {
        DeckFamilyStatistics? family;
        lock (_stateLock)
        {
            family = _snapshot.Families.FirstOrDefault(candidate => candidate.Id == familyId);
        }

        return family is null
            ? Failure("Выбранная колода больше не существует.")
            : await MutateAsync(mutation(family), message, cancellationToken).ConfigureAwait(false);
    }

    private async Task<DeckLibraryOperationResult> MutateAsync(
        Func<DeckLibraryCatalog, DeckLibraryCatalog> mutation,
        string message,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DeckLibraryCatalog current;
            lock (_stateLock)
            {
                current = _catalog;
            }

            DeckLibraryCatalog next;
            try
            {
                next = mutation(current);
                await _file.SaveAsync(next, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                return Failure(exception.Message);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                return Failure("Не удалось сохранить изменения библиотеки колод.");
            }

            var snapshot = Publish(next, message, false);
            return new DeckLibraryOperationResult(true, message, snapshot);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private DeckLibrarySnapshot Publish(DeckLibraryCatalog catalog, string message, bool isError)
    {
        DeckLibrarySnapshot snapshot;
        lock (_stateLock)
        {
            _catalog = catalog;
            _snapshot = DeckLibraryProjector.Create(_history, catalog, message, isError);
            snapshot = _snapshot;
        }

        SnapshotChanged?.Invoke(snapshot);
        return snapshot;
    }

    private DeckLibraryOperationResult Failure(string message)
    {
        DeckLibrarySnapshot snapshot;
        lock (_stateLock)
        {
            _snapshot = _snapshot with { Message = message, IsError = true };
            snapshot = _snapshot;
        }

        SnapshotChanged?.Invoke(snapshot);
        return new DeckLibraryOperationResult(false, message, snapshot);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
