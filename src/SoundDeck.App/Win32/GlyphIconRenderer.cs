namespace SoundDeck.App.Win32;

/// <summary>Segoe Fluent Icons の文字をトレイ用のアイコン（HICON）に描く。Windows 11 の標準アイコンと同じ見た目になる。</summary>
internal static class GlyphIconRenderer
{
    private const uint DtCenter = 0x1;
    private const uint DtVCenter = 0x4;
    private const uint DtSingleLine = 0x20;
    private const uint DtNoPrefix = 0x800;
    private const uint AntialiasedQuality = 4;
    private const uint DefaultCharset = 1;
    private const int Transparent = 1;

    /// <summary>グリフを描いた HICON を返す。使い終わったら DestroyIcon すること。</summary>
    /// <param name="color">0xRRGGBB</param>
    public static unsafe IntPtr Render(string glyph, uint color, int size)
    {
        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = size,
            Height = -size, // 上から下へ並ぶ DIB
            Planes = 1,
            BitCount = 32,
        };

        var hdc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
        var dib = NativeMethods.CreateDIBSection(hdc, ref header, 0, out var bits, IntPtr.Zero, 0);
        var oldBitmap = NativeMethods.SelectObject(hdc, dib);

        var fontName = HasFluentIcons() ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        var font = NativeMethods.CreateFont(-size, 0, 0, 0, 400, 0, 0, 0, DefaultCharset, 0, 0, AntialiasedQuality, 0, fontName);
        var oldFont = NativeMethods.SelectObject(hdc, font);
        NativeMethods.SetBkMode(hdc, Transparent);
        NativeMethods.SetTextColor(hdc, 0x00FFFFFF);

        var rect = new Rect { Left = 0, Top = 0, Right = size, Bottom = size };
        NativeMethods.DrawText(hdc, glyph, -1, ref rect, DtCenter | DtVCenter | DtSingleLine | DtNoPrefix);
        NativeMethods.GdiFlush();

        // 黒地に白で描いたので、明るさをそのまま不透明度として使い、指定色で塗り直す（乗算済みアルファ）
        var r = (color >> 16) & 0xFF;
        var g = (color >> 8) & 0xFF;
        var b = color & 0xFF;
        var pixels = (uint*)bits;
        for (var i = 0; i < size * size; i++)
        {
            var p = pixels[i];
            var a = Math.Max(p & 0xFF, Math.Max((p >> 8) & 0xFF, (p >> 16) & 0xFF));
            pixels[i] = (a << 24) | ((r * a / 255) << 16) | ((g * a / 255) << 8) | (b * a / 255);
        }

        NativeMethods.SelectObject(hdc, oldFont);
        NativeMethods.SelectObject(hdc, oldBitmap);
        NativeMethods.DeleteObject(font);
        NativeMethods.DeleteDC(hdc);

        var mask = NativeMethods.CreateBitmap(size, size, 1, 1, IntPtr.Zero);
        var iconInfo = new IconInfo { IsIcon = 1, MaskBitmap = mask, ColorBitmap = dib };
        var icon = NativeMethods.CreateIconIndirect(ref iconInfo);
        NativeMethods.DeleteObject(mask);
        NativeMethods.DeleteObject(dib);
        return icon;
    }

    private static bool? s_hasFluentIcons;

    private static bool HasFluentIcons() =>
        s_hasFluentIcons ??= File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "SegoeIcons.ttf"));
}
