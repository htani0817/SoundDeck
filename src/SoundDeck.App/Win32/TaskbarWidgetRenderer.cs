namespace SoundDeck.App.Win32;

/// <summary>
/// タスクバーのウィジェット（アイコン + 音量）を描く。タスクバーの背景が透けるよう、色ごとの不透明度つきの画像を
/// UpdateLayeredWindow で渡す。文字は GDI で白黒に描き、明るさを不透明度として使う（<see cref="GlyphIconRenderer"/> と同じ方法）。
/// </summary>
internal static class TaskbarWidgetRenderer
{
    private const uint DtLeft = 0x0;
    private const uint DtCenter = 0x1;
    private const uint DtVCenter = 0x4;
    private const uint DtSingleLine = 0x20;
    private const uint DtNoPrefix = 0x800;
    private const uint AntialiasedQuality = 4;
    private const uint DefaultCharset = 1;
    private const int Transparent = 1;

    // 大きさ（拡大率 100% のときのピクセル）。タスクバーの時計と同じくらいの文字にする
    private const double PaddingDip = 8;
    private const double IconDip = 16;
    private const double GapDip = 6;
    private const double TextDip = 12;
    private const double HoverInsetXDip = 2;
    private const double HoverInsetYDip = 4;
    private const double HoverRadiusDip = 4;

    // 幅は「100%」が入る大きさで固定する（音量で幅が変わってガタつかないように）
    private const string WidestText = "100%";
    private const string TextFont = "Segoe UI";

    private static readonly Dictionary<double, int> s_widthCache = [];
    private static readonly object s_widthLock = new();

    /// <summary>拡大率に合わせたウィジェットの幅（物理ピクセル）。</summary>
    public static int MeasureWidth(double scale)
    {
        lock (s_widthLock)
        {
            if (s_widthCache.TryGetValue(scale, out var cached))
            {
                return cached;
            }

            var hdc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
            var font = CreateTextFont(scale);
            var oldFont = NativeMethods.SelectObject(hdc, font);
            NativeMethods.GetTextExtentPoint32(hdc, WidestText, WidestText.Length, out var size);
            NativeMethods.SelectObject(hdc, oldFont);
            NativeMethods.DeleteObject(font);
            NativeMethods.DeleteDC(hdc);

            var width = Px(PaddingDip, scale) + Px(IconDip, scale) + Px(GapDip, scale) + size.Width + Px(PaddingDip, scale);
            s_widthCache[scale] = width;
            return width;
        }
    }

