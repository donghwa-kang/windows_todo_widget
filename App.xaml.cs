using System;
using System.Threading;
using System.Windows;

namespace TerminalWidget;
public partial class App : Application
{
    internal static void Trace(string message) { try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"window-diagnostic.log"),DateTime.Now.ToString("O")+" "+message+Environment.NewLine); } catch(System.IO.IOException) {} }
    private Mutex? mutex;
    private EventWaitHandle? showEvent;
    private RegisteredWaitHandle? showWait;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Trace("startup " + Environment.ProcessId);
        mutex = new Mutex(true, "Local\\TerminalWidget.Desktop.v1", out bool first);
        showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\TerminalWidget.Show.v1");
        if (!first) { showEvent.Set(); Shutdown(); return; }
        try
        {
            Trace("constructing"); var widget = new WidgetWindow(); MainWindow = widget; Trace("constructed");
            showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, (_,_) => Dispatcher.BeginInvoke(new Action(widget.ShowWidget)), null, Timeout.Infinite, false);
            MainWindow.Show();
            Trace("Show returned: " + MainWindow.IsVisible);
            Dispatcher.BeginInvoke(new Action(widget.ShowWidget), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
        catch (Exception ex) { MessageBox.Show("위젯을 시작할 수 없습니다: " + ex.Message); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { showWait?.Unregister(null); showEvent?.Dispose(); mutex?.Dispose(); base.OnExit(e); }
}
