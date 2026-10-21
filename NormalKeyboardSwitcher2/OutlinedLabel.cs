using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace NormalKeyboardSwitcher2;

// Outline only the letters: the overlay background remains fully transparent.
internal sealed class OutlinedLabel : FrameworkElement
{
    private string text = "";
    private Color color = Colors.Black;
    private Brush fill = Brushes.Black;
    private Pen outline = MakeOutline(Colors.Black);
    internal OutlinedLabel()
    {
        TextOptions.SetTextRenderingMode(this,TextRenderingMode.Grayscale);
        TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
    }
    internal string Text
    {
        get => text;
        set { if (text == value) return; text = value; InvalidateVisual(); }
    }
    internal void SetColor(Color value)
    {
        if (color == value) return;
        color = value;
        var brush = new SolidColorBrush(value);
        brush.Freeze();
        fill = brush;
        outline = MakeOutline(value);
        InvalidateVisual();
    }
    private static Pen MakeOutline(Color value)
    {
        double luminance = 0.2126 * value.R + 0.7152 * value.G + 0.0722 * value.B;
        var pen = new Pen(luminance < 128 ? Brushes.White : Brushes.Black, 1.0)
        { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        return pen;
    }
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (text.Length == 0) return;
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            9, fill, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double scale=VisualTreeHelper.GetDpi(this).DpiScaleY;
        var origin=new Point(2, Math.Round(Math.Max(0,(ActualHeight-formatted.Height)/2)*scale)/scale);
        var geometry = formatted.BuildGeometry(origin);
        // Paint the halo first, then fill to keep the original glyph strokes intact.
        drawingContext.DrawGeometry(null, outline, geometry);
        drawingContext.DrawText(formatted,origin);
    }
}
