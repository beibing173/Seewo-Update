using System.Net;
using System.Text.RegularExpressions;

namespace SeewoUpdate.Services;

/// <summary>
/// 官方发布信息：版本号与安装包下载地址。
/// </summary>
internal sealed record SeewoRelease(Version Version, Uri DownloadUri);

/// <summary>
/// 查询希沃白板官方最新版本。
/// 主方案：请求官方下载入口，从 302 跳转地址的文件名解析版本；
/// 备用方案：从产品页面 HTML 中匹配安装包地址。
/// </summary>
internal static class SeewoReleaseService
{
    private const string DownloadEndpoint = "https://e.seewo.com/download/file?code=EasiNote5";
    private const string ProductPage = "https://e.seewo.com/product/easinote";

    private static readonly Regex SetupNamePattern = new(
        @"EasiNoteSetup_(\d+(?:\.\d+){1,3})_seewo\.exe",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SetupUrlPattern = new(
        @"https?://[^""'\s\\]+EasiNoteSetup_[^""'\s\\]+\.exe",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) SeewoUpdate/1.0");
        return client;
    }

    public static async Task<SeewoRelease?> QueryLatestAsync(CancellationToken cancellationToken)
    {
        foreach (var method in new[] { HttpMethod.Head, HttpMethod.Get })
        {
            try
            {
                var release = await QueryByRedirectAsync(method, cancellationToken);
                if (release is not null)
                {
                    return release;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn($"通过 {method} 查询最新版本失败：{ex.Message}");
            }
        }

        try
        {
            return await QueryFromProductPageAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn($"从产品页面查询最新版本失败：{ex.Message}");
            return null;
        }
    }

    private static async Task<SeewoRelease?> QueryByRedirectAsync(HttpMethod method, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, DownloadEndpoint);
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        var location = response.Headers.Location;
        if (location is null)
        {
            return null;
        }

        if (!location.IsAbsoluteUri)
        {
            location = new Uri(new Uri(DownloadEndpoint), location);
        }

        var decoded = Uri.UnescapeDataString(location.ToString());
        var match = SetupNamePattern.Match(decoded);
        if (!match.Success || !VersionUtil.TryParse(match.Groups[1].Value, out var version))
        {
            return null;
        }

        return new SeewoRelease(version, location);
    }

    private static async Task<SeewoRelease?> QueryFromProductPageAsync(CancellationToken cancellationToken)
    {
        var html = Uri.UnescapeDataString(await Client.GetStringAsync(ProductPage, cancellationToken));

        var nameMatch = SetupNamePattern.Match(html);
        var urlMatch = SetupUrlPattern.Match(html);
        if (!nameMatch.Success || !urlMatch.Success)
        {
            return null;
        }

        if (!VersionUtil.TryParse(nameMatch.Groups[1].Value, out var version)
            || !Uri.TryCreate(urlMatch.Value, UriKind.Absolute, out var downloadUri))
        {
            return null;
        }

        return new SeewoRelease(version, downloadUri);
    }
}
