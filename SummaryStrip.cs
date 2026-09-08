using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TerminalWidget;

// 핵심 수치를 같은 폭으로 정렬해 긴 문장보다 빠르게 읽을 수 있게 한다.
public sealed class SummaryStrip : Grid
{
    private readonly TextBlock[] values = new TextBlock[3];
    public SummaryStrip()
    {
        string[] labels = { "남은 할 일", "오늘 완료", "진행 프로젝트" };
        for (int i = 0; i < labels.Length; i++)
        {
            ColumnDefinitions.Add(new ColumnDefinition());
            var panel = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 12, 0, 0, 0) };
            values[i] = new TextBlock { FontSize = 23, FontFamily = new FontFamily("Cascadia Mono, Consolas"), Foreground = new SolidColorBrush(Color.FromRgb(211, 223, 215)) };
            panel.Children.Add(values[i]);
            panel.Children.Add(new TextBlock { Text = labels[i], FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(146, 153, 160)), Margin = new Thickness(0, 3, 0, 0) });
            var border = new Border { Child = panel, BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(35, 39, 41)) };
            SetColumn(border, i); Children.Add(border);
        }
    }
    public void Update(int remaining, int completed, int projects)
    {
        int[] counts = { remaining, completed, projects };
        for (int i = 0; i < counts.Length; i++) values[i].Text = counts[i].ToString("D2");
    }
}
