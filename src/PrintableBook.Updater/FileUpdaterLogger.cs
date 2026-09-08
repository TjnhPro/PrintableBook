using System.Text;

namespace PrintableBook.Updater;

public sealed class FileUpdaterLogger : IUpdaterLogger
{
    private readonly string logFilePath;

    public FileUpdaterLogger(string logFilePath)
    {
        if (!Path.IsPathFullyQualified(logFilePath)) throw new ArgumentException("Log path must be absolute.", nameof(logFilePath));
        this.logFilePath = Path.GetFullPath(logFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(this.logFilePath)!);
    }

    public void Info(string message) => Write("INFO", message);
    public void Error(string message, Exception exception) => Write("ERROR", $"{message} | {exception.GetType().FullName}: {exception.Message}");

    private void Write(string level, string message) =>
        File.AppendAllText(logFilePath, $"{DateTimeOffset.UtcNow:O} [{level}] {message}{Environment.NewLine}", new UTF8Encoding(false));
}
