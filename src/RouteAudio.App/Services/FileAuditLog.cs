using System.IO;
using System.Text;
using RouteAudio.Core.Abstractions;

namespace RouteAudio.App.Services;

/// <summary>极简文件日志：按天分文件、单文件 10 MB 上限、保留 7 天。</summary>
public sealed class FileAuditLog : IAuditLog
{
    private readonly object _gate = new();
    private readonly string _directory;

    public FileAuditLog()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RouteAudio",
            "logs");
        Directory.CreateDirectory(_directory);
        CleanupOldLogs();
    }

    public string DirectoryPath => _directory;

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message} :: {exception.GetType().Name}: {exception.Message}");

    private void Write(string level, string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
            lock (_gate)
            {
                var path = Path.Combine(_directory, $"app-{DateTime.Now:yyyyMMdd}.log");
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 10 * 1024 * 1024)
                {
                    File.Move(path, Path.Combine(_directory, $"app-{DateTime.Now:yyyyMMdd}-{DateTime.Now:HHmmss}.log"), overwrite: true);
                }

                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch
        {
            // 日志失败不得影响主流程
        }
    }

    private void CleanupOldLogs()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_directory, "app-*.log"))
            {
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-7))
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // 忽略
        }
    }
}
