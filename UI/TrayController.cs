using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace NothingOnWindows.UI;

public sealed class TrayController : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ContextMenuStrip menu;
    private readonly Drawing.Icon artwork;
    public bool Visible => icon.Visible;
    public TrayController(Action show, Action settings, Action exit)
    {
        artwork = CreateIcon();
        menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Nothing on Windows", null, (_, _) => show());
        menu.Items.Add("Settings", null, (_, _) => settings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());
        icon = new Forms.NotifyIcon { Icon = artwork, Text = "Nothing on Windows", ContextMenuStrip = menu };
        icon.DoubleClick += (_, _) => show();
        icon.Visible = true;
    }
    public void Update(string text) => icon.Text = text.Length > 63 ? text[..63] : text;
    internal void InvokeMenu(int index) => menu.Items[index].PerformClick();
    public void Dispose() { icon.Visible = false; icon.Dispose(); menu.Dispose(); artwork.Dispose(); }
    private static Drawing.Icon CreateIcon()
    {
        var resource=System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/headphones.ico"))??throw new InvalidOperationException("App icon resource is missing.");
        using var stream=resource.Stream;
        using var original=new Drawing.Icon(stream,32,32);
        return (Drawing.Icon)original.Clone();
    }
}
