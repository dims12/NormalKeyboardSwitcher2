using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NormalKeyboardSwitcher2;

internal sealed record LanguageChoice(nint Handle,string Name)
{
    public string Badge
    {
        get
        {
            try { return System.Globalization.CultureInfo.GetCultureInfo((int)((long)Handle & 0xffff)).ThreeLetterISOLanguageName.ToUpperInvariant(); }
            catch (System.Globalization.CultureNotFoundException) { return App.Label(Handle); }
        }
    }
    public override string ToString() => Name;
}
internal sealed record GestureChoice(Gesture Value,string Name)
{
    public override string ToString() => Name;
}

internal sealed class SettingsWindow : Window
{
    private readonly ListBox languages=new() { MinHeight=120,MaxHeight=210,Margin=new Thickness(0,8,0,8) };
    private readonly ComboBox gesture=new() { Margin=new Thickness(0,8,0,14),Padding=new Thickness(6) };
    private readonly CheckBox indicator=new() { Content="Show language beside the caret",Margin=new Thickness(0,5,0,14) };
    private readonly CheckBox blinking=new() { Content="Blink language indicator",Margin=new Thickness(0,0,0,14) };
    private readonly CheckBox startup=new() { Content="Start with Windows",Margin=new Thickness(0,0,0,14) };
    private readonly TextBlock status=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,18,0,0),FontSize=12 };
    private readonly GestureChoice[] gestures = {
        new(Gesture.System,"System"), new(Gesture.CtrlShift,"Ctrl + Shift"), new(Gesture.AltShift,"Left Alt + Shift")
    };
    private nint[] displayed=Array.Empty<nint>();
    private int clickCount=1;

    internal SettingsWindow(App app,Settings settings)
    {
        Title="Normal Keyboard Switcher 2"; Width=620; MinWidth=480; MinHeight=520;
        SizeToContent=SizeToContent.Height;
        MaxHeight=SystemParameters.WorkArea.Height;
        Icon=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/AppIcon.ico"));
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var panel=new StackPanel { Margin=new Thickness(24) };
        Content=new ScrollViewer { Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text="Languages",FontSize=22,FontWeight=FontWeights.SemiBold });
        var rowStyle=new Style(typeof(ListBoxItem));
        rowStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,7,10,7)));
        languages.ItemContainerStyle=rowStyle;
        languages.ItemTemplate=(DataTemplate)System.Windows.Markup.XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <StackPanel Orientation="Horizontal">
                <Border Background="{DynamicResource {x:Static SystemColors.ControlBrushKey}}"
                        BorderBrush="{DynamicResource {x:Static SystemColors.ControlDarkBrushKey}}"
                        BorderThickness="1" CornerRadius="3" Padding="5,2" Margin="0,0,10,0" MinWidth="44">
                  <TextBlock Text="{Binding Badge}" Foreground="{DynamicResource {x:Static SystemColors.ControlTextBrushKey}}"
                             FontSize="11" FontWeight="SemiBold" HorizontalAlignment="Center" />
                </Border>
                <TextBlock Text="{Binding Name}" VerticalAlignment="Center" />
              </StackPanel>
            </DataTemplate>
            """);
        panel.Children.Add(languages);
        panel.Children.Add(new TextBlock { Text="Click to switch. Double-click to switch and close.",
            TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,0,0,20) });
        languages.PreviewMouseLeftButtonDown+=(_,e)=>clickCount=e.ClickCount;
        languages.PreviewMouseLeftButtonUp+=async (_,e)=>
        {
            if (e.OriginalSource is not DependencyObject source ||
                ItemsControl.ContainerFromElement(languages,source) is not ListBoxItem row ||
                row.Content is not LanguageChoice choice) return;
            bool close=clickCount>=2;
            try
            {
                await app.SelectLanguage(choice.Handle,close);
                if (close) Close();
            }
            catch (Exception ex) { MessageBox.Show(this,ex.Message,Title); }
        };
        panel.Children.Add(new TextBlock { Text="Switching gesture",FontWeight=FontWeights.SemiBold });
        gesture.ItemsSource=gestures;
        panel.Children.Add(gesture);
        var help=new TextBlock { TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,0,0,12) };
        gesture.SelectionChanged+=(_,_)=>help.Text=SelectedGesture==Gesture.System
            ? "Use Windows switching. OK restores the Windows shortcut previously managed by this app."
            : "Switch in recently used order. OK automatically disables the matching Windows shortcut while this app is running.";
        panel.Children.Add(help);
        panel.Children.Add(indicator);
        panel.Children.Add(blinking);
        panel.Children.Add(startup);
        var buttons=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right };
        if (!App.IsElevated())
        {
            var admin=new Button { Content="Restart as administrator",Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,10,0) };
            admin.Click+=(_,_)=>app.RestartElevated(); buttons.Children.Add(admin);
        }
        var ok=new Button { Content="OK",Width=80,Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,0),IsDefault=true };
        ok.Click+=(_,_)=> {
            try { app.Apply(SelectedGesture,indicator.IsChecked==true,blinking.IsChecked==true,startup.IsChecked==true); Close(); }
            catch(Exception ex) { MessageBox.Show(this,ex.Message,Title); }
        };
        var cancel=new Button { Content="Cancel",Width=80,Padding=new Thickness(12,7,12,7),IsCancel=true };
        cancel.Click+=(_,_)=>Close();
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        panel.Children.Add(status);
        LoadSettings(settings);
    }
    private Gesture SelectedGesture => (gesture.SelectedItem as GestureChoice)?.Value ?? Gesture.System;
    internal void LoadSettings(Settings settings)
    {
        gesture.SelectedItem=gestures.First(x=>x.Value==settings.Gesture);
        indicator.IsChecked=settings.ShowIndicator;
        blinking.IsChecked=settings.BlinkIndicator;
        startup.IsChecked=StartupRegistration.Enabled;
        displayed=Array.Empty<nint>();
    }
    internal void UpdateStatus(string text,LanguageChoice[] choices,nint selected)
    {
        status.Text=text;
        // Keep row order stable for double-clicks. Only additions/removals rebuild it.
        if (choices.Length!=displayed.Length || choices.Any(x=>!displayed.Contains(x.Handle)))
        {
            displayed=choices.Select(x=>x.Handle).ToArray();
            languages.ItemsSource=choices;
        }
        languages.SelectedItem=languages.Items.OfType<LanguageChoice>().FirstOrDefault(x=>x.Handle==selected);
    }
}

