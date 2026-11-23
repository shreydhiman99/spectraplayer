using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Generates every image the app and the installer need from two sources of truth:
//   - src/SpectraPlayer.App/Assets/Logo.xaml        the Spectraplayer logo (drawn in XAML)
//   - branding/NeuSpectra-logo.png                  the NeuSpectra company logo (dark ink on white)
// Outputs: Assets/spectraplayer.ico, docs/logo.png, Assets/neuspectra-light.png (for the dark app),
//          installer/images/*.bmp (wizard pictures for the setup program).
internal static class Program
{
    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    [STAThread]
    private static int Main()
    {
        var root = FindRepoRoot();
        var assets = Path.Combine(root, "src", "SpectraPlayer.App", "Assets");

        ResourceDictionary resources;
        using (var stream = File.OpenRead(Path.Combine(assets, "Logo.xaml"))) resources = (ResourceDictionary)XamlReader.Load(stream);
        var logo = (DrawingImage)resources["SpectraLogo"];

        // --- Spectraplayer icon -------------------------------------------------------------------------------
        var pngs = IconSizes.Select(size => (Size: size, Png: RenderPng(logo, size))).ToList();
        File.WriteAllBytes(Path.Combine(assets, "spectraplayer.ico"), BuildIco(pngs));
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        File.WriteAllBytes(Path.Combine(root, "docs", "logo.png"), RenderPng(logo, 256));
        Console.WriteLine($"Icon: Assets/spectraplayer.ico ({string.Join(", ", IconSizes)} px) and docs/logo.png");

        // --- NeuSpectra logo variants and installer pictures ----------------------------------------------------
        var source = Path.Combine(root, "branding", "NeuSpectra-logo.png");
        if (!File.Exists(source))
        {
            Console.WriteLine("branding/NeuSpectra-logo.png not found: skipped the company logo and installer pictures.");
            return 0;
        }

        var inkOnTransparent = LoadAsInk(source);
        File.WriteAllBytes(Path.Combine(assets, "neuspectra-light.png"), Encode(Tint(inkOnTransparent, Color.FromRgb(0xE9, 0xEC, 0xF5))));
        var dark = Tint(inkOnTransparent, Color.FromRgb(0x33, 0x36, 0x3F));

        var images = Path.Combine(root, "installer", "images");
        Directory.CreateDirectory(images);
        foreach (var (w, h) in new[] { (164, 314), (192, 386), (246, 459), (328, 628) })
            File.WriteAllBytes(Path.Combine(images, $"wizard-{w}x{h}.bmp"), RenderBmp(w, h, dc => DrawWizardSide(dc, w, h, dark, logo)));
        foreach (var (w, h) in new[] { (55, 55), (64, 68), (83, 80), (110, 106) })
            File.WriteAllBytes(Path.Combine(images, $"wizard-small-{w}x{h}.bmp"), RenderBmp(w, h, dc => DrawWizardSmall(dc, w, h, logo)));

        Console.WriteLine("Branding: Assets/neuspectra-light.png and installer/images/*.bmp");
        return 0;
    }

    // ---- NeuSpectra logo -----------------------------------------------------------------------------------------

    /// <summary>
    /// Turns a dark-on-white logo into "ink": black stays opaque, white becomes transparent, grey is partly transparent,
    /// then crops to the drawn area with a little margin. The colour is applied later by <see cref="Tint"/>.
    /// </summary>
    private static BitmapSource LoadAsInk(string path)
    {
        var bmp = new FormatConvertedBitmap(new BitmapImage(new Uri(path)), PixelFormats.Bgra32, null, 0);
        int w = bmp.PixelWidth, h = bmp.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        bmp.CopyPixels(px, stride, 0);

        var lum = new byte[w * h];
        byte darkest = 255;
        for (var i = 0; i < w * h; i++)
        {
            // The file may have a transparent background (it then only looks white in a viewer): lay every pixel over white first.
            int a0 = px[i * 4 + 3];
            int b = (px[i * 4] * a0 + 255 * (255 - a0)) / 255;
            int g = (px[i * 4 + 1] * a0 + 255 * (255 - a0)) / 255;
            int r = (px[i * 4 + 2] * a0 + 255 * (255 - a0)) / 255;
            var l = (byte)((b * 114 + g * 587 + r * 299) / 1000);
            lum[i] = l;
            if (l < darkest) darkest = l;
        }

        // alpha = how much darker than white, scaled so the darkest ink in the picture is fully opaque
        var alpha = new byte[w * h];
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                // x1.35: the original's hairlines would otherwise look faint once shrunk to wizard or title-bar size
                var a = (byte)Math.Clamp((255 - lum[y * w + x]) * 255 * 135 / (Math.Max(1, 255 - darkest) * 100), 0, 255);
                alpha[y * w + x] = a;
                if (a <= 12) continue;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }

