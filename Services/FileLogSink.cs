using System.Text;

namespace AvoTelemetryAgent.Services;

/// <summary>
/// Best-effort persistent log sink (daily files) for "install once and forget" deployments.
/// It never throws to callers.
/// </summary>
public sealed class FileLogSink
{
    private readonly object _lock = new();
    private readonly string _dir;

    public FileLogSink()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _dir = Path.Combine(docs, "AvoTelemetryAgent", "Logs");
        try
        {
            Directory.CreateDirectory(_dir);
            PruneOldLogs_NoThrow(maxFiles: 10);
        }
        catch
        {
            // best-effort
        }
    }

    public string DirectoryPath => _dir;

    private string CurrentLogPathUtc()
    {
        var file = $"agent_{DateTime.UtcNow:yyyyMMdd}.log";
        return Path.Combine(_dir, file);
    }

    public void Write(LogEntry e)
    {
        // One line per entry, keep it simple and robust.
        var line = new StringBuilder(256);
        line.Append(e.TimestampUtc.ToString("O"));
        line.Append(' ');
        line.Append('[').Append(e.Level).Append(']');
        line.Append(' ');
        line.Append(e.Category);
        line.Append(": ");
        line.Append(e.Message?.Replace("\r", "").Replace("\n", " ") ?? string.Empty);
        TryAppendLine(line.ToString());
    }

    public void WriteException(string category, Exception ex)
    {
        var msg = $"EXCEPTION {ex.GetType().Name}: {ex.Message} | {ex.StackTrace}";
        TryAppendLine($"{DateTime.UtcNow:O} [Critical] {category}: {msg}");
    }

    private void TryAppendLine(string line)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(_dir);
                var path = CurrentLogPathUtc();
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var sw = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                sw.WriteLine(line);
            }
        }
        catch
        {
            // swallow (never crash the Agent because logging failed)
        }
    }

    private void PruneOldLogs_NoThrow(int maxFiles)
    {
        try
        {
            if (!Directory.Exists(_dir)) return;
            var files = new DirectoryInfo(_dir)
                .GetFiles("agent_*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            foreach (var f in files.Skip(maxFiles))
            {
                try { f.Delete(); } catch { /* ignore */ }
            }
        }
        catch
        {
            // ignore
        }
    }
}
