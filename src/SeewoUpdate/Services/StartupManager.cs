using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace SeewoUpdate.Services;

/// <summary>
/// 通过“登录时触发 + 最高权限”的计划任务实现开机自启，
/// 这样登录后启动不会弹出 UAC，同时保留静默安装所需的管理员权限。
/// </summary>
internal static class StartupManager
{
    private const string TaskName = "SeewoUpdate";

    public static bool IsEnabled()
    {
        try
        {
            using var process = StartSchtasks("/Query", "/TN", TaskName);
            process.WaitForExit(15000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Warn($"查询开机自启任务失败：{ex.Message}");
            return false;
        }
    }

    public static bool Enable()
    {
        string? xmlPath = null;
        try
        {
            xmlPath = Path.Combine(Path.GetTempPath(), $"SeewoUpdate-{Guid.NewGuid():N}.xml");
            File.WriteAllText(xmlPath, BuildTaskXml(), new System.Text.UnicodeEncoding(bigEndian: false, byteOrderMark: true));

            using var process = StartSchtasks("/Create", "/TN", TaskName, "/XML", xmlPath, "/F");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);

            if (process.HasExited && process.ExitCode == 0)
            {
                Log.Info("已启用开机自启（计划任务，最高权限）");
                return true;
            }

            Log.Warn($"启用开机自启失败：{output} {error}".Trim());
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("启用开机自启异常", ex);
            return false;
        }
        finally
        {
            if (xmlPath is not null)
            {
                try
                {
                    File.Delete(xmlPath);
                }
                catch
                {
                    // 忽略临时文件清理失败
                }
            }
        }
    }

    public static bool Disable()
    {
        try
        {
            using var process = StartSchtasks("/Delete", "/TN", TaskName, "/F");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(15000);

            if (process.HasExited && process.ExitCode == 0)
            {
                Log.Info("已关闭开机自启");
                return true;
            }

            Log.Warn($"关闭开机自启失败：{output} {error}".Trim());
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("关闭开机自启异常", ex);
            return false;
        }
    }

    private static Process StartSchtasks(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 schtasks.exe");
    }

    private static string BuildTaskXml()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value
                  ?? throw new InvalidOperationException("无法获取当前用户 SID");

        var exePath = AppPaths.ExecutablePath;
        var escapedExePath = System.Security.SecurityElement.Escape(exePath);

        return $"""
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>希沃白板自动更新：登录后检查并静默更新希沃白板</Description>
    <URI>\{TaskName}</URI>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{sid}</UserId>
      <Delay>PT15S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>{sid}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>{escapedExePath}</Command>
      <Arguments>--autostart</Arguments>
    </Exec>
  </Actions>
</Task>
""";
    }
}
