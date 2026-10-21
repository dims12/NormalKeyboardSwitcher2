using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace NormalKeyboardSwitcher2;
internal static class Preview
{
    internal static void RenderContrast(string path)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.LightGray, null, new Rect(0,0,480,180));
            var backgrounds = new Brush[] { Brushes.White, Brushes.Black, Brushes.Teal, Brushes.Gray };
            for (int row=0; row<3; row++)
                for (int col=0; col<4; col++)
                {
                    double x=col*120, y=row*60, scale=1+row*0.5;
                    dc.DrawRectangle(backgrounds[col], null, new Rect(x,y,120,60));
                    for (int ink=0; ink<2; ink++)
                    {
                        var label = new OutlinedLabel { Text="EN", Width=24, Height=14 };
                        label.SetColor(ink==0 ? Colors.Black : Colors.White);
                        label.Measure(new Size(24,14));
                        label.Arrange(new Rect(0,0,24,14));
                        label.UpdateLayout();
                        dc.DrawRectangle(new VisualBrush(label) { Stretch=Stretch.Fill },null,
                            new Rect(x+8+ink*55,y+15,24*scale,14*scale));
                    }
                }
        }
        var bitmap=new RenderTargetBitmap(480,180,96,96,PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(path); encoder.Save(stream);
    }

    internal static void Render(App app,string path)
    {
        var window=new SettingsWindow(app,new Settings());
        window.UpdateStatus("Switching enabled · Standard permissions\nSelected English (United States)\nCaret: Windows caret",
            new[]{new LanguageChoice(0x409,"English (United States)"),new LanguageChoice(0x419,"Russian"),new LanguageChoice(0x407,"German")},0x409);
        var content=(FrameworkElement)window.Content;
        content.Measure(new Size(620,590));
        content.Arrange(new Rect(0,0,620,590));
        content.UpdateLayout();
        var visual=new DrawingVisual();
        using (var drawing=visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White,null,new Rect(0,0,620,590));

        }
        var image=new RenderTargetBitmap(620,590,96,96,PixelFormats.Pbgra32);
        image.Render(visual);
        image.Render(content);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream=File.Create(path); encoder.Save(stream);
    }
}



