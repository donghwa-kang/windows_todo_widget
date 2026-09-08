using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Automation;
namespace TerminalWidget;
// 참고 HTML의 4px 선 / 2px 간격을 실제 창 너비에 맞추어 그린다.
public sealed class SegmentedMeter : FrameworkElement
{
    private readonly int percent;
    public SegmentedMeter(int percent) { this.percent = Math.Clamp(percent,0,100); Height=5; Margin=new Thickness(0,8,0,6); AutomationProperties.SetName(this,$"진행률 {this.percent}%"); }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(32,34,34)),null,new Rect(0,0,ActualWidth,5));
        var ink=new SolidColorBrush(Color.FromRgb(141,156,146));double end=ActualWidth*percent/100;
        for(double x=0;x<end;x+=6) dc.DrawRectangle(ink,null,new Rect(x,0,Math.Min(4,end-x),5));
    }
}
