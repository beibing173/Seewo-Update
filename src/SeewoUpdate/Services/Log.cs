using System.IO;

namespace SeewoUpdate.Services;

/// <summary>
/// 简单文件日志：按天写入，保留 7 天。
/// </summary>
internal static class Log
{
    private static readonly object Sync = new();

    public static void Init()
    {
        try
        {
            AppPaths.EnsureCreated();
            Cleanup();
        }
        catch
        {
            // 日志不可用时不阻断主流程
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null)
        => Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
            lock (Sync)
            {
                Directory.CreateDirectory(AppPaths.LogsDirectory);
                File.AppendAllText(Path.Combine(AppPaths.LogsDirectory, $"app-{DateTime.Now:yyyyMMdd}.log"), line);
            }
        }
        catch
        {
            // 忽略日志写入失败
        }
    }

    private static void Cleanup()
    {
        var cutoff = DateTime.Now.AddDays(-7);
        foreach (var file in Directory.EnumerateFiles(AppPaths.LogsDirectory, "app-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // 忽略清理失败
            }
        }
    }
}
