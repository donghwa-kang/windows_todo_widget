using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace TerminalWidget;

public sealed class KlasSync
{
    private readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TerminalWidget", "klas-cache.json");
    private KlasWindow? window;
    public Core.KlasSnapshot? Snapshot { get; private set; }
    public string Status { get; private set; } = "KLAS를 연결해 수강 과목 현황을 가져오세요.";
    public bool Busy { get; private set; }
    public bool LastRefreshFailed { get; private set; }
    public event Action? Changed;
    public KlasSync()
    {
        if (!File.Exists(path)) return;
        try { if (new FileInfo(path).Length > 1024 * 1024) throw new FormatException(); Snapshot = Core.KlasRules.ParseCache(File.ReadAllText(path)); Status = "저장된 목록 · ↻로 최신 현황 확인"; }
        catch (Exception ex) when (ex is JsonException or FormatException or IOException or UnauthorizedAccessException) { Status = "KLAS 저장 목록을 읽지 못했습니다. 원본을 보존했습니다. 다시 연결해 주세요."; }
    }
    private bool Save(Core.KlasSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var file = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(file, snapshot); file.Flush(true); }
            if (File.Exists(path)) File.Replace(path + ".tmp", path, path + ".bak"); else File.Move(path + ".tmp", path);
            Snapshot = snapshot; LastRefreshFailed = false; Status = "수강 과목 현황 갱신 완료"; Changed?.Invoke(); return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    private KlasWindow Browser()
    {
        if (window == null || window.IsClosed) window = new KlasWindow(Save);
        return window;
    }
    public async void Connect()
    {
        var target = Browser(); target.Reveal();
        try { await target.Initialize(); }
        catch (Exception) { Status = "KLAS 창을 열지 못했습니다. 인터넷 연결과 WebView2 Runtime 설치를 확인해 주세요."; Changed?.Invoke(); target.Shutdown(); }
    }
    public async Task Refresh()
    {
        if (Busy || Snapshot == null) return;
        Busy = true; Status = "KLAS 현황 조회 중…"; Changed?.Invoke();
        try
        {
            var target = Browser();
            if (!target.IsLoaded) { target.Opacity = 0; target.ShowInTaskbar = false; target.Show(); }
            if (!await target.Read()) { LastRefreshFailed = true; Status = "KLAS 갱신 안 됨 · 이전 목록 유지 · 연결 창에서 로그인 상태 확인"; }
        }
        finally { Busy = false; Changed?.Invoke(); }
    }
    public void Close() => window?.Shutdown();
}
