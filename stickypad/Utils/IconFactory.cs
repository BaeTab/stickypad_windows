using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StickyPad.Utils;

/// 런타임에 StickyPad 아이콘을 그려서 .ico 파일로 저장하고 BitmapImage 로 노출.
/// H.NotifyIcon.Wpf 의 비동기 경로는 BitmapImage.UriSource 의 파일을 그대로
/// System.Drawing.Icon(stream, size) 에 넘기므로 ICO 컨테이너 + BMP 페이로드(GDI+ 친화)
/// 로 작성한다. 여러 사이즈를 미리 임베딩해 트레이(16/20)·작업표시줄(32/48) 모두 또렷.
public static class IconFactory
{
    private static readonly int[] IconSizes = { 16, 20, 24, 32, 48, 64 };

    private static readonly object _lock = new();
    private static ImageSource? _cachedImage;
    private static string? _cachedPath;

    public static ImageSource CreateAppIcon(int _ = 32) => EnsureBuilt().Image;

    public static string GetIconFilePath() => EnsureBuilt().Path;

    /// 트레이용 System.Drawing.Icon — H.NotifyIcon 의 TaskbarIcon.Icon 속성에 직접 할당하면
    /// 비동기 IconSource 변환 경로를 우회해 가장 안정적으로 표시된다.
    public static System.Drawing.Icon CreateTrayIcon(int size = 16)
    {
        var path = GetIconFilePath();
        using var fs = File.OpenRead(path);
        return new System.Drawing.Icon(fs, size, size);
    }

