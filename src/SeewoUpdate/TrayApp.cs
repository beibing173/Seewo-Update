using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using SeewoUpdate.Services;

namespace SeewoUpdate;

/// <summary>
/// 托盘应用：目前提供开机自启开关、日志入口与退出。
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _startupItem;

    public TrayApp(string[] args)
    {
        _startupItem = new ToolStripMenuItem("开机自动运行")
        {
            CheckOnClick = true,
            Checked = StartupManager.IsEnabled()
        };
        _startupItem.Click += OnStartupItemClick;

        var menu = new ContextMenuStrip();
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripMenuItem("打开日志文件夹", null, (_, _) => OpenLogFolder()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => ExitApplication()));

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "希沃白板自动更新",
            ContextMenuStrip = menu,
            Visible = true
        };
    }

    private void OnStartupItemClick(object? sender, EventArgs e)
    {
        var enabled = _startupItem.Checked;
        var succeeded = enabled ? StartupManager.Enable() : StartupManager.Disable();
        if (succeeded)
        {
            ShowBalloon("希沃白板自动更新", enabled ? "已启用开机自动更新" : "已关闭开机自动更新");
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
        _notifyIcon.Visible = false;
        ExitThread();
    }

    private static Icon LoadIcon()
    {
        using var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("SeewoUpdate.app.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
