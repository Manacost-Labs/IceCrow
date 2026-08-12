using System.Text;

namespace IceCrow.Hearthstone.Logs;

public sealed class LogConfigManager : IDisposable
{
    private const int MaximumConfigBytes = 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly (string Key, string Value)[] RequiredPowerValues =
    [
        ("LogLevel", "1"),
        ("FilePrinting", "true"),
        ("Verbose", "true"),
    ];

    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private bool _disposed;

    public async Task<bool> EnsurePowerLoggingAsync(
        HearthstoneLogLocator locator,
        string? productUid = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locator);
        return await EnsurePowerLoggingAsync(
            locator.GetLogConfigPath(productUid),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> EnsurePowerLoggingAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await _updateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var fullPath = Path.GetFullPath(path);
            var existing = await ReadConfigAsync(fullPath, cancellationToken).ConfigureAwait(false);
            var updatedText = EnsurePowerSection(existing.Text);
            if (string.Equals(existing.Text, updatedText, StringComparison.Ordinal))
            {
                return false;
            }

            await WriteAtomicallyAsync(
                fullPath,
                updatedText,
                existing.HasUtf8Preamble,
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _updateGate.Dispose();
    }

    internal static string EnsurePowerSection(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var endsWithNewline = text.EndsWith('\n') || text.EndsWith('\r');
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n').ToList();
        if (endsWithNewline && lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var sectionHeaders = new List<(int Index, string Name)>();
        for (var index = 0; index < lines.Count; index++)
        {
            if (TryGetSectionName(lines[index], out var sectionName))
            {
                sectionHeaders.Add((index, sectionName));
            }
        }

        var powerSections = new List<(int Start, int End)>();
        for (var index = 0; index < sectionHeaders.Count; index++)
        {
            var header = sectionHeaders[index];
            if (!string.Equals(header.Name, "Power", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var end = index + 1 < sectionHeaders.Count ? sectionHeaders[index + 1].Index : lines.Count;
            powerSections.Add((header.Index, end));
        }

        if (powerSections.Count == 0)
        {
            if (lines.Count == 1 && lines[0].Length == 0)
            {
                lines.Clear();
            }
            else if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.Add(string.Empty);
            }

            lines.Add("[Power]");
            lines.AddRange(RequiredPowerValues.Select(value => $"{value.Key}={value.Value}"));
            return string.Join(newline, lines) + newline;
        }

        var foundKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in powerSections)
        {
            for (var index = section.Start + 1; index < section.End; index++)
            {
                if (!TryParseSetting(lines[index], out var key, out var value, out var replacementPrefix, out var suffix))
                {
                    continue;
                }

                var required = RequiredPowerValues.FirstOrDefault(
                    item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
                if (required == default)
                {
                    continue;
                }

                foundKeys.Add(required.Key);
                if (!IsExpectedValue(required.Key, value))
                {
                    lines[index] = $"{replacementPrefix}{required.Key}={required.Value}{suffix}";
                }
            }
        }

        var missing = RequiredPowerValues
            .Where(item => !foundKeys.Contains(item.Key))
            .Select(item => $"{item.Key}={item.Value}")
            .ToArray();

        if (missing.Length > 0)
        {
            var lastSection = powerSections[^1];
            var insertionIndex = lastSection.End;
            while (insertionIndex > lastSection.Start + 1 &&
                   string.IsNullOrWhiteSpace(lines[insertionIndex - 1]))
            {
                insertionIndex--;
            }

            lines.InsertRange(insertionIndex, missing);
        }

        var result = string.Join(newline, lines);
        return endsWithNewline ? result + newline : result;
    }

    private static bool TryGetSectionName(string line, out string name)
    {
        var trimmed = line.Trim();
        if (trimmed.Length >= 3 && trimmed[0] == '[')
        {
            var closingBracket = trimmed.IndexOf(']');
            if (closingBracket > 1)
            {
                var remainder = trimmed[(closingBracket + 1)..].TrimStart();
                if (remainder.Length == 0 || remainder[0] is ';' or '#')
                {
                    name = trimmed[1..closingBracket].Trim();
                    return name.Length > 0;
                }
            }
        }

        name = string.Empty;
        return false;
    }

    private static bool TryParseSetting(
        string line,
        out string key,
        out string value,
        out string replacementPrefix,
        out string suffix)
    {
        key = string.Empty;
        value = string.Empty;
        replacementPrefix = string.Empty;
        suffix = string.Empty;

        var firstContent = 0;
        while (firstContent < line.Length && char.IsWhiteSpace(line[firstContent]))
        {
            firstContent++;
        }

        if (firstContent == line.Length || line[firstContent] is ';' or '#')
        {
            return false;
        }

        var equalsIndex = line.IndexOf('=', firstContent);
        if (equalsIndex < 0)
        {
            return false;
        }

        key = line[firstContent..equalsIndex].Trim();
        if (key.Length == 0)
        {
            return false;
        }

        var rawValue = line[(equalsIndex + 1)..];
        var commentIndex = FindCommentIndex(rawValue);
        if (commentIndex >= 0)
        {
            suffix = rawValue[commentIndex..];
            rawValue = rawValue[..commentIndex];
        }

        value = rawValue.Trim();
        replacementPrefix = line[..firstContent];
        return true;
    }

    private static int FindCommentIndex(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] is ';' or '#')
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsExpectedValue(string key, string value)
    {
        if (string.Equals(key, "LogLevel", StringComparison.OrdinalIgnoreCase))
        {
            return value == "1";
        }

        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";
    }

    private static async Task<(string Text, bool HasUtf8Preamble)> ReadConfigAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return (string.Empty, false);
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaximumConfigBytes)
        {
            throw new InvalidDataException(
                $"Refusing to rewrite log.config because it exceeds {MaximumConfigBytes} bytes.");
        }

        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        var hasPreamble = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        var content = hasPreamble ? bytes.AsSpan(Encoding.UTF8.Preamble.Length) : bytes.AsSpan();
        return (StrictUtf8.GetString(content), hasPreamble);
    }

    private static async Task WriteAtomicallyAsync(
        string path,
        string content,
        bool includeUtf8Preamble,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path) ??
                        throw new ArgumentException("log.config must have a parent directory.", nameof(path));
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        var contentBytes = StrictUtf8.GetBytes(content);

        try
        {
            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                if (includeUtf8Preamble)
                {
                    await stream.WriteAsync(Encoding.UTF8.GetPreamble(), cancellationToken).ConfigureAwait(false);
                }

                await stream.WriteAsync(contentBytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Move(tempPath, path, overwrite: true);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
