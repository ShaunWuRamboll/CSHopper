using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace CSHopper;

/// Draws the CSHopper component/plugin icon in code, so no binary image asset needs to ship with the repo.
public static class CSHopperIcon
{
    public static Bitmap Create(int size = 24)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        var chipRect = new RectangleF(0.5f, 0.5f, size - 1, size - 1);
        using (var path = RoundedRect(chipRect, size * 0.22f))
        using (var bg = new SolidBrush(Color.FromArgb(255, 36, 114, 153)))
        using (var border = new Pen(Color.FromArgb(255, 18, 64, 92), 1f))
        {
            g.FillPath(bg, path);
            g.DrawPath(border, path);
        }

        using (var font = new Font("Consolas", size * 0.46f, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            var textSize = g.MeasureString("C#", font);
            var textPos = new PointF((size - textSize.Width) / 2f, (size - textSize.Height) / 2f - size * 0.04f);
            g.DrawString("C#", font, Brushes.White, textPos);
        }

        // Small green "sync" badge in the corner marks this as the live-synced variant of the native C# component.
        float badgeSize = size * 0.46f;
        var badgeRect = new RectangleF(size - badgeSize - 0.5f, size - badgeSize - 0.5f, badgeSize, badgeSize);
        using (var badgeBg = new SolidBrush(Color.FromArgb(255, 86, 171, 96)))
        using (var badgeBorder = new Pen(Color.White, badgeSize * 0.1f))
        {
            g.FillEllipse(badgeBg, badgeRect);
            g.DrawEllipse(badgeBorder, badgeRect);
        }
        using (var badgeFont = new Font("Segoe UI", badgeSize * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            var glyphSize = g.MeasureString("\u21BB", badgeFont);
            var glyphPos = new PointF(
                badgeRect.X + (badgeRect.Width - glyphSize.Width) / 2f,
                badgeRect.Y + (badgeRect.Height - glyphSize.Height) / 2f);
            g.DrawString("\u21BB", badgeFont, Brushes.White, glyphPos);
        }

        return bmp;
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
