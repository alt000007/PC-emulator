using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PocketPC.Companion;

/// <summary>Draws the tray/window icon at runtime so the repo needs no binary assets.</summary>
internal static class AppIcon
{
    private static Icon? _cached;

    public static Icon Get() => _cached ??= Create(Color.FromArgb(0x2D, 0x7D, 0xF6));

    private static Icon Create(Color accent)
    {
        using var bitmap = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // A phone outline with a small "screen" — reads well at 16 px.
            using var body = RoundedRect(new Rectangle(14, 2, 36, 60), 9);
            using var fill = new SolidBrush(accent);
            g.FillPath(fill, body);
            using var screen = new SolidBrush(Color.White);
            g.FillRectangle(screen, 19, 10, 26, 38);
            g.FillEllipse(screen, 28, 52, 8, 6);
            using var play = new SolidBrush(accent);
            g.FillPolygon(play, [new Point(27, 20), new Point(27, 38), new Point(40, 29)]);
        }

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
