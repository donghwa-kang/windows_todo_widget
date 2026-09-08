using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TerminalWidget.Core;

namespace TerminalWidget;
public static class Dialogs
{
    public static Window Form(string title, out StackPanel panel)
    {
        panel = new StackPanel { Margin = new Thickness(24,14,24,24) };
        var window = new WidgetDialog { Title = title, Width = 410, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = panel, ShowInTaskbar = true, Topmost = true, MaxHeight = Math.Min(850,SystemParameters.WorkArea.Height-40) };
        window.Loaded+=(_,_)=>window.Dispatcher.BeginInvoke(new Action(()=>new TerminalWidget.Platform.DesktopHost(window).Reveal()));
        return window;
    }
    public static void Label(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping=TextWrapping.Wrap,FontSize=12,Foreground=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(158,172,179)),Margin = new Thickness(0,14,0,6) });
    public static Button Button(string text, Action action)
    {
        var b = new Button { Content = text };
        if(text is "저장" or "설정 저장" or "연결 확인 및 시작")
        {
            b.Background=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(38,58,48));
            b.Foreground=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(218,238,226));
            b.BorderBrush=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(71,102,84));
            b.FontWeight=FontWeights.SemiBold;b.Margin=new Thickness(0,16,0,0);b.MinHeight=38;
        }
        b.Click += (_,_) => action(); return b;
    }
    public static Project? EditProject(Project? project) => ProjectEditor.Show(project);
    public static TaskItem? EditTask(TaskItem? task, Workspace state, Guid? selected)
    {
        var w = Form(task == null ? "할 일 추가" : "할 일 수정", out var panel);
        Label(panel,"할 일"); var title = new TextBox { Text = task?.Title ?? "", MaxLength = 500 }; panel.Children.Add(title);
        Label(panel,"프로젝트"); var projects = new ComboBox(); projects.Items.Add(new ComboBoxItem { Content = "독립 할 일", Tag = null });
        foreach (var p in state.Projects) projects.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
        projects.SelectedIndex = 0;
        foreach (ComboBoxItem item in projects.Items) if (Equals(item.Tag, task?.ProjectId ?? selected)) projects.SelectedItem = item;
        panel.Children.Add(projects);
        Label(panel,"우선순위"); var priority = new ComboBox { ItemsSource = Rules.Priorities, SelectedItem = task?.Priority ?? "보통" }; panel.Children.Add(priority);
        Label(panel,"마감일 (선택)"); var due = new DatePicker { SelectedDate = task?.Due?.ToDateTime(TimeOnly.MinValue), Margin = new Thickness(0,4,0,8) }; panel.Children.Add(due);
        TaskItem? result = null;
        void Save() { if (string.IsNullOrWhiteSpace(title.Text)) { title.Focus(); return; } result = new TaskItem(task?.Id ?? Guid.NewGuid(), title.Text.Trim(), (Guid?)((ComboBoxItem)projects.SelectedItem).Tag, (string)priority.SelectedItem, due.SelectedDate is {} date ? DateOnly.FromDateTime(date) : null, task?.CompletedAt); w.DialogResult = true; }
        panel.Children.Add(Button("저장", Save)); title.KeyDown += (_,e) => { if (e.Key == Key.Enter) Save(); }; w.PreviewKeyDown += (_,e) => { if(e.Key == Key.Escape) w.Close(); }; w.Loaded += (_,_) => title.Focus(); w.ShowDialog(); return result;
    }
}
