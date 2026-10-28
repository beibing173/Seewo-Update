using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using SeewoUpdate.Services;

namespace SeewoUpdate;

/// <summary>
/// 托盘应用：开机静默检查更新、手动检查、开机自启开关、日志入口与退出。
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private const string DefaultTooltip = "希沃白板自动更新";

    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _checkItem;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _busy;

    public TrayApp(string[] args)
    {
        _checkItem = new ToolStripMenuItem("立即检查更新", null, async (_, _) => await RunUpdateAsync(isManual: true));

        _startupItem = new ToolStripMenuItem("开机自动运行")
        {
            CheckOnClick = true,
            Checked = StartupManager.IsEnabled()
        };
        _startupItem.Click += OnStartupItemClick;

        var menu = new ContextMenuStrip();
        menu.Items.Add(_checkItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripMenuItem("打开日志文件夹", null, (_, _) => OpenLogFolder()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => ExitApplication()));

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = DefaultTooltip,
            ContextMenuStrip = menu,
            Visible = true
        };

        if (args.Contains("--autostart", StringComparer.OrdinalIgnoreCase))
        {
            StartAutostartCheck();
        }
    }

    private async void StartAutostartCheck()
    {
        try
        {
            // 登录后稍等片刻，避开开机时的磁盘与网络争抢
            await Task.Delay(TimeSpan.FromSeconds(10), _cancellation.Token);
            await RunUpdateAsync(isManual: false);
        }
        catch (OperationCanceledException)
        {
            // 用户退出程序
        }
    }

    private async Task RunUpdateAsync(bool isManual)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _checkItem.Enabled = false;

        var progress = new Progress<UpdateProgress>(update =>
        {
            Log.Info(update.Message);
            _notifyIcon.Text = Truncate($"{DefaultTooltip} - {update.Message}");

            if (update.Notify)
            {
                ShowBalloon(DefaultTooltip, update.Message);
            }
        });

        try
        {
            var report = await UpdateService.CheckAndUpdateAsync(progress, _cancellation.Token);
            Log.Info($"更新结果：{report.Outcome}，{report.Message}");

            // 开机自启且无新版本时保持静默，不做任何提示
            switch (report.Outcome)
            {
                case UpdateOutcome.Updated:
                    ShowBalloon("更新完成", report.Message);
                    break;
                case UpdateOutcome.Failed:
                    ShowBalloon("更新失败", report.Message, ToolTipIcon.Error);
                    break;
                case UpdateOutcome.NoUpdate when isManual:
                    ShowBalloon("已是最新版本", report.Message);
                    break;
                case UpdateOutcome.NotInstalled when isManual:
                    ShowBalloon("未检测到希沃白板", report.Message, ToolTipIcon.Warning);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // 用户退出程序
        }
        catch (Exception ex)
        {
            Log.Error("检查更新异常", ex);
            ShowBalloon("更新失败", ex.Message, ToolTipIcon.Error);
        }
        finally
        {
            _busy = false;
            _checkItem.Enabled = true;
            _notifyIcon.Text = DefaultTooltip;
        }
    }

    private void OnStartupItemClick(object? sender, EventArgs e)
    {
        var enabled = _startupItem.Checked;
        var succeeded = enabled ? StartupManager.Enable() : StartupManager.Disable();
        if (succeeded)
        {
            ShowBalloon(DefaultTooltip, enabled ? "已启用开机自动更新" : "已关闭开机自动更新");
            return;
        }

        _startupItem.Checked = !enabled;
        ShowBalloon("设置失败", "请以管理员身份运行本程序后重试", ToolTipIcon.Error);
    }

    private static void OpenLogFolder()
    {
        AppPaths.EnsureCreated();
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.LogsDirectory}\"")
        {
            UseShellExecute = true
        });
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(5000);
    }

    private void ExitApplication()
    {
        _cancellation.Cancel();
        _notifyIcon.Visible = false;
        ExitThread();
    }

    private static string Truncate(string text)
        => text.Length <= 60 ? text : text[..59] + "…";

    private static Icon LoadIcon()
    {
        using var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("SeewoUpdate.app.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
