using System.Collections.Generic;

namespace HarnessMcp.Infrastructure.Postgres;

public sealed class LogFileRoller
{
    private readonly object _sync = new();
    private readonly string _directory;
    private readonly string _prefix;
    private readonly long _maxFileSizeBytes;
    private readonly int _maxRetainedFiles;
    private readonly string _currentPath;

    public LogFileRoller(
        string directory,
        string prefix,
        long maxFileSizeBytes,
        int maxRetainedFiles)
    {
        _directory = string.IsNullOrWhiteSpace(directory) ? "." : directory;
        _prefix = string.IsNullOrWhiteSpace(prefix) ? "harness-mcp" : prefix;
        _maxFileSizeBytes = maxFileSizeBytes <= 0 ? 10 * 1024 * 1024 : maxFileSizeBytes;
        _maxRetainedFiles = maxRetainedFiles <= 0 ? 10 : maxRetainedFiles;
        _currentPath = Path.Combine(_directory, $"{_prefix}.log");
        Directory.CreateDirectory(_directory);
    }

    public string EnsureCurrentFile()
    {
        lock (_sync)
        {
            RotateIfNeeded();
            return _currentPath;
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_currentPath))
            return;

        var size = new FileInfo(_currentPath).Length;
        if (size < _maxFileSizeBytes)
            return;

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var rotatedPath = Path.Combine(_directory, $"{_prefix}.{stamp}.log");

        try
        {
            File.Move(_currentPath, rotatedPath, overwrite: true);
        }
        catch
        {
            // If rotation fails (file lock, etc), keep writing to the current file.
            return;
        }

        PruneOld(rotatedPath);
    }

    private void PruneOld(string newestPath)
    {
        var files = Directory.GetFiles(_directory, $"{_prefix}.*.log");
        if (files.Length <= _maxRetainedFiles)
            return;

        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        var keep = Math.Max(0, _maxRetainedFiles - 1); // exclude newestPath already in list

        var toDelete = new List<string>();
        for (int i = 0; i < files.Length; i++)
        {
            if (files[i].Equals(newestPath, StringComparison.OrdinalIgnoreCase))
                continue;
            toDelete.Add(files[i]);
        }

        // Sort again for stable pruning
        toDelete.Sort(StringComparer.OrdinalIgnoreCase);
        var excess = toDelete.Count - keep;
        for (int i = 0; i < excess; i++)
        {
            try { File.Delete(toDelete[i]); } catch { /* ignore */ }
        }
    }
}

