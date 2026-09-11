using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using IceCrow.Hearthstone.ClientState;
using IceCrow.Hearthstone.Data;
using IceCrow.Hearthstone.Decks;

namespace IceCrow.App.Runtime;

/// <summary>
/// Owns the explicit local deck selection. The selected deck is snapshotted at
/// CREATE_GAME by <see cref="ProfileRecordPipeline"/>; changing this setting
/// during a match therefore cannot rewrite that match's identity.
/// </summary>
internal sealed class ActiveDeckRuntime : IDisposable
{
    internal const int MaximumImportLength = 16 * 1024;
    internal const int MaximumNameLength = 80;
    internal const int MaximumFileBytes = 8 * 1024;

    private readonly string _path;
    private readonly IDeckCodec _codec;
    private readonly ICardDatabase _cards;
    private readonly TimeProvider _timeProvider;
    private readonly Action<ActiveDeckState> _onStateChanged;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ActiveDeckSelection? _selection;
    private bool _disposed;

    public ActiveDeckRuntime(
        string localDataDirectory,
        IDeckCodec codec,
        ICardDatabase cards,
        Action<ActiveDeckState> onStateChanged,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localDataDirectory);
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(onStateChanged);
        _path = Path.GetFullPath(Path.Combine(localDataDirectory, "decks", "active.json"));
        _codec = codec;
        _cards = cards;
        _onStateChanged = onStateChanged;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ActiveDeckSelection? Current => Volatile.Read(ref _selection);

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var loaded = await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _selection, loaded.Selection);
            _onStateChanged(loaded);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ActiveDeckState> ActivateAsync(
        string? requestedName,
        string importText,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var parsed = ParseSelection(requestedName, importText, _timeProvider.GetUtcNow());
        if (parsed.Selection is null)
        {
            _onStateChanged(parsed);
            return parsed;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveCoreAsync(parsed.Selection, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _selection, parsed.Selection);
            _onStateChanged(parsed);
            return parsed;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var failed = new ActiveDeckState(Current, "Не удалось сохранить колоду на этом компьютере.", true);
            _onStateChanged(failed);
            return failed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ActiveDeckState> ClearAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }

            Volatile.Write(ref _selection, null);
            _onStateChanged(ActiveDeckState.Empty);
            return ActiveDeckState.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var failed = new ActiveDeckState(Current, "Не удалось сбросить выбранную колоду.", true);
            _onStateChanged(failed);
            return failed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private ActiveDeckState ParseSelection(
        string? requestedName,
        string importText,
        DateTimeOffset selectedAt)
    {
        if (string.IsNullOrWhiteSpace(importText))
        {
            return Error("Вставьте код колоды или экспорт из Hearthstone.");
        }

        if (importText.Length > MaximumImportLength)
        {
            return Error("Экспорт колоды слишком большой.");
        }

        var input = importText.Trim();
        var export = _codec.ParseExport(input);
        var decoded = export.Success ? null : _codec.Decode(input);
        var deck = export.Export?.Deck ?? decoded?.Deck;
        if (deck is null)
        {
            return Error("Не удалось распознать код колоды.");
        }

        var validation = _codec.Validate(deck);
        if (!validation.IsValid)
        {
            return Error("Колода не прошла проверку формата.");
        }

        var format = deck.Format switch
        {
            DeckFormat.Standard => "standard",
            DeckFormat.Wild => "wild",
            _ => null,
        };
        if (format is null)
        {
            return Error("Статистика колод сейчас поддерживает Стандарт и Вольный режим.");
        }

        var canonicalCode = _codec.Encode(_codec.Canonicalize(deck));
        if (canonicalCode.Length > SelectedDeckSnapshot.MaximumDeckCodeLength)
        {
            return Error("Код колоды превышает допустимый размер.");
        }

        var name = ResolveName(requestedName, export.Export?.Metadata.Name, deck, format);
        if (name is null)
        {
            return Error($"Название должно быть не длиннее {MaximumNameLength} символов.");
        }

        var heroCardId = deck.Heroes.Count > 0 ? _cards.GetByDbfId(deck.Heroes[0])?.CardId : null;
        var snapshot = new SelectedDeckSnapshot(selectedAt, canonicalCode, heroCardId, format, []);
        var selection = new ActiveDeckSelection(name, format, snapshot);
        return new ActiveDeckState(selection, "Колода выбрана. Следующий матч попадёт в её статистику.", false);
    }

    private string? ResolveName(
        string? requestedName,
        string? exportedName,
        DeckDefinition deck,
        string format)
    {
        var candidate = string.IsNullOrWhiteSpace(requestedName) ? exportedName : requestedName;
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            var trimmed = candidate.Trim();
            return trimmed.Length <= MaximumNameLength && !trimmed.Any(char.IsControl)
                ? trimmed
                : null;
        }

        var heroName = deck.Heroes.Count > 0 ? _cards.GetByDbfId(deck.Heroes[0])?.Name : null;
        return heroName is { Length: > 0 }
            ? $"Колода: {heroName}"
            : format == "standard" ? "Колода Стандарта" : "Колода Вольного режима";
    }

    private async Task<ActiveDeckState> LoadCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_path))
            {
                return ActiveDeckState.Empty;
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
                return Error("Сохранённая колода повреждена. Выберите её заново.");
            }

            var bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            var file = JsonSerializer.Deserialize<PersistedDeck>(bytes, JsonOptions);
            if (file is null)
            {
                return Error("Сохранённая колода повреждена. Выберите её заново.");
            }

            var parsed = ParseSelection(file.Name, file.DeckCode, file.SelectedAt);
            if (parsed.Selection is null ||
                !string.Equals(parsed.Selection.Format, file.Format, StringComparison.Ordinal))
            {
                return Error("Сохранённая колода больше не проходит проверку.");
            }

            return parsed with { Message = "Активная колода восстановлена." };
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return Error("Сохранённая колода недоступна. Выберите её заново.");
        }
    }

    private async Task SaveCoreAsync(ActiveDeckSelection selection, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"active.{Guid.NewGuid():N}.tmp");
        try
        {
            var file = new PersistedDeck(
                selection.Name,
                selection.Snapshot.DeckCode!,
                selection.Format,
                selection.Snapshot.ObservedAt);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(file, JsonOptions);
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

    private ActiveDeckState Error(string message) => new(Current, message, true);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record PersistedDeck(
        string Name,
        string DeckCode,
        string Format,
        DateTimeOffset SelectedAt);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 4,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };
}
