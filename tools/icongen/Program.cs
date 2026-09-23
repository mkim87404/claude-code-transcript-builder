// Generates the app icon (multi-resolution .ico, PNG-encoded frames): a terminal window with
// transcript lines and an active ">_" prompt on an indigo-to-violet squircle.
//
// Dependencies: .NET 10 SDK on Windows (System.Drawing/GDI+ via UseWindowsForms in icongen.csproj).
// Run:          dotnet run --project tools/icongen -- TranscriptBuilder/Assets/app.ico
//               (optional 2nd argument: path for a 256px PNG preview)

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

int[] frameSizes = [16, 32, 48, 64, 128, 256];

var outputPath = args.Length > 0 ? args[0] : "app.ico";
WriteIco(outputPath, frameSizes);

if (args.Length > 1)
{
    using var preview = Draw(256);
    preview.Save(args[1], ImageFormat.Png);
}

Console.WriteLine($"Wrote {outputPath}");

static GraphicsPath RoundedRect(RectangleF rect, float radius)
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

static GraphicsPath RoundedRectTopOnly(RectangleF rect, float radius)
{
    var path = new GraphicsPath();
    float d = radius * 2;
    path.AddArc(rect.X, rect.Y, d, d, 180, 90);
    path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
    path.AddLine(rect.Right, rect.Bottom, rect.X, rect.Bottom);
    path.CloseFigure();
    return path;
}

static Bitmap Draw(int size)
{
    var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.Clear(Color.Transparent);

    float s = size;

    // App-icon squircle background, indigo -> violet gradient.
    using (var bgPath = RoundedRect(new RectangleF(0, 0, s, s), s * 0.20f))
    using (var bgBrush = new LinearGradientBrush(new PointF(0, 0), new PointF(s, s),
               Color.FromArgb(255, 79, 70, 229), Color.FromArgb(255, 124, 58, 237)))
    {
        g.FillPath(bgBrush, bgPath);
    }

    // Terminal window.
    float margin = s * 0.14f;
    var winRect = new RectangleF(margin, margin, s - margin * 2, s - margin * 2);
    float winRadius = s * 0.09f;
    using (var winPath = RoundedRect(winRect, winRadius))
    using (var winBrush = new SolidBrush(Color.FromArgb(255, 15, 23, 42)))
    {
        g.FillPath(winBrush, winPath);
    }

    // Title bar with traffic-light dots.
    float titleH = winRect.Height * 0.17f;
    var titleRect = new RectangleF(winRect.X, winRect.Y, winRect.Width, titleH);
    using (var titlePath = RoundedRectTopOnly(titleRect, winRadius))
    using (var titleBrush = new SolidBrush(Color.FromArgb(255, 30, 41, 59)))
    {
        g.FillPath(titleBrush, titlePath);
    }

    float dotR = titleH * 0.20f;
    float dotY = titleRect.Y + titleH / 2f;
    float dotStartX = titleRect.X + titleH * 0.55f;
    float dotSpacing = titleH * 0.85f;
    Color[] dotColors = [Color.FromArgb(255, 248, 113, 113), Color.FromArgb(255, 251, 191, 36), Color.FromArgb(255, 52, 211, 153)];
    for (int i = 0; i < dotColors.Length; i++)
    {
        using var dotBrush = new SolidBrush(dotColors[i]);
        float cx = dotStartX + i * dotSpacing;
        g.FillEllipse(dotBrush, cx - dotR, dotY - dotR, dotR * 2, dotR * 2);
    }

    // Transcript lines (prior logged entries).
    float bodyTop = titleRect.Bottom + winRect.Height * 0.11f;
    float lineH = winRect.Height * 0.075f;
    float lineGap = winRect.Height * 0.135f;
    float lineX = winRect.X + winRect.Width * 0.11f;
    float[] lineWidths = [0.55f, 0.72f, 0.40f];
    using (var lineBrush = new SolidBrush(Color.FromArgb(255, 100, 116, 139)))
    {
        for (int i = 0; i < lineWidths.Length; i++)
        {
            var lineRect = new RectangleF(lineX, bodyTop + i * lineGap, winRect.Width * lineWidths[i], lineH);
            using var linePath = RoundedRect(lineRect, lineH / 2f);
            g.FillPath(lineBrush, linePath);
        }
    }

    // Active prompt row: ">" chevron + solid cursor block, accent teal.
    float cursorY = bodyTop + lineWidths.Length * lineGap + lineGap * 0.05f;
    var accentColor = Color.FromArgb(255, 45, 212, 191);
    using (var accentPen = new Pen(accentColor, lineH * 0.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
    {
        float chevW = winRect.Width * 0.09f;
        float chevH = lineH * 1.7f;
        g.DrawLines(accentPen, new[]
        {
            new PointF(lineX, cursorY - chevH / 2f),
            new PointF(lineX + chevW, cursorY),
            new PointF(lineX, cursorY + chevH / 2f)
        });
    }

    using (var cursorBrush = new SolidBrush(accentColor))
    {
        var cursorRect = new RectangleF(lineX + winRect.Width * 0.15f, cursorY - lineH / 2f, winRect.Width * 0.30f, lineH);
        using var cursorPath = RoundedRect(cursorRect, lineH / 2f);
        g.FillPath(cursorBrush, cursorPath);
    }

    return bmp;
}

// ICO container: ICONDIR header, one 16-byte ICONDIRENTRY per frame, then each frame's PNG bytes.
static void WriteIco(string path, int[] sizes)
{
    var frames = new List<(int Size, byte[] Png)>();
    foreach (var size in sizes)
    {
        using var bmp = Draw(size);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        frames.Add((size, ms.ToArray()));
    }

    using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
    using var bw = new BinaryWriter(fs);

    bw.Write((short)0);
    bw.Write((short)1);
    bw.Write((short)frames.Count);

    int offset = 6 + frames.Count * 16;
    foreach (var (size, png) in frames)
    {
        bw.Write((byte)(size >= 256 ? 0 : size));
        bw.Write((byte)(size >= 256 ? 0 : size));
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((short)1);
        bw.Write((short)32);
        bw.Write(png.Length);
        bw.Write(offset);
        offset += png.Length;
    }

    foreach (var (_, png) in frames)
    {
        bw.Write(png);
    }
}
