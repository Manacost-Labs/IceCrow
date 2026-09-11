using System.Text.Json;

namespace IceCrow.ProfileSync.Outbox;

/// <summary>Atomic latest-only storage for the pending collection snapshot.</summary>
internal sealed class ProfileCollectionFile
{
    private readonly string _path;

    public ProfileCollectionFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = System.IO.Path.GetFullPath(path);
    }

    public void Delete() => File.Delete(_path);

    public async Task<ProfileEvent?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        if (new FileInfo(_path).Length > ProfileEvent.MaximumCollectionPayloadBytes + 1024)
        {
            throw new InvalidDataException("The pending collection snapshot has an invalid size.");
        }

        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var item = await JsonSerializer.DeserializeAsync<ProfileEvent>(
                stream,
                ProfileJson.Options,
                cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The pending collection snapshot is empty.");
            ProfileEvent.Validate(item);
            if (!ProfileEventType.IsLatestOnly(item.Type))
            {
                throw new InvalidDataException("The pending collection file contains a history event.");
            }

            return item;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The pending collection snapshot contains invalid JSON.", exception);
        }
    }

    public async Task WriteAsync(ProfileEvent profileEvent, CancellationToken cancellationToken)
    {
        ProfileEvent.Validate(profileEvent);
        if (!ProfileEventType.IsLatestOnly(profileEvent.Type))
        {
            throw new InvalidDataException("Only a latest-only event can be written as the pending collection.");
        }

        var directory = System.IO.Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("The collection outbox path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    profileEvent,
                    ProfileJson.Options,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

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
}
