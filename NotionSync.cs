using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using TerminalWidget.Core;
using TerminalWidget.Platform;
namespace TerminalWidget;
public sealed class NotionSync
{
    private readonly string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TerminalWidget","notion-cache.json");

    public NotionCache Cache {get;private set;}=new(false,new());
    public bool Busy {get;private set;}
    public string Status {get;private set;}="노션 연결 안 됨";
    public event Action? Changed;
    private NotionTask? deleted;
    public bool CanUndo=>deleted!=null;

    public NotionSync()
    {
        if(File.Exists(path))try {var cache=JsonSerializer.Deserialize<NotionCache>(File.ReadAllText(path));if(cache==null||cache.Tasks==null||cache.Tasks.Any(t=>t==null||t.Id==Guid.Empty||t.Fields==null)||cache.Tasks.Select(t=>t.Id).Distinct().Count()!=cache.Tasks.Count)throw new FormatException();Cache=cache;}
        catch(Exception e) when(e is JsonException or FormatException or IOException){File.Copy(path,path+".corrupt-"+DateTime.Now.ToString("yyyyMMddHHmmssfff"));Status="노션 캐시 복구 필요 · 설정에서 재연결하세요.";}
        if(!Guid.TryParse(Cache.SourceId,out var source)||source==Guid.Empty)
        {Cache=Cache with {Enabled=false};if(Cache.Tasks.Count>0)Status="노션 데이터 소스 ID 설정 필요 · 기존 캐시는 보존됩니다.";}
        else if(Cache.Enabled)Status="노션 · 마지막 저장 목록";
    }
    private void Save(NotionCache next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+".tmp";
        using(var file=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(file,next);file.Flush(true);}
        if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);Cache=next;
    }
    private NotionClient Client()=>new(NotionCredential.Load()??throw new InvalidOperationException("설정에서 노션 인증 토큰을 입력하세요."),Cache.SourceId??throw new InvalidOperationException("노션 데이터 소스 ID를 설정하세요."));
    private async Task<bool> Run(Func<NotionClient,Task> operation)
    {
        if(!Cache.Enabled){Status="노션 연결이 중지되어 변경하지 않았습니다.";Changed?.Invoke();return false;}
        if(Busy){Status="노션 동기화 중입니다. 잠시 후 다시 시도하세요.";Changed?.Invoke();return false;}
        Busy=true;
        try {using var api=Client();await operation(api);Status=$"노션 · {DateTime.Now:HH:mm:ss} 동기화";return true;}
        catch(Exception e){Status=e is TaskCanceledException?"노션 응답 지연 · 변경 결과를 확인하세요.":e is System.Net.Http.HttpRequestException?"노션 연결 실패 · 마지막 목록 유지":e.Message;return false;}
        finally {Busy=false;Changed?.Invoke();}
    }
    public async Task Refresh()
    {
        if(!Cache.Enabled||Busy)return;
        await Run(async api=>{var list=await api.List(Cache.Tasks);Save(Cache with {Tasks=list,LastSync=DateTimeOffset.Now});});
    }
    public Task<bool> Create(TaskItem task)=>Run(async api=>
    {
        if(Cache.PendingCreate!=null)throw new InvalidOperationException("이전 생성 결과를 먼저 확인하세요. 설정 → 노션 연결에서 해결할 수 있습니다.");
        var fields=NotionMapping.FromTask(task);Save(Cache with {PendingCreate=fields});
        NotionTask created;
        try {created=await api.Create(fields);}
        catch(NotionHttpException e) when(e.Status>=400&&e.Status<500&&e.Status!=408){Save(Cache with {PendingCreate=null});throw;}
        Save(Cache with {Tasks=Cache.Tasks.Where(t=>t.Id!=created.Id).Append(created).ToList(),PendingCreate=null,LastSync=DateTimeOffset.Now});
    });
    public Task<bool> Update(NotionTask task,TaskItem next)=>Run(async api=>{var updated=await api.Update(task,NotionMapping.FromTask(next));Save(Cache with {Tasks=Cache.Tasks.Select(t=>t.Id==updated.Id?updated:t).ToList()});});
    public Task<bool> Delete(NotionTask task)=>Run(async api=>{await api.Trash(task);Save(Cache with {Tasks=Cache.Tasks.Where(t=>t.Id!=task.Id).ToList()});deleted=task;});
    public Task<bool> Undo()=>Run(async api=>{if(deleted==null)return;await api.Restore(deleted.Id);Save(Cache with {Tasks=Cache.Tasks.Where(t=>t.Id!=deleted.Id).Append(deleted).ToList()});deleted=null;});

    public void Settings()
    {
        var w=Dialogs.Form("노션 양방향 연결",out var panel);w.Width=440;w.Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        var explanation=new TextBlock {Text="워크스페이스 → 할 일 & 일정\n상단 ↻로 최신 목록 가져오기 · 위젯 변경은 즉시 전송\n삭제는 노션 휴지통으로 이동합니다.\n기존 로컬 할 일은 유지하고, 연결 후 새 할 일은 노션에 추가합니다.",TextWrapping=TextWrapping.Wrap};panel.Children.Add(explanation);
        Dialogs.Label(panel,"노션 데이터 소스 ID (data_source_id · 페이지/보기 ID와 다름)");
        var sourceInput=new TextBox {Text=Cache.SourceId??""};panel.Children.Add(sourceInput);
        Dialogs.Label(panel,"노션 내부 연결 토큰 (Windows 자격 증명 저장소에 저장)");var token=new PasswordBox {Margin=new Thickness(0,8,0,8),Padding=new Thickness(8)};panel.Children.Add(token);
        var result=new TextBlock {Text=Status,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};panel.Children.Add(result);
        Dialogs.Label(panel,"노션에서 내부 연결을 만들고 원본 ‘할 일 & 일정’ 데이터베이스에 연결 권한을 추가하세요. 읽기·삽입·수정 권한이 필요합니다.");
        panel.Children.Add(Dialogs.Button("노션 연결 설정 열기",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.notion.so/profile/integrations"){UseShellExecute=true})));
        var connect=Dialogs.Button("연결 확인 및 시작",()=>{});panel.Children.Add(connect);
        connect.Click+=async(_,_)=>
        {
            if(Busy)return;
            if(!Guid.TryParse(sourceInput.Text.Trim(),out var source)||source==Guid.Empty){result.Text="올바른 데이터 소스 ID를 입력하세요.";return;}
            bool sourceChanged=!Guid.TryParse(Cache.SourceId,out var oldSource)||source!=oldSource;
            if(sourceChanged&&Cache.PendingCreate!=null){result.Text="이전 생성 결과를 아래에서 확인한 뒤 데이터 소스를 변경하세요.";return;}
            if(sourceChanged&&Cache.Tasks.Count>0&&MessageBox.Show(w,"연결 대상을 변경하면 위젯의 노션 목록을 새 데이터 소스 목록으로 교체합니다. 노션 원본과 로컬 할 일은 삭제하지 않습니다. 계속할까요?","연결 대상 변경",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            Busy=true;panel.IsEnabled=false;result.Text="접근 권한과 속성을 확인하는 중…";
            try {string secret=string.IsNullOrWhiteSpace(token.Password)?NotionCredential.Load()??"":token.Password.Trim();if(secret.Length==0)throw new InvalidOperationException("인증 토큰을 입력하세요.");using var client=new NotionClient(secret,source.ToString());await client.Validate();var items=await client.List(sourceChanged?Array.Empty<NotionTask>():Cache.Tasks);NotionCredential.Save(secret);Save(Cache with {Enabled=true,Tasks=items,LastSync=DateTimeOffset.Now,SourceId=source.ToString()});deleted=null;token.Clear();Status="노션 연결 완료 · ↻로 최신 목록 가져오기";result.Text=Status;}
            catch(Exception e){result.Text=e is TaskCanceledException?"연결 시간이 초과됐습니다.":e.Message;}
            finally{Busy=false;panel.IsEnabled=true;Changed?.Invoke();}
        };
        panel.Children.Add(Dialogs.Button("노션 연결 중지",()=>{if(Busy)return;Save(Cache with {Enabled=false});Status="노션 연결 중지 · 캐시 유지";Changed?.Invoke();w.Close();}));
        if(Cache.PendingCreate!=null)
        {
            Dialogs.Label(panel,"생성 결과 확인 필요: "+Cache.PendingCreate.Title);
            Dialogs.Label(panel,"노션에서 생성된 항목의 페이지 링크를 붙여넣으세요.");var page=new TextBox();panel.Children.Add(page);
            panel.Children.Add(Dialogs.Button("기존 노션 항목과 연결",async()=>
            {
                var match=System.Text.RegularExpressions.Regex.Match(page.Text,@"[0-9a-fA-F]{32}|[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
                if(!Guid.TryParse(match.Value,out var id)){result.Text="올바른 노션 페이지 링크를 입력하세요.";return;}
                await Run(async api=>{var item=await api.Get(id);if(item.Fields!=Cache.PendingCreate)throw new InvalidOperationException("입력한 항목이 생성 요청 내용과 다릅니다.");Save(Cache with {Tasks=Cache.Tasks.Where(t=>t.Id!=id).Append(item).ToList(),PendingCreate=null});});result.Text=Status;
            }));
            panel.Children.Add(Dialogs.Button("노션에 생성되지 않았음을 확인했어요",()=>{if(Busy)return;if(MessageBox.Show(w,"노션에 같은 항목이 없음을 직접 확인했나요? 확인 후 새로 추가할 수 있습니다.","생성 결과 확인",MessageBoxButton.YesNo)==MessageBoxResult.Yes){Save(Cache with {PendingCreate=null});result.Text="생성 대기를 해제했습니다. 다시 추가할 수 있습니다.";}}));
        }
        w.Closing+=(_,e)=>{if(Busy)e.Cancel=true;};w.ShowDialog();
    }
}
