using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace TerminalWidget;
public sealed class WidgetDialog : Window
{
    public WidgetDialog()
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Template = (ControlTemplate)Application.Current.FindResource("DialogFrame");
        WindowStyle = WindowStyle.None;
        Background = new SolidColorBrush(Color.FromRgb(8,10,11));
        Resources.Add(typeof(TextBox), FieldStyle(typeof(TextBox)));
        Resources.Add(typeof(PasswordBox), FieldStyle(typeof(PasswordBox)));
        var combo = new Style(typeof(ComboBox), (Style)Application.Current.FindResource(typeof(ComboBox)));
        combo.Setters.Add(new Setter(MinHeightProperty, 34d));
        combo.Setters.Add(new Setter(Control.FontSizeProperty, 13d));
        Resources.Add(typeof(ComboBox), combo);
        var button = new Style(typeof(Button), (Style)Application.Current.FindResource(typeof(Button)));
        button.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(20,25,28))));
        button.Setters.Add(new Setter(Control.ForegroundProperty,new SolidColorBrush(Color.FromRgb(198,209,215))));
        button.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,7,10,7)));
        button.Setters.Add(new Setter(MarginProperty,new Thickness(0,5,0,5)));
        Resources.Add(typeof(Button),button);
        PreviewKeyDown += (_, e) => { if(e.Key == Key.Escape && !e.Handled) { Close(); e.Handled=true; } };
    }
    private static Style FieldStyle(Type type)
    {
        var style = new Style(type, Application.Current.TryFindResource(type) as Style);
        style.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(16,20,22))));
        style.Setters.Add(new Setter(Control.ForegroundProperty,new SolidColorBrush(Color.FromRgb(220,226,230))));
        style.Setters.Add(new Setter(Control.BorderBrushProperty,new SolidColorBrush(Color.FromRgb(48,57,61))));
        style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,8,10,8)));
        style.Setters.Add(new Setter(Control.FontSizeProperty,14d));
        return style;
    }
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if(GetTemplateChild("DialogClose") is Button close)close.Click+=(_,_)=>Close();
        if(GetTemplateChild("DialogDrag") is Thumb drag)drag.DragDelta+=(_,e)=>{Left+=e.HorizontalChange;Top+=e.VerticalChange;};
    }
}
