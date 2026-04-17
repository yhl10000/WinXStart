using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinXStart.Models;
using Color = System.Windows.Media.Color;

namespace WinXStart.ViewModels;

public class TileViewModel : ViewModelBase
{
    private TileSize _size;

    public AppInfo AppInfo { get; }
    public ImageSource Icon { get; }
    public string Name => AppInfo.Name;
    public string AppId { get; }

    /// <summary>
    /// Background image for Medium/Large/Wide tiles. Null for Small tiles or apps without wide logos.
    /// </summary>
    public ImageSource? BackgroundImage => GetBackgroundImage();

    /// <summary>True when the tile has a full-bleed background image (hides the small centered icon).</summary>
    public bool HasBackgroundImage => BackgroundImage != null;

    private ImageSource? GetBackgroundImage()
    {
        if (Size == TileSize.Small) return null;

        var path = Size == TileSize.Wide && !string.IsNullOrEmpty(AppInfo.WideLogoPath)
            ? AppInfo.WideLogoPath
            : AppInfo.MediumLogoPath;

        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    public TileSize Size
    {
        get => _size;
        set
        {
            if (SetField(ref _size, value))
            {
                OnPropertyChanged(nameof(TileWidth));
                OnPropertyChanged(nameof(TileHeight));
                OnPropertyChanged(nameof(IconSize));
                OnPropertyChanged(nameof(BackgroundImage));
                OnPropertyChanged(nameof(HasBackgroundImage));
            }
        }
    }

    public double TileWidth => Size switch
    {
        TileSize.Small => 72,
        TileSize.Medium => 110,
        TileSize.Wide => 110,
        TileSize.Large => 150,
        _ => 110
    };

    public double TileHeight => TileWidth;

    public double IconSize => Size switch
    {
        TileSize.Small => 30,
        TileSize.Large => 48,
        _ => 36
    };

    public SolidColorBrush TileColor { get; }

    // Resize commands
    public RelayCommand ResizeSmallCommand { get; }
    public RelayCommand ResizeMediumCommand { get; }
    public RelayCommand ResizeWideCommand { get; }
    public RelayCommand ResizeLargeCommand { get; }

    public event Action<string, TileSize>? ResizeRequested;

    public TileViewModel(AppInfo appInfo, ImageSource icon, TileSize size)
    {
        AppInfo = appInfo;
        Icon = icon;
        _size = size;
        AppId = appInfo.Id;
        TileColor = DeriveColorFromIcon(icon, appInfo.Name);

        ResizeSmallCommand = new RelayCommand(() => DoResize(TileSize.Small));
        ResizeMediumCommand = new RelayCommand(() => DoResize(TileSize.Medium));
        ResizeWideCommand = new RelayCommand(() => DoResize(TileSize.Wide));
        ResizeLargeCommand = new RelayCommand(() => DoResize(TileSize.Large));
    }

    private void DoResize(TileSize newSize)
    {
        Size = newSize;
        ResizeRequested?.Invoke(AppId, newSize);
    }

    /// <summary>
    /// Extract the dominant color from the icon bitmap pixels and produce a
    /// harmonious tile background: same hue, moderate saturation, darkened.
    /// Falls back to name-hash color if extraction fails.
    /// </summary>
    private static SolidColorBrush DeriveColorFromIcon(ImageSource icon, string fallbackName)
    {
        try
        {
            if (icon is BitmapSource bmp)
            {
                var dominant = GetDominantColor(bmp);
                if (dominant.HasValue)
                {
                    var c = dominant.Value;
                    RgbToHsl(c.R, c.G, c.B, out double h, out double s, out double l);

                    // Make a pleasant tile background: keep hue, boost saturation, darken
                    s = Math.Clamp(s * 1.1, 0.25, 0.65);
                    l = Math.Clamp(l * 0.7, 0.18, 0.38);

                    HslToRgb(h, s, l, out byte r, out byte g, out byte b);
                    var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
                    brush.Freeze();
                    return brush;
                }
            }
        }
        catch { }

        return FallbackColor(fallbackName);
    }

    /// <summary>
    /// Sample the icon pixels, ignore near-white / near-black / near-transparent,
    /// and return the most common "colorful" pixel bucket.
    /// </summary>
    private static Color? GetDominantColor(BitmapSource bmp)
    {
        const int sampleSize = 32;

        // Convert to 32-bit BGRA
        var formatted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        var scaled = new TransformedBitmap(formatted,
            new ScaleTransform(
                sampleSize / (double)formatted.PixelWidth,
                sampleSize / (double)formatted.PixelHeight));

        int w = scaled.PixelWidth;
        int h = scaled.PixelHeight;
        int stride = w * 4;
        var pixels = new byte[stride * h];
        scaled.CopyPixels(pixels, stride, 0);

        // Bucket colors by quantised hue (12 buckets × 30°)
        const int bucketCount = 12;
        var bucketR = new long[bucketCount];
        var bucketG = new long[bucketCount];
        var bucketB = new long[bucketCount];
        var bucketN = new int[bucketCount];

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2], a = pixels[i + 3];
            if (a < 128) continue;                       // skip transparent
            int max = Math.Max(r, Math.Max(g, b));
            int min = Math.Min(r, Math.Min(g, b));
            if (max < 30) continue;                      // skip near-black
            if (min > 220) continue;                     // skip near-white
            if (max - min < 20) continue;                // skip gray

            RgbToHsl(r, g, b, out double hue, out _, out _);
            int bucket = (int)(hue / 360.0 * bucketCount) % bucketCount;
            bucketR[bucket] += r;
            bucketG[bucket] += g;
            bucketB[bucket] += b;
            bucketN[bucket]++;
        }

