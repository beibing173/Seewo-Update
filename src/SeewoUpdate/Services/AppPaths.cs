using System.IO;
using System.Windows.Forms;

namespace SeewoUpdate.Services;

/// <summary>
/// 应用数据、下载与日志的固定路径（所有用户可写）。
/// </summary>
internal static class AppPaths
{
    public static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SeewoUpdate");

    public static readonly string DownloadsDirectory = Path.Combine(DataDirectory, "downloads");

    public static readonly string LogsDirectory = Path.Combine(DataDirectory, "logs");

    public static string ExecutablePath => Environment.ProcessPath ?? Application.ExecutablePath;

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(DownloadsDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
