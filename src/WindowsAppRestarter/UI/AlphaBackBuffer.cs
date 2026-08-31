using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WindowsAppRestarter.UI;

/// <summary>
/// A 32-bit premultiplied-alpha DIB back buffer. DWM interprets a window surface as premultiplied ARGB, so
/// translucent Fluent fills and anti-aliased text only composite correctly over acrylic when drawn this way.
/// </summary>
internal sealed class AlphaBackBuffer : IDisposable
{
    private const int SRCCOPY = 0x00CC0020;

    private nint memoryDc;
    private nint dibHandle;
    private nint previousBitmap;
    private Bitmap? bitmap;
    private Graphics? graphics;
    private Size size;

    public Graphics BeginDraw(Size requiredSize, float dpi)
    {
        if (graphics is null || size != requiredSize)
        {
            Release();
            Allocate(requiredSize, dpi);
        }
        else if (Math.Abs(bitmap!.HorizontalResolution - dpi) > 0.5f)
        {
            bitmap.SetResolution(dpi, dpi);
            graphics.Dispose();
            graphics = Graphics.FromImage(bitmap);
        }

        return graphics!;
    }

    public void Render(nint targetDc)
    {
        if (graphics is null)
        {
            return;
        }

        graphics.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
        BitBlt(targetDc, 0, 0, size.Width, size.Height, memoryDc, 0, 0, SRCCOPY);
    }

    public void Dispose() => Release();

    private void Allocate(Size requiredSize, float dpi)
    {
        size = requiredSize;
        memoryDc = CreateCompatibleDC(nint.Zero);

        var info = new BITMAPINFO
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFO>(),
            biWidth = size.Width,
            biHeight = -size.Height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0
        };

        dibHandle = CreateDIBSection(memoryDc, ref info, 0, out var bits, nint.Zero, 0);
        if (dibHandle == nint.Zero)
        {
            throw new InvalidOperationException("Could not allocate the flyout back buffer.");
        }

        previousBitmap = SelectObject(memoryDc, dibHandle);
        bitmap = new Bitmap(size.Width, size.Height, size.Width * 4, PixelFormat.Format32bppPArgb, bits);
        bitmap.SetResolution(dpi, dpi);
        graphics = Graphics.FromImage(bitmap);
    }

    private void Release()
    {
        graphics?.Dispose();
        graphics = null;
        bitmap?.Dispose();
        bitmap = null;

        if (memoryDc != nint.Zero)
        {
            if (previousBitmap != nint.Zero)
            {
                SelectObject(memoryDc, previousBitmap);
                previousBitmap = nint.Zero;
            }

            DeleteDC(memoryDc);
            memoryDc = nint.Zero;
        }

        if (dibHandle != nint.Zero)
        {
            DeleteObject(dibHandle);
            dibHandle = nint.Zero;
        }

        size = Size.Empty;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
        public uint bmiColors;
    }

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern nint CreateDIBSection(nint hdc, ref BITMAPINFO info, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern nint SelectObject(nint hdc, nint handle);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint handle);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(nint destDc, int x, int y, int width, int height, nint sourceDc, int sourceX, int sourceY, int rop);
}
