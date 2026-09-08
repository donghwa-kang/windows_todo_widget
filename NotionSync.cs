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
        Dialogs.Label(panel,"연결 방법");
        var mode=new ComboBox {ItemsSource=new[]{"기존 데이터베이스 연결","새 할 일 데이터베이스 만들기"},SelectedIndex=0};panel.Children.Add(mode);
        var existingPanel=new StackPanel();panel.Children.Add(existingPanel);
        Dialogs.Label(existingPanel,"노션 데이터 소스 ID (data_source_id · 페이지/보기 ID와 다름)");
        var sourceInput=new TextBox {Text=Cache.DatabaseSetup?.SourceId??Cache.SourceId??""};existingPanel.Children.Add(sourceInput);
        var newPanel=new StackPanel {Visibility=Visibility.Collapsed};panel.Children.Add(newPanel);
        Dialogs.Label(newPanel,"생성할 위치의 노션 페이지 링크 또는 페이지 ID");
        var parentInput=new TextBox();newPanel.Children.Add(parentInput);
        Dialogs.Label(newPanel,"이 페이지 아래에 ‘할 일 & 일정’ 데이터베이스와 이름·완료·일정·우선순위·유형 속성을 만듭니다. 먼저 부모 페이지의 연결 메뉴에서 사용할 내부 연결을 추가하세요.");
        Dialogs.Label(panel,"노션 내부 연결 토큰 (Windows 자격 증명 저장소에 저장)");var token=new PasswordBox {Margin=new Thickness(0,8,0,8),Padding=new Thickness(8)};panel.Children.Add(token);
        var result=new TextBlock {Text=Status,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};panel.Children.Add(result);
        Dialogs.Label(panel,"내부 연결에 읽기·삽입·수정 권한이 필요합니다. 기존 연결은 대상 데이터베이스에, 새 생성은 부모 페이지에 연결 권한을 추가하세요.");
        panel.Children.Add(Dialogs.Button("노션 연결 설정 열기",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.notion.so/profile/integrations"){UseShellExecute=true})));
        var connect=Dialogs.Button("연결 확인 및 시작",()=>{});panel.Children.Add(connect);
        var createDatabase=Dialogs.Button("생성 및 연결",()=>{});createDatabase.Visibility=Visibility.Collapsed;panel.Children.Add(createDatabase);
        mode.SelectionChanged+=(_,_)=>{bool create=mode.SelectedIndex==1;existingPanel.Visibility=create?Visibility.Collapsed:Visibility.Visible;newPanel.Visibility=create?Visibility.Visible:Visibility.Collapsed;connect.Visibility=create?Visibility.Collapsed:Visibility.Visible;createDatabase.Visibility=create?Visibility.Visible:Visibility.Collapsed;};
        var setupNotice=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};panel.Children.Add(setupNotice);
        void UpdateSetupNotice()=>setupNotice.Text=Cache.DatabaseSetup==null?"":Cache.DatabaseSetup.SourceId is {} setupSource?"데이터베이스 생성 완료 · 기존 연결에서 다음 ID로 연결을 마무리하세요: "+setupSource:"이전 데이터베이스 생성 결과 확인 필요 · 노션 부모 페이지를 확인하세요. 생성된 경우 기존 연결을 사용하세요. 자동으로 다시 만들지 않습니다.";
        UpdateSetupNotice();
        panel.Children.Add(Dialogs.Button("생성 대기 기록 해제",()=>{if(Busy||Cache.DatabaseSetup==null)return;if(MessageBox.Show(w,"부모 페이지에서 생성 결과를 확인했나요? 기록만 해제하며 노션 데이터베이스는 삭제하지 않습니다. 이미 생성됐다면 기존 연결을 사용하세요.","생성 결과 확인",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;Save(Cache with {DatabaseSetup=null});UpdateSetupNotice();}));
        createDatabase.Click+=async(_,_)=>
        {
            if(Busy)return;
            if(Cache.DatabaseSetup!=null){result.Text="이전 생성 결과를 먼저 확인하세요. 생성 대기 기록이 있어 다시 만들지 않았습니다.";return;}
            if(Cache.PendingCreate!=null){result.Text="이전 할 일 생성 결과를 아래에서 해결한 뒤 연결 대상을 바꾸세요.";return;}
            Guid parent;string secret;
            try{parent=NotionClient.ParseParentPage(parentInput.Text);secret=string.IsNullOrWhiteSpace(token.Password)?NotionCredential.Load()??"":token.Password.Trim();if(secret.Length==0)throw new InvalidOperationException("인증 토큰을 입력하세요.");}
            catch(Exception e){result.Text=e.Message;return;}
            if(MessageBox.Show(w,"지정한 페이지 아래에 ‘할 일 & 일정’을 새로 만들고 위젯을 연결할까요? 기존 노션 목록은 새 목록으로 교체되며 원격 데이터와 로컬 할 일은 삭제하지 않습니다.","데이터베이스 생성",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            Busy=true;panel.IsEnabled=false;result.Text="데이터베이스 생성 중…";
            try
            {
                Save(Cache with {DatabaseSetup=new(parent)});
                string createdSource;
                try{createdSource=await NotionClient.CreateTaskDatabase(secret,parent);}
                catch(NotionHttpException e) when(e.Status>=400&&e.Status<500&&e.Status!=408){Save(Cache with {DatabaseSetup=null});throw;}
                // 후속 검증·연결 실패에도 생성된 ID를 보존해 재생성을 방지한다.
                Save(Cache with {DatabaseSetup=new(parent,createdSource)});sourceInput.Text=createdSource;mode.SelectedIndex=0;
                using var client=new NotionClient(secret,createdSource);await client.Validate();var items=await client.List(Array.Empty<NotionTask>());
                NotionCredential.Save(secret);Save(Cache with {Enabled=true,SourceId=createdSource,Tasks=items,LastSync=DateTimeOffset.Now,DatabaseSetup=null});deleted=null;token.Clear();
                Status="노션 데이터베이스 생성 및 연결 완료";result.Text=Status;
            }
            catch(Exception e){result.Text=Cache.DatabaseSetup==null?e.Message:"연결을 마치지 못했습니다. 아래 생성 기록을 확인하고 기존 연결로 이어가세요. "+(e is TaskCanceledException?"응답 시간이 초과됐습니다.":e.Message);}
            finally{Busy=false;panel.IsEnabled=true;UpdateSetupNotice();Changed?.Invoke();}
        };
        connect.Click+=async(_,_)=>
        {
            if(Busy)return;
            if(!Guid.TryParse(sourceInput.Text.Trim(),out var source)||source==Guid.Empty){result.Text="올바른 데이터 소스 ID를 입력하세요.";return;}
            bool sourceChanged=!Guid.TryParse(Cache.SourceId,out var oldSource)||source!=oldSource;
            if(sourceChanged&&Cache.PendingCreate!=null){result.Text="이전 생성 결과를 아래에서 확인한 뒤 데이터 소스를 변경하세요.";return;}
            if(sourceChanged&&Cache.Tasks.Count>0&&MessageBox.Show(w,"연결 대상을 변경하면 위젯의 노션 목록을 새 데이터 소스 목록으로 교체합니다. 노션 원본과 로컬 할 일은 삭제하지 않습니다. 계속할까요?","연결 대상 변경",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            if(Cache.DatabaseSetup!=null&&Cache.DatabaseSetup.SourceId!=source.ToString()&&MessageBox.Show(w,"선택한 데이터 소스에 연결하고 이전 데이터베이스 생성 대기 기록을 해제할까요? 노션 원본은 삭제하지 않습니다.","생성 기록 확인",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            Busy=true;panel.IsEnabled=false;result.Text="접근 권한과 속성을 확인하는 중…";
            try {string secret=string.IsNullOrWhiteSpace(token.Password)?NotionCredential.Load()??"":token.Password.Trim();if(secret.Length==0)throw new InvalidOperationException("인증 토큰을 입력하세요.");using var client=new NotionClient(secret,source.ToString());await client.Validate();var items=await client.List(sourceChanged?Array.Empty<NotionTask>():Cache.Tasks);NotionCredential.Save(secret);Save(Cache with {Enabled=true,Tasks=items,LastSync=DateTimeOffset.Now,SourceId=source.ToString(),DatabaseSetup=null});deleted=null;token.Clear();Status="노션 연결 완료 · ↻로 최신 목록 가져오기";result.Text=Status;}
            catch(Exception e){result.Text=e is TaskCanceledException?"연결 시간이 초과됐습니다.":e.Message;}
            finally{Busy=false;panel.IsEnabled=true;UpdateSetupNotice();Changed?.Invoke();}
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