    public static unsafe void Render(IntPtr hwnd, int width, int height, double scale, TaskbarWidgetState state, bool hover, bool pressed)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = width,
            Height = -height, // 上から下へ並ぶ DIB
            Planes = 1,
            BitCount = 32,
        };

        var screen = IntPtr.Zero;
        var hdc = NativeMethods.CreateCompatibleDC(screen);
        var dib = NativeMethods.CreateDIBSection(hdc, ref header, 0, out var bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero)
        {
            NativeMethods.DeleteDC(hdc);
            return;
        }

        var oldBitmap = NativeMethods.SelectObject(hdc, dib);
        NativeMethods.SetBkMode(hdc, Transparent);

        // アイコンは赤、文字は緑で描き、あとで色ごとに不透明度として取り出す（重ならないので混ざらない）
        var padding = Px(PaddingDip, scale);
        var iconSize = Px(IconDip, scale);
        var iconFont = NativeMethods.CreateFont(-iconSize, 0, 0, 0, 400, 0, 0, 0, DefaultCharset, 0, 0, AntialiasedQuality, 0, IconFontName());
        var oldFont = NativeMethods.SelectObject(hdc, iconFont);
        NativeMethods.SetTextColor(hdc, 0x000000FF);
        var iconRect = new Rect { Left = padding, Top = 0, Right = padding + iconSize, Bottom = height };
        NativeMethods.DrawText(hdc, state.Glyph, -1, ref iconRect, DtCenter | DtVCenter | DtSingleLine | DtNoPrefix);

        var textFont = CreateTextFont(scale);
        NativeMethods.SelectObject(hdc, textFont);
        NativeMethods.SetTextColor(hdc, 0x0000FF00);
        var textRect = new Rect { Left = padding + iconSize + Px(GapDip, scale), Top = 0, Right = width, Bottom = height };
        NativeMethods.DrawText(hdc, state.Text, -1, ref textRect, DtLeft | DtVCenter | DtSingleLine | DtNoPrefix);
        NativeMethods.GdiFlush();
        NativeMethods.SelectObject(hdc, oldFont);
        NativeMethods.DeleteObject(iconFont);
        NativeMethods.DeleteObject(textFont);

        // 前景の色と、マウスを乗せたときの背景（タスクバーのボタンと同じ角丸）
        var foreground = state.LightTaskbar ? 0x1A1A1Au : 0xFFFFFFu;
        var textOpacity = state.Muted ? 0.5 : 1.0;
        const uint hoverColor = 0xFFFFFF; // ダークでもライトでも白を重ねる（ライトは濃いめ）
        var hoverAlpha = (hover, pressed, state.LightTaskbar) switch
        {
            (_, true, true) => 0.5,
            (true, _, true) => 0.7,
            (_, true, false) => 0.04,
            (true, _, false) => 0.08,
            _ => 0.0,
        };

        var insetX = Px(HoverInsetXDip, scale);
        var insetY = Px(HoverInsetYDip, scale);
        var radius = HoverRadiusDip * scale;
        var pixels = (uint*)bits;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = pixels[(y * width) + x];
                // 画素は 0xAARRGGBB。赤がアイコン、緑が文字
                var iconCoverage = ((p >> 16) & 0xFF) / 255.0;
                var textCoverage = ((p >> 8) & 0xFF) / 255.0 * textOpacity;
                var fg = Math.Min(1.0, iconCoverage + textCoverage);

                // 背景。何も無いところも不透明度 1/255 にしておかないと、クリックが下へ抜けてしまう
                var bgAlpha = hoverAlpha > 0 ? hoverAlpha * RoundedCoverage(x, y, insetX, insetY, width - insetX, height - insetY, radius) : 0;
                bgAlpha = Math.Max(bgAlpha, 1 / 255.0);

                var alpha = fg + (bgAlpha * (1 - fg));
                var r = (Channel(foreground, 16) * fg) + (Channel(hoverColor, 16) * bgAlpha * (1 - fg));
                var g = (Channel(foreground, 8) * fg) + (Channel(hoverColor, 8) * bgAlpha * (1 - fg));
                var b = (Channel(foreground, 0) * fg) + (Channel(hoverColor, 0) * bgAlpha * (1 - fg));
                pixels[(y * width) + x] = ((uint)Math.Round(alpha * 255) << 24) | ((uint)Math.Round(r) << 16) | ((uint)Math.Round(g) << 8) | (uint)Math.Round(b);
            }
        }

        var size = new NativeSize { Width = width, Height = height };
        var source = new Point();
        var blend = new BlendFunction { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        NativeMethods.UpdateLayeredWindow(hwnd, IntPtr.Zero, IntPtr.Zero, ref size, hdc, ref source, 0, ref blend, NativeMethods.UlwAlpha);

        NativeMethods.SelectObject(hdc, oldBitmap);
        NativeMethods.DeleteObject(dib);
        NativeMethods.DeleteDC(hdc);
    }

    private static IntPtr CreateTextFont(double scale) =>
        NativeMethods.CreateFont(-Px(TextDip, scale), 0, 0, 0, 400, 0, 0, 0, DefaultCharset, 0, 0, AntialiasedQuality, 0, TextFont);

    private static int Px(double dip, double scale) => (int)Math.Round(dip * scale);

    private static double Channel(uint color, int shift) => (color >> shift) & 0xFF;

    /// <summary>角丸の四角の中にどれだけ入っているか（0〜1、縁はなめらかにする）。</summary>
    private static double RoundedCoverage(int x, int y, int left, int top, int right, int bottom, double radius)
    {
        var px = x + 0.5;
        var py = y + 0.5;
        if (px < left || px > right || py < top || py > bottom)
        {
            return 0;
        }

        var cx = Math.Clamp(px, left + radius, right - radius);
        var cy = Math.Clamp(py, top + radius, bottom - radius);
        var distance = Math.Sqrt(((px - cx) * (px - cx)) + ((py - cy) * (py - cy)));
        return Math.Clamp(radius - distance + 0.5, 0, 1);
    }

    private static string IconFontName() =>
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "SegoeIcons.ttf")) ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
}