        var pad = (int)(Math.Max(maxX - minX, maxY - minY) * 0.03);
        minX = Math.Max(0, minX - pad); minY = Math.Max(0, minY - pad);
        maxX = Math.Min(w - 1, maxX + pad); maxY = Math.Min(h - 1, maxY + pad);
        int cw = maxX - minX + 1, ch = maxY - minY + 1;

        // keep the alpha only (colour 0); Tint() fills the colour in
        var outPx = new byte[cw * ch * 4];
        for (var y = 0; y < ch; y++)
            for (var x = 0; x < cw; x++)
                outPx[(y * cw + x) * 4 + 3] = alpha[(minY + y) * w + minX + x];

        return BitmapSource.Create(cw, ch, 96, 96, PixelFormats.Bgra32, null, outPx, cw * 4);
    }

    private static BitmapSource Tint(BitmapSource ink, Color color)
    {
        int w = ink.PixelWidth, h = ink.PixelHeight;
        var px = new byte[w * h * 4];
        ink.CopyPixels(px, w * 4, 0);
        for (var i = 0; i < w * h; i++)
        {
            var a = px[i * 4 + 3];
            // straight (non-premultiplied) alpha: the colour is the same everywhere, only the opacity varies
            px[i * 4] = color.B; px[i * 4 + 1] = color.G; px[i * 4 + 2] = color.R;
            px[i * 4 + 3] = a;
        }
        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        result.Freeze();
        return result;
    }

    // ---- installer pictures --------------------------------------------------------------------------------------

    /// <summary>The tall picture on the welcome and finish pages: the NeuSpectra logo above the Spectraplayer logo.</summary>
    private static void DrawWizardSide(DrawingContext dc, int w, int h, BitmapSource neuSpectra, DrawingImage product)
    {
        var margin = w * 0.07;
        var logoW = w - 2 * margin;
        var logoH = logoW * neuSpectra.PixelHeight / neuSpectra.PixelWidth;
        var top = h * 0.05;
        DrawBitmap(dc, neuSpectra, new Rect(margin, top, logoW, logoH));

        var lineY = top + logoH + h * 0.04;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xD9, 0xDB, 0xE1)), null, new Rect(margin * 2.2, lineY, w - margin * 4.4, Math.Max(1, h / 314.0)));

        var icon = w * 0.42;
        var iconTop = lineY + h * 0.05;
        dc.DrawImage(product, new Rect((w - icon) / 2, iconTop, icon, icon));

        var nameTop = iconTop + icon + h * 0.02;
        DrawCentered(dc, "Spectraplayer", w, nameTop, w * 0.10, FontWeights.SemiBold, Color.FromRgb(0x20, 0x23, 0x2B));
        DrawCentered(dc, "by NeuSpectra", w, nameTop + w * 0.135, w * 0.07, FontWeights.Normal, Color.FromRgb(0x70, 0x75, 0x82));
    }

    private static void DrawWizardSmall(DrawingContext dc, int w, int h, DrawingImage product)
    {
        var size = Math.Min(w, h) * 0.9;
        dc.DrawImage(product, new Rect((w - size) / 2, (h - size) / 2, size, size));
    }

    private static void DrawBitmap(DrawingContext dc, BitmapSource bmp, Rect rect)
    {
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.HighQuality);
        using (var g = group.Open()) g.DrawImage(bmp, rect);
        dc.DrawDrawing(group);
    }

    private static void DrawCentered(DrawingContext dc, string text, double width, double y, double size, FontWeight weight, Color color)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, new SolidColorBrush(color), 1.0);
        dc.DrawText(ft, new Point((width - ft.Width) / 2, y));
    }

    // ---- encoding ------------------------------------------------------------------------------------------------

    private static byte[] RenderPng(DrawingImage image, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawImage(image, new Rect(0, 0, size, size));
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return Encode(bitmap);
    }

    private static byte[] Encode(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>A white-backed 24-bit BMP, the only picture format the setup program accepts for its wizard.</summary>
    private static byte[] RenderBmp(int w, int h, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            draw(dc);
        }
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0)));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>ICO container with PNG-compressed images (supported since Windows Vista).</summary>
    private static byte[] BuildIco(List<(int Size, byte[] Png)> images)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write((ushort)0);              // reserved
        w.Write((ushort)1);              // type: icon
        w.Write((ushort)images.Count);

        var offset = 6 + 16 * images.Count;
        foreach (var (size, png) in images)
        {
            w.Write((byte)(size >= 256 ? 0 : size)); // width (0 means 256)
            w.Write((byte)(size >= 256 ? 0 : size)); // height
            w.Write((byte)0);                        // palette colours
            w.Write((byte)0);                        // reserved
            w.Write((ushort)1);                      // colour planes
            w.Write((ushort)32);                     // bits per pixel
            w.Write((uint)png.Length);
            w.Write((uint)offset);
            offset += png.Length;
        }
        foreach (var (_, png) in images) w.Write(png);

        return ms.ToArray();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SpectraPlayer.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Run this from inside the Spectraplayer repository.");
    }
}
