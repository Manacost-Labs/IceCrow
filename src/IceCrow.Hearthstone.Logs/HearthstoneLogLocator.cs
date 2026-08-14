using System.ComponentModel;
using System.Diagnostics;

namespace IceCrow.Hearthstone.Logs;

public sealed class HearthstoneLogLocator
{
    private const string DefaultProductUid = "hs_beta";
    private const string LogConfigFileName = "log.config";
    private const string PowerLogFileName = "Power.log";
    private readonly string _hearthstoneAppData;
    private readonly string[] _configuredLogRoots;
    private readonly bool _useDefaultLogRoots;

    public HearthstoneLogLocator(
        string? localApplicationData = null,
        IEnumerable<string>? logRoots = null)
    {
        var localData = localApplicationData ??
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _hearthstoneAppData = Path.GetFullPath(Path.Combine(localData, "Blizzard", "Hearthstone"));
        _useDefaultLogRoots = logRoots is null;
        _configuredLogRoots = (logRoots ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public event Action<Exception>? RecoverableError;

    public string GetLogConfigPath(string? productUid = null)
    {
        if (string.IsNullOrWhiteSpace(productUid) ||
            string.Equals(productUid, DefaultProductUid, StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(_hearthstoneAppData, LogConfigFileName);
        }

        ValidateProductUid(productUid);
        return Path.Combine(_hearthstoneAppData, productUid, LogConfigFileName);
    }

    public IReadOnlyList<string> GetLogRoots()
    {
        var roots = new HashSet<string>(_configuredLogRoots, StringComparer.OrdinalIgnoreCase);

        if (_useDefaultLogRoots)
        {
            roots.Add(Path.Combine(_hearthstoneAppData, "Logs"));
            if (OperatingSystem.IsWindows())
            {
                AddRunningClientLogRoots(roots);
            }
        }

        return roots.ToArray();
    }

    public string? FindPowerLog()
    {
        FileInfo? newest = null;

        foreach (var root in GetLogRoots())
        {
            foreach (var candidate in EnumeratePowerLogs(root))
            {
                if (newest is null ||
                    candidate.LastWriteTimeUtc > newest.LastWriteTimeUtc ||
                    (candidate.LastWriteTimeUtc == newest.LastWriteTimeUtc &&
                     candidate.CreationTimeUtc > newest.CreationTimeUtc))
                {
                    newest = candidate;
                }
            }
        }

        return newest?.FullName;
    }

    private static void ValidateProductUid(string productUid)
    {
        if (productUid is "." or ".." ||
            !string.Equals(productUid, Path.GetFileName(productUid), StringComparison.Ordinal) ||
            productUid.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The product UID must be a single valid path component.", nameof(productUid));
        }
    }

    private void AddRunningClientLogRoots(HashSet<string> roots)
    {
        Process[] processes;

        try
        {
            processes = Process.GetProcessesByName("Hearthstone");
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            RecoverableError?.Invoke(exception);
            return;
        }

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    var executable = process.MainModule?.FileName;
                    var directory = executable is null ? null : Path.GetDirectoryName(executable);
                    if (directory is not null)
                    {
                        roots.Add(Path.Combine(directory, "Logs"));
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    RecoverableError?.Invoke(exception);
                }
            }
        }
    }

    private IEnumerable<FileInfo> EnumeratePowerLogs(string root)
    {
        var directory = new DirectoryInfo(root);
        if (!directory.Exists)
        {
            yield break;
        }

        var directFile = new FileInfo(Path.Combine(directory.FullName, PowerLogFileName));
        if (directFile.Exists)
        {
            yield return directFile;
        }

        DirectoryInfo[] sessionDirectories;
        try
        {
            sessionDirectories = directory.GetDirectories();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            RecoverableError?.Invoke(exception);
            yield break;
        }

        foreach (var sessionDirectory in sessionDirectories)
        {
            var file = new FileInfo(Path.Combine(sessionDirectory.FullName, PowerLogFileName));
            if (file.Exists)
            {
                yield return file;
            }
        }
    }
}
