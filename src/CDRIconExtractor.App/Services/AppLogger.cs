using System.Text;
using System.IO;

namespace CDRIconExtractor.App.Services;

public sealed class AppLogger
{
    private readonly object _gate = new();
    private readonly string _logRoot;

    public AppLogger()
    {
        _logRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Beiluoguo", "CDRIconExtractor", "Logs");
    }

    public void Info(string message) => Write("INFO", message);
    public void Warning(string message) => Write("WARN", message);
    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} | {exception.GetType().Name}: {exception.Message}");

    public void Timing(string name, long elapsedMilliseconds) => Write("TIME", $"{name}={elapsedMilliseconds}ms");

    private void Write(string level, string message)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_logRoot);
                var path = Path.Combine(_logRoot, $"{DateTime.Now:yyyy-MM-dd}.log");
                var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(path, line, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Logging must never break the extractor workflow.
        }
    }
}