        // Pick the bucket with the most pixels
        int best = -1, bestCount = 0;
        for (int i = 0; i < bucketCount; i++)
        {
            if (bucketN[i] > bestCount) { bestCount = bucketN[i]; best = i; }
        }

        if (best < 0 || bestCount < 3) return null;

        return Color.FromRgb(
            (byte)(bucketR[best] / bestCount),
            (byte)(bucketG[best] / bestCount),
            (byte)(bucketB[best] / bestCount));
    }

    // ── HSL helpers ─────────────────────────────────────

    private static void RgbToHsl(byte r, byte g, byte b, out double h, out double s, out double l)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        l = (max + min) / 2.0;

        if (delta < 1e-6) { h = 0; s = 0; return; }

        s = l < 0.5 ? delta / (max + min) : delta / (2.0 - max - min);

        if (Math.Abs(max - rd) < 1e-6)
            h = ((gd - bd) / delta + (gd < bd ? 6 : 0)) * 60;
        else if (Math.Abs(max - gd) < 1e-6)
            h = ((bd - rd) / delta + 2) * 60;
        else
            h = ((rd - gd) / delta + 4) * 60;
    }

    private static void HslToRgb(double h, double s, double l, out byte r, out byte g, out byte b)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = l - c / 2;
        double rd, gd, bd;

        if (h < 60)       { rd = c; gd = x; bd = 0; }
        else if (h < 120) { rd = x; gd = c; bd = 0; }
        else if (h < 180) { rd = 0; gd = c; bd = x; }
        else if (h < 240) { rd = 0; gd = x; bd = c; }
        else if (h < 300) { rd = x; gd = 0; bd = c; }
        else              { rd = c; gd = 0; bd = x; }

        r = (byte)Math.Clamp((rd + m) * 255, 0, 255);
        g = (byte)Math.Clamp((gd + m) * 255, 0, 255);
        b = (byte)Math.Clamp((bd + m) * 255, 0, 255);
    }

    private static SolidColorBrush FallbackColor(string name)
    {
        var hash = name.GetHashCode();
        var colors = new[]
        {
            Color.FromRgb(0x00, 0x78, 0xD4),
            Color.FromRgb(0x00, 0x99, 0xBC),
            Color.FromRgb(0x7A, 0x76, 0x74),
            Color.FromRgb(0x00, 0x7B, 0x4F),
            Color.FromRgb(0xC2, 0x39, 0xB3),
            Color.FromRgb(0xDA, 0x3B, 0x01),
            Color.FromRgb(0x88, 0x64, 0x00),
            Color.FromRgb(0x51, 0x5C, 0x6B),
        };
        var brush = new SolidColorBrush(colors[Math.Abs(hash) % colors.Length]);
        brush.Freeze();
        return brush;
    }
}
