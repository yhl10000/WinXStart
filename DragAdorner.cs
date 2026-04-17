using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace WinXStart;

/// <summary>
/// Renders a semi-transparent snapshot of a tile that follows the mouse during drag.
/// </summary>
public sealed class DragAdorner : Adorner
{
    private readonly Rectangle _ghost;
    private readonly double _offsetX;
    private readonly double _offsetY;
    private double _left;
    private double _top;

    public DragAdorner(UIElement adornedElement, UIElement sourceElement, double offsetX, double offsetY)
        : base(adornedElement)
    {
        IsHitTestVisible = false;
        _offsetX = offsetX;
        _offsetY = offsetY;

        // Capture a static bitmap snapshot so dimming the original doesn't affect the ghost
        var snapshot = CaptureSnapshot(sourceElement);

        _ghost = new Rectangle
        {
            Width = sourceElement.RenderSize.Width,
            Height = sourceElement.RenderSize.Height,
            Fill = new ImageBrush(snapshot),
            Opacity = 0.75,
            IsHitTestVisible = false
        };

        AddVisualChild(_ghost);
    }

    public void SetPosition(double mouseX, double mouseY)
    {
        _left = mouseX - _offsetX;
        _top = mouseY - _offsetY;
        InvalidateArrange();
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _ghost;

    protected override Size MeasureOverride(Size constraint)
    {
        _ghost.Measure(constraint);
        return _ghost.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _ghost.Arrange(new Rect(_left, _top, _ghost.Width, _ghost.Height));
        return finalSize;
    }

    private static RenderTargetBitmap CaptureSnapshot(UIElement element)
    {
        var size = element.RenderSize;
        var dpi = VisualTreeHelper.GetDpi(element);
        var rtb = new RenderTargetBitmap(
            (int)Math.Ceiling(size.Width * dpi.DpiScaleX),
            (int)Math.Ceiling(size.Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        rtb.Render(element);
        return rtb;
    }
}
