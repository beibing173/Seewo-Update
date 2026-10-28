using System.IO;
using Microsoft.Win32;

namespace SeewoUpdate.Services;

/// <summary>
/// 本机已安装的希沃白板信息。
/// </summary>
internal sealed record EasiNoteInstallation(Version Version, string? InstallDirectory, string? UninstallString);

/// <summary>
/// 从注册表卸载项中读取希沃白板的已安装版本。
/// </summary>
internal static class EasiNoteDetector
{
    private static readonly (RegistryHive Hive, RegistryView View)[] Locations =
    {
        (RegistryHive.LocalMachine, RegistryView.Registry64),
        (RegistryHive.LocalMachine, RegistryView.Registry32),
        (RegistryHive.CurrentUser, RegistryView.Registry64),
        (RegistryHive.CurrentUser, RegistryView.Registry32)
    };

    public static EasiNoteInstallation? Detect()
    {
        EasiNoteInstallation? found = null;

        foreach (var (hive, view) in Locations)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstallKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstallKey is null)
                {
                    continue;
                }

                foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                {
                    using var appKey = uninstallKey.OpenSubKey(subKeyName);
                    if (appKey is null)
                    {
                        continue;
                    }

                    var displayName = appKey.GetValue("DisplayName") as string;
                    if (displayName is null || !IsEasiNote(displayName))
                    {
                        continue;
                    }

                    if (!VersionUtil.TryParse(appKey.GetValue("DisplayVersion") as string, out var version))
                    {
                        continue;
                    }

                    var uninstallString = (appKey.GetValue("UninstallString") as string)?.Trim().Trim('"');
                    var installDirectory = (appKey.GetValue("InstallLocation") as string)?.Trim().Trim('"');
                    if (string.IsNullOrEmpty(installDirectory) && !string.IsNullOrEmpty(uninstallString))
                    {
                        installDirectory = Path.GetDirectoryName(uninstallString);
                    }

                    var candidate = new EasiNoteInstallation(version, installDirectory, uninstallString);
                    if (found is null || candidate.Version > found.Version)
                    {
                        found = candidate;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"读取注册表卸载项失败（{hive}/{view}）：{ex.Message}");
            }
        }

        return found;
    }

    private static bool IsEasiNote(string displayName)
        => displayName.Contains("希沃白板", StringComparison.OrdinalIgnoreCase)
           || displayName.Contains("EasiNote", StringComparison.OrdinalIgnoreCase);
}