    private static (ImageSource Image, string Path) EnsureBuilt()
    {
        lock (_lock)
        {
            if (_cachedImage is not null && _cachedPath is not null)
            {
                return (_cachedImage, _cachedPath);
            }

            var path = Path.Combine(Path.GetTempPath(), "StickyPad", "app-icon.ico");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, BuildIcoBytes());
            }
            catch (IOException)
            {
                // 다른 인스턴스가 동시에 같은 파일을 만들었거나 잠그고 있을 수 있다.
                // 동일 내용이면 그대로 사용 가능하므로 진행한다.
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            _cachedImage = image;
            _cachedPath = path;
            return (image, path);
        }
    }

    private static byte[] BuildIcoBytes()
    {
        var frames = new List<(int Size, byte[] Bgra)>(IconSizes.Length);
        foreach (var s in IconSizes)
        {
            frames.Add((s, RenderBgra(s)));
        }
        return EncodeBmpIco(frames);
    }

    private static byte[] RenderBgra(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            DrawIcon(dc, size);
        }

        var rt = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rt.Render(visual);

        var stride = size * 4;
        var pixels = new byte[stride * size];
        rt.CopyPixels(pixels, stride, 0);

        // Pbgra32 -> straight BGRA. ICO 의 32bpp 포맷은 unpremultiplied alpha 를 기대한다.
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var a = pixels[i + 3];
            if (a == 0 || a == 255) continue;
            pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / a);
            pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / a);
            pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / a);
        }
        return pixels;
    }

    private static byte[] EncodeBmpIco(IList<(int Size, byte[] Bgra)> frames)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // ICONDIR
        bw.Write((ushort)0);                                // reserved
        bw.Write((ushort)1);                                // type = icon
        bw.Write((ushort)frames.Count);                     // frame count

        var dirSize = 6 + 16 * frames.Count;
        var offset = (uint)dirSize;
        var payloads = new List<byte[]>(frames.Count);

        foreach (var (size, bgra) in frames)
        {
            var payload = BuildBmpPayload(size, bgra);
            payloads.Add(payload);

            bw.Write((byte)(size >= 256 ? 0 : size));       // width  (0 = 256)
            bw.Write((byte)(size >= 256 ? 0 : size));       // height (0 = 256)
            bw.Write((byte)0);                              // colors in palette (0 for >= 8bpp)
            bw.Write((byte)0);                              // reserved
            bw.Write((ushort)1);                            // color planes
            bw.Write((ushort)32);                           // bits per pixel
            bw.Write((uint)payload.Length);                 // bytes in resource
            bw.Write(offset);                               // offset of image data

            offset += (uint)payload.Length;
        }

        foreach (var p in payloads)
        {
            bw.Write(p);
        }

        bw.Flush();
        return ms.ToArray();
    }

    private static byte[] BuildBmpPayload(int size, byte[] bgraTopDown)
    {
        var stride = size * 4;
        var andRowBytes = ((size + 31) / 32) * 4;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // BITMAPINFOHEADER (40 bytes). ICO 안에서 height 는 image+AND mask 합친 두 배 값.
        bw.Write((uint)40);
        bw.Write(size);                                     // biWidth
        bw.Write(size * 2);                                 // biHeight (image + AND mask)
        bw.Write((ushort)1);                                // biPlanes
        bw.Write((ushort)32);                               // biBitCount
        bw.Write((uint)0);                                  // biCompression = BI_RGB
        bw.Write((uint)0);                                  // biSizeImage
        bw.Write(0);                                        // biXPelsPerMeter
        bw.Write(0);                                        // biYPelsPerMeter
        bw.Write((uint)0);                                  // biClrUsed
        bw.Write((uint)0);                                  // biClrImportant

        // XOR mask: BGRA, bottom-up.
        for (var y = size - 1; y >= 0; y--)
        {
            bw.Write(bgraTopDown, y * stride, stride);
        }

        // AND mask: 1bpp, bottom-up, 4 바이트 정렬. 32bpp + alpha 에서는 사실상 무시되지만
        // 표준상 반드시 존재해야 하므로 전부 0(불투명) 으로 채운다.
        var blankRow = new byte[andRowBytes];
        for (var y = 0; y < size; y++)
        {
            bw.Write(blankRow);
        }

        bw.Flush();
        return ms.ToArray();
    }

    private static void DrawIcon(DrawingContext dc, int size)
    {
        // 따뜻한 sticky-note 팔레트.
        var bodyTop = Color.FromRgb(0xFF, 0xF1, 0xA0);
        var bodyBottom = Color.FromRgb(0xFE, 0xD9, 0x5C);
        var borderColor = Color.FromRgb(0xC2, 0x99, 0x36);
        var foldTop = Color.FromRgb(0xFF, 0xEF, 0xB8);
        var foldBottom = Color.FromRgb(0xD8, 0xB0, 0x4F);
        var foldShadow = Color.FromArgb(0x40, 0x00, 0x00, 0x00);
        var ink = Color.FromRgb(0x3A, 0x29, 0x0A);

        var inset = size <= 16 ? 1.0 : size * 0.07;
        var radius = Math.Max(2.0, size * 0.14);
        var foldDepth = size * 0.32;
        var rect = new Rect(inset, inset, size - inset * 2, size - inset * 2);

        var borderPen = new Pen(new SolidColorBrush(borderColor), Math.Max(0.7, size * 0.04))
        {
            LineJoin = PenLineJoin.Round,
        };
        borderPen.Freeze();

        // 본체: 둥근 모서리 + 우상단 모서리만 사선 컷.
        var body = new StreamGeometry();
        using (var ctx = body.Open())
        {
            ctx.BeginFigure(new Point(rect.X + radius, rect.Y), true, true);
            ctx.LineTo(new Point(rect.Right - foldDepth, rect.Y), true, true);
            ctx.LineTo(new Point(rect.Right, rect.Y + foldDepth), true, true);
            ctx.LineTo(new Point(rect.Right, rect.Bottom - radius), true, true);
            ctx.QuadraticBezierTo(new Point(rect.Right, rect.Bottom),
                new Point(rect.Right - radius, rect.Bottom), true, true);
            ctx.LineTo(new Point(rect.X + radius, rect.Bottom), true, true);
            ctx.QuadraticBezierTo(new Point(rect.X, rect.Bottom),
                new Point(rect.X, rect.Bottom - radius), true, true);
            ctx.LineTo(new Point(rect.X, rect.Y + radius), true, true);
            ctx.QuadraticBezierTo(new Point(rect.X, rect.Y),
                new Point(rect.X + radius, rect.Y), true, true);
        }
        body.Freeze();

        var bodyBrush = new LinearGradientBrush(bodyTop, bodyBottom, 90);
        bodyBrush.Freeze();
        dc.DrawGeometry(bodyBrush, borderPen, body);

        // 모서리 그림자(접힌 부분 아래쪽).
        if (size >= 20)
        {
            var shadow = new StreamGeometry();
            using (var ctx = shadow.Open())
            {
                var pad = Math.Max(1.0, size * 0.04);
                ctx.BeginFigure(new Point(rect.Right - foldDepth, rect.Y + pad), true, true);
                ctx.LineTo(new Point(rect.Right - foldDepth, rect.Y + foldDepth), true, false);
                ctx.LineTo(new Point(rect.Right - pad, rect.Y + foldDepth), true, false);
            }
            shadow.Freeze();
            dc.DrawGeometry(new SolidColorBrush(foldShadow), null, shadow);
        }

        // 접힌 모서리 삼각형.
        var fold = new StreamGeometry();
        using (var ctx = fold.Open())
        {
            ctx.BeginFigure(new Point(rect.Right - foldDepth, rect.Y), true, true);
            ctx.LineTo(new Point(rect.Right, rect.Y + foldDepth), true, true);
            ctx.LineTo(new Point(rect.Right - foldDepth, rect.Y + foldDepth), true, true);
        }
        fold.Freeze();

        var foldBrush = new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(foldTop, 0),
                new GradientStop(foldBottom, 1),
            },
            new Point(0.2, 0), new Point(0.85, 1));
        foldBrush.Freeze();
        dc.DrawGeometry(foldBrush, borderPen, fold);

        // 잉크 라인 — 작은 크기에서는 디테일 줄이거나 생략.
        if (size >= 18)
        {
            var lineCount = size <= 22 ? 2 : 3;
            var thickness = Math.Max(0.9, size * 0.055);
            var pen = new Pen(new SolidColorBrush(ink), thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            pen.Freeze();

            var leftPad = size * 0.22;
            var rightPad = size * 0.20;
            var top = size * 0.46;
            var spacing = size * 0.16;
            for (var i = 0; i < lineCount; i++)
            {
                var y = top + i * spacing;
                var xStart = rect.X + leftPad;
                var xEnd = rect.Right - rightPad - (i == lineCount - 1 ? size * 0.18 : 0);
                dc.DrawLine(pen, new Point(xStart, y), new Point(xEnd, y));
            }
        }
    }
}
