using System.Drawing;
using System.Windows.Forms;

namespace SeewoUpdate;

/// <summary>
/// 托盘应用入口，后续会在此挂载版本检测与静默更新流程。
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;

    public TrayApp()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => ExitThread()));

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "希沃白板自动更新",
            ContextMenuStrip = menu,
            Visible = true
        };
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
