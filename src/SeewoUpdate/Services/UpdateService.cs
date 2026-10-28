using System.Diagnostics;
using System.IO;

namespace SeewoUpdate.Services;

internal enum UpdateOutcome
{
    /// <summary>已是最新版本。</summary>
    NoUpdate,

    /// <summary>已完成更新，并通过版本校验。</summary>
    Updated,

    /// <summary>未安装希沃白板。</summary>
    NotInstalled,

    /// <summary>检查或更新失败。</summary>
    Failed
}

internal sealed record UpdateReport(UpdateOutcome Outcome, string Message, Version? Installed, Version? Latest);

/// <summary>
/// 进度消息；Notify 为 true 时由托盘弹出气泡提示。
/// </summary>
internal sealed record UpdateProgress(string Message, bool Notify = false);

/// <summary>
/// 完整的更新流程：检测已安装版本 → 查询官方最新版 → 下载 → 结束进程 → 静默安装 → 校验。
/// </summary>
internal static class UpdateService
{
    private const long MinimumInstallerBytes = 5 * 1024 * 1024;

    private static readonly HttpClient DownloadClient = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static async Task<UpdateReport> CheckAndUpdateAsync(
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var installed = EasiNoteDetector.Detect();
        if (installed is null)
        {
            return new UpdateReport(UpdateOutcome.NotInstalled, "未检测到已安装的希沃白板 5", null, null);
        }

        var installedVersion = installed.Version;
        Log.Info($"已安装希沃白板版本：{VersionUtil.Format(installedVersion)}");
        progress?.Report(new UpdateProgress("正在检查希沃白板最新版本…"));

        var release = await SeewoReleaseService.QueryLatestAsync(cancellationToken);
        if (release is null)
        {
            return new UpdateReport(UpdateOutcome.Failed, "无法获取希沃白板最新版本信息", installedVersion, null);
        }

        Log.Info($"官方最新版本：{VersionUtil.Format(release.Version)}");
        if (release.Version <= installedVersion)
        {
            return new UpdateReport(
                UpdateOutcome.NoUpdate,
                $"已是最新版本 {VersionUtil.Format(installedVersion)}",
                installedVersion,
                release.Version);
        }

        progress?.Report(new UpdateProgress(
            $"发现新版本 {VersionUtil.Format(release.Version)}，正在后台下载…", Notify: true));

        string installerPath;
        try
        {
            installerPath = await DownloadAsync(release, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error("下载安装包失败", ex);
            return new UpdateReport(UpdateOutcome.Failed, $"下载安装包失败：{ex.Message}", installedVersion, release.Version);
        }

        int exitCode;
        try
        {
            progress?.Report(new UpdateProgress("正在静默安装希沃白板…", Notify: true));
            exitCode = await InstallAsync(installerPath, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error("静默安装失败", ex);
            return new UpdateReport(UpdateOutcome.Failed, $"静默安装失败：{ex.Message}", installedVersion, release.Version);
        }

        var verified = await VerifyInstalledAsync(release.Version, TimeSpan.FromMinutes(2), cancellationToken);
        if (!verified)
        {
            Log.Warn($"安装程序退出码 {exitCode}，但未检测到版本 {VersionUtil.Format(release.Version)}");
            return new UpdateReport(
                UpdateOutcome.Failed,
                $"安装程序退出码 {exitCode}，但未检测到新版本，请查看日志",
                installedVersion,
                release.Version);
        }

        Log.Info($"希沃白板已更新到 {VersionUtil.Format(release.Version)}（安装程序退出码 {exitCode}）");
        TryDeleteInstaller(installerPath);

        return new UpdateReport(
            UpdateOutcome.Updated,
            $"希沃白板已更新到 {VersionUtil.Format(release.Version)}",
            installedVersion,
            release.Version);
    }

    private static async Task<string> DownloadAsync(
        SeewoRelease release,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        AppPaths.EnsureCreated();

        var fileName = $"EasiNoteSetup_{VersionUtil.Format(release.Version)}_seewo.exe";
        var targetPath = Path.Combine(AppPaths.DownloadsDirectory, fileName);
        var tempPath = targetPath + ".part";

        using (var response = await DownloadClient.GetAsync(
                   release.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength;

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long receivedBytes = 0;
            var lastPercent = -1;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                receivedBytes += read;

                if (totalBytes is > 0)
                {
                    var percent = (int)(receivedBytes * 100 / totalBytes.Value);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress?.Report(new UpdateProgress($"正在下载安装包 {percent}%"));
                    }
                }
            }
        }

        var fileInfo = new FileInfo(tempPath);
        if (fileInfo.Length < MinimumInstallerBytes || !HasExecutableHeader(tempPath))
        {
            TryDeleteFile(tempPath);
            throw new InvalidOperationException("下载的安装包无效");
        }

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        File.Move(tempPath, targetPath);
        Log.Info($"安装包已下载：{targetPath}（{fileInfo.Length / 1024 / 1024} MB）");
        return targetPath;
    }

    private static async Task<int> InstallAsync(string installerPath, CancellationToken cancellationToken)
    {
        StopEasiNoteProcesses();

        var startInfo = new ProcessStartInfo(installerPath)
        {
            // 希沃白板安装包基于 NSIS，/S 表示静默安装
            Arguments = "/S",
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? AppPaths.DownloadsDirectory
        };

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("无法启动安装程序");

        var waitTask = process.WaitForExitAsync(CancellationToken.None);
        var timeoutTask = Task.Delay(TimeSpan.FromMinutes(20), cancellationToken);
        var completed = await Task.WhenAny(waitTask, timeoutTask);
        if (completed != waitTask)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // 忽略结束失败
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("安装程序运行超时");
        }

        return process.ExitCode;
    }

    private static void StopEasiNoteProcesses()
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!process.ProcessName.StartsWith("EasiNote", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Log.Info($"结束进程：{process.ProcessName}（PID {process.Id}）");
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                Log.Warn($"结束希沃白板进程失败：{ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static async Task<bool> VerifyInstalledAsync(
        Version expectedVersion,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var current = EasiNoteDetector.Detect();
            if (current is not null && current.Version >= expectedVersion)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }

        return false;
    }

    private static bool HasExecutableHeader(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return stream.ReadByte() == 'M' && stream.ReadByte() == 'Z';
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteInstaller(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Warn($"删除安装包失败：{ex.Message}");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // 忽略清理失败
        }
    }
}
