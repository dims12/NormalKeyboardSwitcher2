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
    private Brush outline = Brushes.White;
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
        outline = Outline(value);
        InvalidateVisual();
    }
    private static Brush Outline(Color value)
    {
        double luminance = 0.2126 * value.R + 0.7152 * value.G + 0.0722 * value.B;
        return luminance < 128 ? Brushes.White : Brushes.Black;
    }
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (text.Length == 0) return;
        var dpi=VisualTreeHelper.GetDpi(this);
        FormattedText Format(Brush brush) => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            9, brush, null, TextFormattingMode.Display, dpi.PixelsPerDip);
        var formatted=Format(fill);
        var halo=Format(outline);
        double scale=dpi.DpiScaleY;
        var origin=new Point(2, Math.Round(Math.Max(0,(ActualHeight-formatted.Height)/2)*scale)/scale);
        // Use the same hinted glyphs for both layers. A vector outline and
        // DrawText disagree at small sizes, leaving broken-looking letter edges.
        for(int y=-1;y<=1;y++)
            for(int x=-1;x<=1;x++)
                if(x!=0 || y!=0)
                    drawingContext.DrawText(halo,new Point(origin.X+x/dpi.DpiScaleX,origin.Y+y/scale));
        drawingContext.DrawText(formatted,origin);
    }
}
