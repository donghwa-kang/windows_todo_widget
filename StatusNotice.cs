using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TerminalWidget;
// 기존 호출부의 Text 변경을 관찰하되 같은 메시지의 재렌더는 수명을 연장하지 않는다.
public sealed class StatusNotice : TextBlock
{
    private DispatcherTimer? timer;
    private bool expired;
    static StatusNotice() => TextProperty.OverrideMetadata(typeof(StatusNotice), new FrameworkPropertyMetadata("", (target, _) => ((StatusNotice)target).TextChanged()));
    public StatusNotice() { Unloaded += (_, _) => timer?.Stop(); }
    private void TextChanged()
    {
        timer?.Stop(); expired = false;
        Visibility = string.IsNullOrEmpty(Text) ? Visibility.Collapsed : Visibility.Visible;
        bool persistent = new[] { "실패", "오류", "불가", "필요", "중지", "지연", "확인하세요", "만료", "중…", "중입니다" }.Any(word => Text.Contains(word, StringComparison.Ordinal));
        if (string.IsNullOrEmpty(Text) || persistent) return;
        timer ??= CreateTimer(); timer.Start();
    }
    private DispatcherTimer CreateTimer()
    {
        var result = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        result.Tick += (_, _) => { result.Stop(); expired = true; Visibility = Visibility.Collapsed; };
        return result;
    }
    public void SetShown(bool shown) => Visibility = shown && !expired && !string.IsNullOrEmpty(Text) ? Visibility.Visible : Visibility.Collapsed;
}
