using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace TerminalWidget;
public static class ScrollActivity
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(ScrollActivity), new PropertyMetadata(false, Enable));
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.RegisterAttached("Active", typeof(bool), typeof(ScrollActivity), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetActive(DependencyObject target, bool value) => target.SetValue(ActiveProperty, value);
    public static bool GetActive(DependencyObject target) => (bool)target.GetValue(ActiveProperty);
    private static void Enable(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ScrollViewer viewer || args.NewValue is not true) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        void Show() { SetActive(viewer, true); timer.Stop(); timer.Start(); }
        timer.Tick += (_, _) => { timer.Stop(); SetActive(viewer, false); };
        viewer.PreviewMouseMove += (_, _) => Show();
        viewer.PreviewMouseWheel += (_, _) => Show();
        viewer.PreviewKeyDown += (_, _) => Show();
        viewer.GotKeyboardFocus += (_, _) => Show();
        viewer.Unloaded += (_, _) => { timer.Stop(); SetActive(viewer, false); };
    }
}
