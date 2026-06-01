using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MeetSpace.Client.App.Diagnostics;

public sealed record CrashContext(
    string? SessionId,
    string? CallKind,
    string? Phase,
    string? PeerId);

public sealed class CrashReporter
{
    private readonly string _storageDirectory;
    private readonly int _maxReports;
    private readonly object _writeLock = new();

    public CrashReporter(string storageDirectory, int maxReports = 50)
    {
        _storageDirectory = storageDirectory ?? throw new ArgumentNullException(nameof(storageDirectory));
        _maxReports = Math.Max(1, Math.Min(500, maxReports));
    }

    public void Report(Exception exception, CrashContext? context = null)
    {
        if (exception == null)
            return;

        try
        {
            var report = new Dictionary<string, object?>
            {
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
                ["exceptionType"] = exception.GetType().FullName,
                ["message"] = exception.Message,
                ["stackTrace"] = exception.StackTrace,
                ["sessionId"] = context?.SessionId,
                ["callKind"] = context?.CallKind,
                ["phase"] = context?.Phase,
                ["peerId"] = context?.PeerId,
                ["machineName"] = Environment.MachineName,
                ["osVersion"] = Environment.OSVersion.ToString()
            };

            if (exception.InnerException != null)
            {
                report["innerExceptionType"] = exception.InnerException.GetType().FullName;
                report["innerMessage"] = exception.InnerException.Message;
            }

            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            WriteReportToStorage(json);
        }
        catch
        {
            // CrashReporter itself must never throw
        }
    }

    public IReadOnlyList<string> GetRecentReports(int count = 10)
    {
        try
        {
            if (!Directory.Exists(_storageDirectory))
                return Array.Empty<string>();

            var files = Directory.GetFiles(_storageDirectory, "crash-*.json")
                .OrderByDescending(f => f)
                .Take(Math.Max(1, count))
                .ToList();

            var reports = new List<string>();
            foreach (var file in files)
            {
                try
                {
                    reports.Add(File.ReadAllText(file, Encoding.UTF8));
                }
                catch
                {
                }
            }

            return reports;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private void WriteReportToStorage(string json)
    {
        lock (_writeLock)
        {
            try
            {
                if (!Directory.Exists(_storageDirectory))
                    Directory.CreateDirectory(_storageDirectory);
            }
            catch
            {
                return;
            }

            var fileName = $"crash-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json";
            var filePath = Path.Combine(_storageDirectory, fileName);

            try
            {
                File.WriteAllText(filePath, json, Encoding.UTF8);
            }
            catch
            {
                return;
            }

            PruneOldReports();
        }
    }

    private void PruneOldReports()
    {
        try
        {
            var files = Directory.GetFiles(_storageDirectory, "crash-*.json")
                .OrderByDescending(f => f)
                .ToList();

            if (files.Count <= _maxReports)
                return;

            foreach (var file in files.Skip(_maxReports))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }
}
