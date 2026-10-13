using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SoundDeck.App.Services;

/// <summary>実行ファイルからアプリのアイコンと名前を取り出してキャッシュする。UI スレッドで使うこと。</summary>
internal static partial class AppIconCache
{
    private const int IconPixels = 32;
    private const int SiigbfIconOnly = 0x4;

    private static readonly Dictionary<string, ImageSource?> s_icons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> s_names = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetIcon(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath))
        {
            return null;
        }

        if (!s_icons.TryGetValue(executablePath, out var icon))
        {
            icon = LoadIcon(executablePath);
            s_icons[executablePath] = icon;
        }

        return icon;
    }

    /// <summary>「Google Chrome」のような表示名。ファイルの説明 → 製品名 → ファイル名の順に使う。</summary>
    public static string GetDisplayName(string executablePath)
    {
        if (s_names.TryGetValue(executablePath, out var name))
        {
            return name;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            name = FirstNonEmpty(info.FileDescription, info.ProductName) ?? Path.GetFileNameWithoutExtension(executablePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            name = Path.GetFileNameWithoutExtension(executablePath);
        }

        s_names[executablePath] = name;
        return name;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static unsafe ImageSource? LoadIcon(string path)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item) < 0 || item is not IShellItemImageFactory factory)
        {
            return null;
        }

        if (factory.GetImage(new NativeSize { Width = IconPixels, Height = IconPixels }, SiigbfIconOnly, out var bitmap) < 0)
        {
            return null;
        }

        try
        {
            var header = new BitmapInfoHeaderNative
            {
                Size = (uint)sizeof(BitmapInfoHeaderNative),
                Width = IconPixels,
                Height = -IconPixels,
                Planes = 1,
                BitCount = 32,
            };
            var pixels = new byte[IconPixels * IconPixels * 4];
            var hdc = CreateCompatibleDC(IntPtr.Zero);
            int lines;
            fixed (byte* p = pixels)
            {
                lines = GetDIBits(hdc, bitmap, 0, IconPixels, p, ref header, 0);
            }

            DeleteDC(hdc);
            if (lines == 0)
            {
                return null;
            }

            var image = new WriteableBitmap(IconPixels, IconPixels);
            using (var stream = image.PixelBuffer.AsStream())
            {
                stream.Write(pixels);
            }

            image.Invalidate();
            return image;
        }
        finally
        {
            DeleteObject(bitmap);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeaderNative
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
        public uint Palette; // RGBQUAD 1 個分の余白
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        string path, IntPtr bindContext, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object item);

    [LibraryImport("gdi32.dll")]
    private static unsafe partial int GetDIBits(IntPtr hdc, IntPtr bitmap, uint start, uint lines, byte* bits, ref BitmapInfoHeaderNative info, uint usage);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr gdiObject);
}
