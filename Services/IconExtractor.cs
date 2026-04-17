using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinXStart.Models;

namespace WinXStart.Services;

public class IconExtractor
{
    private readonly Dictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<ImageSource> _defaultIcon = new(CreateDefaultIcon);

    public ImageSource GetIcon(AppInfo app)
    {
        var key = !string.IsNullOrEmpty(app.TargetPath) ? app.TargetPath : app.ShortcutPath;

        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var icon = ExtractFromFile(app.TargetPath)
                   ?? ExtractFromFile(app.ShortcutPath)
                   ?? _defaultIcon.Value;

        _cache[key] = icon;
        return icon;
    }

    private static ImageSource? ExtractFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon == null) return null;

            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(48, 48));

            bitmapSource.Freeze();
            return bitmapSource;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource CreateDefaultIcon()
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawRoundedRectangle(
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4)),
                null,
                new Rect(0, 0, 48, 48), 6, 6);
            ctx.DrawText(
                new FormattedText("?",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    24, System.Windows.Media.Brushes.White,
                    VisualTreeHelper.GetDpi(visual).PixelsPerDip),
                new System.Windows.Point(14, 8));
        }

        var bmp = new RenderTargetBitmap(48, 48, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }
}
