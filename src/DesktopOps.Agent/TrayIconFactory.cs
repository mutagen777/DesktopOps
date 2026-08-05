using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace DesktopOps.Agent;

internal static class TrayIconFactory
{
    public static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            graphics.Clear(Color.Transparent);

            using (var brush = new SolidBrush(Color.FromArgb(255, 20, 110, 140)))
            {
                graphics.FillEllipse(brush, 1, 1, 29, 29);
            }

            using var font = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            var size = graphics.MeasureString("D", font);
            graphics.DrawString(
                "D",
                font,
                textBrush,
                (32 - size.Width) / 2f,
                (32 - size.Height) / 2f - 1f);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }
}
