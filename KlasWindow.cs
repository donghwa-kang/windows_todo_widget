using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using TerminalWidget.Core;

namespace TerminalWidget;

public sealed class KlasWindow : Window
{
    public const string Home = "https://klas.kw.ac.kr/std/cmn/frame/Frame.do";
    private readonly WebView2 browser = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10) };
    private readonly Button read;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Func<KlasSnapshot, bool> save;
    private Task? initialization;
    private bool busy;
    private bool shuttingDown;
    private Task suspension = Task.CompletedTask;
    public bool IsClosed { get; private set; }
    public KlasWindow(Func<KlasSnapshot, bool> save)
    {
        this.save = save;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "KLAS 연결 · Windows Todo Widget"; Width = 1050; Height = 760; MinWidth = 650; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowActivated = false;
        var root = new DockPanel(); Content = root;
        var bar = new StackPanel { Margin = new Thickness(10) }; DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        bar.Children.Add(new TextBlock { Text = "KLAS에 직접 로그인하고 학기를 확인한 뒤 ‘현황 가져오기’를 누르세요. 크롬과 별도 로그인입니다.", TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; bar.Children.Add(buttons);
        read = Dialogs.Button("현황 가져오기", async () => await Read()); read.IsEnabled = false; buttons.Children.Add(read);
        buttons.Children.Add(Dialogs.Button("KLAS 홈", () => { if (browser.CoreWebView2 != null) browser.CoreWebView2.Navigate(Home); }));
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status); root.Children.Add(browser);
        Closing += (_, e) =>
        {
            if (shuttingDown) return;
            e.Cancel = true;
            Hide();
            if (!busy) _ = Pause();
        };
        Closed += (_, _) => { IsClosed = true; lifetime.Cancel(); browser.Dispose(); };
    }
    public void Shutdown() { shuttingDown = true; Close(); }
    private Task Pause() => suspension = SuspendCore();
    private async Task SuspendCore()
    {
        if (IsClosed || IsVisible || browser.CoreWebView2 == null) return;
        browser.Visibility = Visibility.Hidden;
        try
        {
            await browser.CoreWebView2.TrySuspendAsync();
            // 절전 요청 도중 사용자가 창을 다시 열었으면 즉시 복귀한다.
            if (!IsClosed && IsVisible) browser.CoreWebView2.Resume();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { App.Trace("KLAS suspend unavailable: " + ex.GetType().Name); }
    }
    public void Reveal()
    {
        if (IsClosed) return;
        browser.Visibility = Visibility.Visible;
        browser.CoreWebView2?.Resume();
        Opacity = 1; ShowInTaskbar = true; Show(); WindowState = WindowState.Normal;
        new Platform.DesktopHost(this).Reveal(); Activate();
    }
    public async Task Initialize() => await (initialization ??= InitializeCore());
    private async Task InitializeCore()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TerminalWidget", "klas-browser");
        var environment = await CoreWebView2Environment.CreateAsync(null, folder);
        lifetime.Token.ThrowIfCancellationRequested();
        await browser.EnsureCoreWebView2Async(environment);
        browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
        browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
        browser.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
        browser.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; };
        browser.CoreWebView2.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        browser.CoreWebView2.DownloadStarting += (_, e) => e.Cancel = true;
        browser.CoreWebView2.NavigationStarting += (_, e) =>
        {
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !(uri.Host == "kw.ac.kr" || uri.Host.EndsWith(".kw.ac.kr", StringComparison.OrdinalIgnoreCase))) e.Cancel = true;
        };
        var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e) { loaded.TrySetResult(e.IsSuccess); }
        browser.CoreWebView2.NavigationCompleted += Completed;
        try
        {
            browser.CoreWebView2.Navigate(Home);
            if (!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(35), lifetime.Token)) throw new InvalidOperationException("KLAS 페이지를 열지 못했습니다.");
            read.IsEnabled = !busy;
        }
        finally { browser.CoreWebView2.NavigationCompleted -= Completed; }
    }
    public async Task<bool> Read()
    {
        if (busy || IsClosed) return false;
        busy = true; read.IsEnabled = false; status.Text = "수강 과목과 남은 강의·과제를 읽는 중…";
        try
        {
            await Initialize();
            await suspension;
            lifetime.Token.ThrowIfCancellationRequested();
            var core = browser.CoreWebView2;
            core.Resume();
            if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var source) || source.Host != "klas.kw.ac.kr") throw new InvalidOperationException("KLAS 로그인을 완료하고 홈으로 이동해 주세요.");
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("TerminalWidget.Platform.KlasRead.js")!;
            using var reader = new StreamReader(resource);
            string script = await reader.ReadToEndAsync(lifetime.Token);
            string request = Guid.NewGuid().ToString("N");
            var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Received(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
            {
                if (!Uri.TryCreate(e.Source, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "klas.kw.ac.kr") return;
                try
                {
                    using var message = JsonDocument.Parse(e.WebMessageAsJson);
                    if (message.RootElement.TryGetProperty("request", out var id) && id.GetString() == request)
                        result.TrySetResult(message.RootElement.GetProperty("result").GetString()!);
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { }
            }
            core.WebMessageReceived += Received;
            try
            {
                // ExecuteScriptAsync는 Promise를 기다리지 않으므로 완료 메시지를 별도로 받는다.
                await core.ExecuteScriptAsync(script + ".then(result => chrome.webview.postMessage({ request: " + JsonSerializer.Serialize(request) + ", result }))");
                string json = await result.Task.WaitAsync(TimeSpan.FromSeconds(90), lifetime.Token);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.GetProperty("ok").GetBoolean()) throw new InvalidOperationException(doc.RootElement.GetProperty("error").GetString());
                var snapshot = KlasRules.ParseResponse(doc.RootElement.GetProperty("data").GetRawText(), DateTimeOffset.Now);
                if (!save(snapshot)) throw new IOException("KLAS 목록을 저장하지 못했습니다. 기존 목록을 유지합니다.");
                status.Text = "위젯에 반영했습니다.";
                Hide(); // 세션 쿠키가 사라지지 않도록 동일한 브라우저를 재사용한다.
                return true;
            }
            finally { if (!IsClosed) core.WebMessageReceived -= Received; }
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            if (!IsClosed) { status.Text = ex is TimeoutException ? "조회 시간이 초과되었습니다. 로그인 상태를 확인하고 다시 가져와 주세요." : ex.Message; Reveal(); }
            return false;
        }
        finally { busy = false; if (!IsClosed) { read.IsEnabled = true; if (!IsVisible) await Pause(); } }
    }
}
