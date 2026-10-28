using System.Text.RegularExpressions;

namespace SeewoUpdate.Services;

/// <summary>
/// 版本号解析与比较工具：统一补齐为 4 段数字，便于直接比较。
/// </summary>
internal static class VersionUtil
{
    private static readonly Regex NumberPattern = new(@"\d+(?:\.\d+){1,3}", RegexOptions.Compiled);

    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = NumberPattern.Match(text);
        if (!match.Success)
        {
            return false;
        }

        var numbers = new int[4];
        var parts = match.Value.Split('.');
        for (var i = 0; i < parts.Length && i < numbers.Length; i++)
        {
            if (!int.TryParse(parts[i], out numbers[i]))
            {
                return false;
            }
        }

        version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    public static string Format(Version version)
        => $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
}
