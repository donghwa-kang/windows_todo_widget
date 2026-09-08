using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using TerminalWidget.Core;
using TerminalWidget.Platform;
using Forms = System.Windows.Forms;

namespace TerminalWidget;
public sealed class WidgetWindow : Window
{
    private readonly WorkspaceRepository repository;
    private readonly NotionSync notion;
    private readonly KlasSync klas = new();
    private readonly StackPanel klasItems = new();
    private readonly TextBlock klasHeading = new();
    private readonly StatusNotice notionStatus=new() {FontSize=10,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0)};
    private Workspace state;
    private Workspace? undo;
    private readonly DesktopHost host;
    private readonly Forms.NotifyIcon tray;
    private readonly DispatcherTimer timer;
    private readonly DispatcherTimer geometryTimer;
    private readonly StackPanel projects = new(), tasks = new();
    private readonly SummaryStrip summary = new();
    private readonly StatusNotice notice = new();
    private readonly TextBlock date = new();
    private readonly Grid body = new();
    private Guid? selected;
    private string filter = "전체";
    private readonly TextBlock taskHeading=new(), projectHeading=new();
    private readonly StackPanel filterButtons=new() {Orientation=Orientation.Horizontal};
    private Button? undoButton;
    private Button? refreshButton;
    private bool refreshing;
    private bool showCompletedProjects;
    private Button? completedProjectsButton;
    private bool ready, quitting;
    private int ticks;
    private DateOnly renderedDate;
    private bool recoveryMode = Array.Exists(Environment.GetCommandLineArgs(), a => a == "--window");
    public WidgetWindow()
    {
        // Implicit Window styles do not automatically match a derived Window type.
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "터미널 작업 위젯"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = recoveryMode;
        repository = new WorkspaceRepository(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TerminalWidget"));
        state = repository.Load(); var s = state.Settings;
        notion=new NotionSync();
        Width = s.Width; Height = s.Collapsed ? 125 : s.Height; MinWidth = 340; MinHeight = s.Collapsed ? 125 : 400; Left = s.X; Top = s.Y;
        host = new DesktopHost(this);
        var root = new DockPanel { Margin = new Thickness(18,10,18,10) };
        Content = new Border { Background=Brush("#000000"), BorderThickness = new Thickness(0), Child = root };
        UseLayoutRounding=true; SnapsToDevicePixels=true;
        var header = new DockPanel(); var headerFrame=new Border {Child=header,Padding=new Thickness(0,0,0,7)}; DockPanel.SetDock(headerFrame, Dock.Top); root.Children.Add(headerFrame);
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions);
        actions.Children.Add(Dialogs.Button("↕", Collapse)); actions.Children.Add(Dialogs.Button("⚙", Settings)); actions.Children.Add(Dialogs.Button("—", Hide));
        refreshButton=Dialogs.Button("↻",()=>RefreshProjects());refreshButton.ToolTip="노션 · 프로젝트 · KLAS 새로고침";actions.Children.Insert(0,refreshButton);
        var drag = new Thumb { Height = 30, Cursor = System.Windows.Input.Cursors.SizeAll, Template = new ControlTemplate(typeof(Thumb)) { VisualTree = HeaderVisual() } };
        drag.DragDelta += (_,e) => { var dpi = VisualTreeHelper.GetDpi(this); host.MoveBy((int)(e.HorizontalChange*dpi.DpiScaleX), (int)(e.VerticalChange*dpi.DpiScaleY)); }; drag.DragCompleted += (_,_) => { host.Clamp(); SaveGeometry(); }; header.Children.Add(drag);
        date.Foreground = Brush("#939BA2"); date.FontSize=11; date.Margin = new Thickness(0,10,0,12); DockPanel.SetDock(date,Dock.Top); root.Children.Add(date);
        summary.Margin = new Thickness(0,8,0,15); DockPanel.SetDock(summary,Dock.Top); root.Children.Add(summary);
        notionStatus.Foreground=Brush("#939DA5");DockPanel.SetDock(notionStatus,Dock.Bottom);root.Children.Add(notionStatus);
        var grip = new Thumb { Height = 6, Cursor = System.Windows.Input.Cursors.SizeNWSE, Background = Brush("#171717"), HorizontalAlignment = HorizontalAlignment.Right, Width = 16 };
        var gripVisual=new FrameworkElementFactory(typeof(Border));gripVisual.SetValue(Border.BackgroundProperty,Brush("#171717"));grip.Template=new ControlTemplate(typeof(Thumb)){VisualTree=gripVisual};
        grip.DragDelta += (_,e) => { if (!state.Settings.Collapsed) { Width = Math.Clamp(Width+e.HorizontalChange,340,1600); Height = Math.Clamp(Height+e.VerticalChange,400,1600); } }; grip.DragCompleted += (_,_) => SaveGeometry(); DockPanel.SetDock(grip,Dock.Bottom); root.Children.Add(grip);
        root.Children.Add(body); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1,GridUnitType.Star) }); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var projectArea = new StackPanel { Margin = new Thickness(0,18,0,0) }; Grid.SetRow(projectArea,1); body.Children.Add(projectArea);
        var ph = new DockPanel(); var add = Dialogs.Button("+", () => EditProject(null));add.ToolTip="프로젝트 추가"; DockPanel.SetDock(add,Dock.Right); ph.Children.Add(add);var all=Dialogs.Button("전체",()=>{selected=null;Render();});DockPanel.SetDock(all,Dock.Right);ph.Children.Add(all);projectHeading.Foreground=Brush("#939DA5");projectHeading.VerticalAlignment=VerticalAlignment.Center;ph.Children.Add(projectHeading); projectArea.Children.Add(ph);
        completedProjectsButton=Dialogs.Button("완료",()=>{showCompletedProjects=!showCompletedProjects;selected=null;Render();});completedProjectsButton.FontSize=11;completedProjectsButton.Padding=new Thickness(5,4,5,4);DockPanel.SetDock(completedProjectsButton,Dock.Right);ph.Children.Insert(1,completedProjectsButton);
        all.Content="선택 해제";all.FontSize=10;all.Padding=new Thickness(3,4,3,4);all.ToolTip="프로젝트 선택을 해제하고 모든 할 일 표시";
        projectArea.Children.Add(new ScrollViewer { Content = projects, MaxHeight = 155, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var taskArea = new DockPanel { Margin = new Thickness(0,10,0,0) }; Grid.SetRow(taskArea,0); body.Children.Add(taskArea);
        var th = new DockPanel(); DockPanel.SetDock(th,Dock.Top); taskArea.Children.Add(th);
        var addTask=Dialogs.Button("+",()=>EditTask(null));addTask.ToolTip="할 일 추가";addTask.Width=28;DockPanel.SetDock(addTask,Dock.Right);th.Children.Add(addTask);
        DockPanel.SetDock(filterButtons,Dock.Right);th.Children.Add(filterButtons);
        foreach(var option in new[]{"전체","미완료","완료"}) { var b=Dialogs.Button(option,()=>{filter=option;Render();});b.FontSize=11;b.Padding=new Thickness(5,4,5,4);filterButtons.Children.Add(b); }
        taskHeading.Foreground=Brush("#939DA5");taskHeading.VerticalAlignment=VerticalAlignment.Center;th.Children.Add(taskHeading);
        taskArea.Children.Add(new ScrollViewer { Content = tasks, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var bottom = new StackPanel { Margin = new Thickness(0,8,0,0) }; Grid.SetRow(bottom,2); body.Children.Add(bottom);
        notice.TextWrapping = TextWrapping.Wrap; notice.Foreground = Brush("#ADA08E"); bottom.Children.Add(notice);
        undoButton=Dialogs.Button("↶ 삭제 실행 취소",Undo);undoButton.HorizontalAlignment=HorizontalAlignment.Left;undoButton.FontSize=10;undoButton.Visibility=Visibility.Collapsed;bottom.Children.Add(undoButton);
        // 두 개의 고정 높이 목록 대신 한 흐름으로 배치해 항목이 적을 때 빈 간격을 없앤다.
        var taskScroll=(ScrollViewer)taskArea.Children[taskArea.Children.Count-1];taskScroll.Content=null;taskArea.Children.Remove(taskScroll);taskArea.Children.Add(tasks);
        var projectScroll=(ScrollViewer)projectArea.Children[1];projectScroll.Content=null;projectArea.Children.Remove(projectScroll);projectArea.Children.Add(projects);
        body.Children.Remove(taskArea);body.Children.Remove(projectArea);
        var flow=new StackPanel();flow.Children.Add(taskArea);flow.Children.Add(projectArea);
        th.Children.Remove(taskHeading);th.Children.Add(SectionButton(taskHeading,()=>Commit(state with {Settings=state.Settings with {TasksCollapsed=!state.Settings.TasksCollapsed}})));
        ph.Children.Remove(projectHeading);ph.Children.Add(SectionButton(projectHeading,()=>Commit(state with {Settings=state.Settings with {ProjectsCollapsed=!state.Settings.ProjectsCollapsed}})));
        var klasArea=new StackPanel {Margin=new Thickness(0,18,0,0)};
        projectArea.Children.Insert(0,new Border {Height=1,Background=Brush("#24282B"),Margin=new Thickness(0,0,0,10)});
        klasArea.Children.Add(new Border {Height=1,Background=Brush("#24282B"),Margin=new Thickness(0,0,0,10)});
        var kh=new DockPanel();var connectKlas=Dialogs.Button("열기",klas.Connect);connectKlas.ToolTip="KLAS 로그인 · 학기 선택 · 현황 가져오기";DockPanel.SetDock(connectKlas,Dock.Right);kh.Children.Add(connectKlas);
        kh.Children.Add(SectionButton(klasHeading,()=>Commit(state with {Settings=state.Settings with {KlasCollapsed=!state.Settings.KlasCollapsed}})));
        klasArea.Children.Add(kh);klasArea.Children.Add(klasItems);flow.Children.Add(klasArea);
        body.RowDefinitions[1].Height=new GridLength(0);
        body.Children.Add(new ScrollViewer {Content=flow,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "터미널 작업 위젯", Visible = true, ContextMenuStrip = new Forms.ContextMenuStrip() };
        tray.ContextMenuStrip.Items.Add("위젯 표시", null, (_,_) => Dispatcher.Invoke(ShowWidget)); tray.ContextMenuStrip.Items.Add("숨기기", null, (_,_) => Dispatcher.Invoke(Hide)); tray.ContextMenuStrip.Items.Add("종료", null, (_,_) => Dispatcher.Invoke(Quit)); tray.DoubleClick += (_,_) => Dispatcher.Invoke(ShowWidget);
        geometryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) }; geometryTimer.Tick += (_,_) => { geometryTimer.Stop(); SaveGeometry(); };
        SizeChanged += (_,_) => { if(ready) { geometryTimer.Stop(); geometryTimer.Start(); } };
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; timer.Tick += (_,_) => { if (!recoveryMode && !state.Settings.AlwaysOnTop && !host.Attached && IsVisible) ApplyMode(); if (++ticks % 12 == 0) { host.Clamp(); if(renderedDate!=DateOnly.FromDateTime(DateTime.Now))Render(); } };
        Loaded += (_,_) => { ready = true; ApplyMode(); host.Clamp(); Render(); timer.Start(); try { StartupRegistration.Set(state.Settings.AutoStart); } catch(Exception ex) { notice.Text = "자동 시작 등록 실패: " + ex.Message; } if(repository.RecoveryMessage != null) MessageBox.Show(repository.RecoveryMessage,"데이터 복구"); };
        Closing += (_,e) => { if(!quitting) { e.Cancel = true; Hide(); } };
        notion.Changed+=()=>{if(!quitting)Render();};
        klas.Changed+=()=>{if(!quitting)Render();};
    }
    private static Button SectionButton(TextBlock heading, Action toggle)
    {
        var button=Dialogs.Button("",toggle);button.Content=heading;button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Padding=new Thickness(0,5,0,5);button.ToolTip="클릭 또는 Enter/Space로 접기·펼치기";
        heading.Foreground=Brush("#C1CDC6");heading.FontFamily=new FontFamily("Cascadia Mono, Consolas");heading.FontSize=13;heading.FontWeight=FontWeights.SemiBold;return button;
    }
    private static FrameworkElementFactory HeaderVisual() { var area = new FrameworkElementFactory(typeof(Border)); area.SetValue(Border.BackgroundProperty,Brushes.Transparent); return area; }
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    private bool Commit(Workspace next, bool render=true)
    {
        try { repository.Save(next); state = next; if(render){notice.Text = ""; Render();} return true; }
        catch(Exception ex) { MessageBox.Show("저장하지 못했습니다. 변경을 적용하지 않았습니다.\n"+ex.Message,"저장 오류"); return false; }
    }
    private void Render()
    {
        var visibleProjects=state.Projects.Where(p=>(p.Status=="완료")==showCompletedProjects).OrderBy(p=>p.Status=="진행 중"?0:1).ThenBy(p=>p.Name).ToList();
        if(selected.HasValue&&visibleProjects.All(p=>p.Id!=selected))selected=null;
        if(completedProjectsButton!=null)
        {
            completedProjectsButton.Content=showCompletedProjects?"진행 보기":$"완료 {state.Projects.Count(p=>p.Status=="완료")}";
            completedProjectsButton.ToolTip=showCompletedProjects?"완료되지 않은 프로젝트 보기":"완료된 프로젝트만 보기";
        }
        var today = DateOnly.FromDateTime(DateTime.Now); date.Text = $"{DateTime.Now:yyyy.MM.dd}  ·  {DateTime.Now.ToString("dddd",System.Globalization.CultureInfo.GetCultureInfo("ko-KR"))}";
        renderedDate=today;
        summary.Update(state.Tasks.Count(t=>t.CompletedAt==null)+(notion.Cache.Enabled?notion.Cache.Tasks.Count(t=>!t.Fields.Done):0),state.Tasks.Count(t=>Rules.CompletedToday(t,today))+(notion.Cache.Enabled?notion.Cache.Tasks.Count(t=>t.Fields.Done&&t.KnownCompletedAt is {} c&&DateOnly.FromDateTime(c.LocalDateTime)==today):0),state.Projects.Count(p=>p.Status=="진행 중"));
        date.Visibility=state.Settings.Collapsed?Visibility.Collapsed:Visibility.Visible;
        summary.Margin=state.Settings.Collapsed?new Thickness(0,4,0,0):new Thickness(0,8,0,15);
        notionStatus.Text=notion.Cache.Enabled?notion.Status:"";notionStatus.SetShown(notion.Cache.Enabled&&!state.Settings.Collapsed);
        body.Visibility = state.Settings.Collapsed ? Visibility.Collapsed : Visibility.Visible;
        taskHeading.Text=$"> tasks  {state.Tasks.Count(t=>selected==null || t.ProjectId==selected)}";
        projectHeading.Text=$"{(state.Settings.ProjectsCollapsed?"▸":"▾")} projects  {visibleProjects.Count}";
        tasks.Visibility=state.Settings.TasksCollapsed?Visibility.Collapsed:Visibility.Visible;
        filterButtons.Visibility=tasks.Visibility;
        projects.Visibility=state.Settings.ProjectsCollapsed?Visibility.Collapsed:Visibility.Visible;
        RenderKlas();
        foreach(Button b in filterButtons.Children){bool active=Equals(b.Content,filter);b.Foreground=Brush(active?"#B5BFBA":"#656A6D");b.BorderBrush=Brush(active?"#303632":"#000000");}
        if(undoButton!=null)undoButton.Visibility=undo==null&&!notion.CanUndo?Visibility.Collapsed:Visibility.Visible;
        projects.Children.Clear(); tasks.Children.Clear();
        foreach(var project in visibleProjects)
        {
            var row = new DockPanel(); var edit = Dialogs.Button("⋯",()=>EditProject(project)); DockPanel.SetDock(edit,Dock.Right); row.Children.Add(edit);
            var progress = Rules.Progress(state,project.Id);
            int? shownPercent=project.ProgressMode=="수동"?project.ManualPercent:project.ProgressMode=="폴더 자동"?project.Snapshot?.Percent:progress.Percent;
            var card = new StackPanel { Margin = new Thickness(0,4,0,4) };
            var titleRow = new DockPanel();
            var percent = new TextBlock { Text=shownPercent.HasValue?$"{shownPercent}%":"—", FontFamily=new FontFamily("Cascadia Mono, Consolas"),FontSize=16,Foreground=Brush("#BCD2C4"), Margin=new Thickness(12,0,0,0) }; DockPanel.SetDock(percent,Dock.Right);titleRow.Children.Add(percent);
            titleRow.Children.Add(new TextBlock {Text=$"{(selected==project.Id ? "> " : "")}{project.Name}",FontSize=14,FontWeight=FontWeights.SemiBold,Foreground=Brush("#DBE1E5"),TextWrapping=TextWrapping.Wrap});card.Children.Add(titleRow);
            if(shownPercent.HasValue)card.Children.Add(new SegmentedMeter(shownPercent.Value));
            string detail=project.ProgressMode=="수동"?"수동":project.ProgressMode=="폴더 자동"?(project.Snapshot?.Error??(project.Snapshot==null?"새로고침 필요":$"자동 · {project.Snapshot.Done}/{project.Snapshot.Total} 완료")):$"할 일 · {progress.Done}/{progress.Total} 완료";
            if(project.ProgressMode=="폴더 자동"&&project.Snapshot?.Error!=null&&shownPercent.HasValue)detail+=" · 이전 값 유지";
            card.Children.Add(new TextBlock {Text=$"{project.Status} · {detail}",FontSize=11,Foreground=Brush("#9BA4AB"),TextWrapping=TextWrapping.Wrap});
            if(project.ProgressMode=="폴더 자동"&&project.Snapshot is {} snapshot)card.Children.Add(new TextBlock {Text=$"확인 {snapshot.CheckedAt.LocalDateTime:MM/dd HH:mm} · {snapshot.Source??"기준 없음"}",FontSize=11,Foreground=Brush("#879198"),TextWrapping=TextWrapping.Wrap});
            var button = Dialogs.Button("",()=>{selected=project.Id;Render();}); button.Content=card; button.HorizontalContentAlignment=HorizontalAlignment.Stretch; button.Padding=new Thickness(0); button.ToolTip=project.Description+(project.FolderPath==null?"":"\n"+project.FolderPath); row.Children.Add(button); projects.Children.Add(new Border {Child=row,Background=Brush(selected==project.Id?"#111916":"#0B0D0E"),CornerRadius=new CornerRadius(6),Padding=new Thickness(10,6,4,6),Margin=new Thickness(0,5,0,5)});
        }
        if(visibleProjects.Count==0) projects.Children.Add(new TextBlock { Text=showCompletedProjects?"완료된 프로젝트가 없습니다.":state.Projects.Count==0?"프로젝트를 추가해 진행률을 모아 보세요.":"남은 프로젝트가 없습니다. 완료 목록에서 다시 볼 수 있습니다.", Foreground=Brush("#939DA5"), TextWrapping=TextWrapping.Wrap, Margin=new Thickness(3,8,3,8) });
        var visible=state.Tasks.Where(t=>selected==null || t.ProjectId==selected).Where(t=>filter=="전체" || (filter=="완료")== (t.CompletedAt!=null)).OrderBy(t=>t.Due ?? DateOnly.MaxValue).ThenBy(t=>Array.IndexOf(Rules.Priorities,t.Priority)).ToList();
        foreach(var task in visible)
        {
            string due=task.Due is {} d ? (d<today?"기한 초과 ":d==today?"오늘 마감 ":"마감 ")+d.ToString("MM/dd") : "";
            var metadata=string.Join(" · ",new[]{task.CompletedAt!=null?"완료":"",task.Priority=="보통"?"":task.Priority,state.Projects.FirstOrDefault(p=>p.Id==task.ProjectId)?.Name??"",due}.Where(x=>!string.IsNullOrEmpty(x)));
            tasks.Children.Add(new TaskRow(task,metadata,done=>
            {
                var next=state with {Tasks=state.Tasks.Select(t=>t.Id==task.Id ? t with {CompletedAt=done?(t.CompletedAt??DateTimeOffset.Now):null}:t).ToList()};
                if(!Commit(next))Render();
                Dispatcher.BeginInvoke(new Action(()=>tasks.Children.OfType<TaskRow>().FirstOrDefault(row=>row.TaskId==task.Id)?.FocusCompletion()),DispatcherPriority.Input);
            },()=>EditTask(task),()=>
            {
                var old=state;
                if(Commit(state with {Tasks=state.Tasks.Where(t=>t.Id!=task.Id).ToList()})) {undo=old;Render();notice.Text="삭제했습니다. 아래에서 되돌릴 수 있습니다.";}
            }));
        }
        var remote=notion.Cache.Enabled&&selected==null?notion.Cache.Tasks.Where(t=>filter=="전체"||(filter=="완료")==t.Fields.Done).OrderBy(t=>t.Fields.Due??DateOnly.MaxValue).ToList():new System.Collections.Generic.List<NotionTask>();
        if(remote.Count>0){tasks.Children.Add(new TextBlock {Text="notion / 워크스페이스",FontSize=11,Foreground=Brush("#939DA5"),Margin=new Thickness(0,12,0,5)});foreach(var item in remote){tasks.Children.Add(new TaskRow(item.AsTask(),"노션"+(item.Fields.Due is {} due?$" · {due:MM/dd}":""),async done=>await notion.Update(item,item.AsTask() with {CompletedAt=done?DateTimeOffset.Now:null}),()=>EditNotionTask(item),async()=>await notion.Delete(item)));}}
        taskHeading.Text=$"{(state.Settings.TasksCollapsed?"▸":"▾")} tasks  {state.Tasks.Count(t=>selected==null||t.ProjectId==selected)+(notion.Cache.Enabled&&selected==null?notion.Cache.Tasks.Count:0)}";
        if(visible.Count==0&&remote.Count==0) tasks.Children.Add(new TextBlock {Text="표시할 할 일이 없습니다.\ntasks 옆 + 버튼으로 추가하세요.",TextWrapping=TextWrapping.Wrap,Foreground=Brush("#939DA5"),Margin=new Thickness(3,18,3,18)});
    }
    private void RenderKlas()
    {
        var snapshot=klas.Snapshot;
        int count=snapshot?.Courses.Sum(c=>c.Lectures+c.Assignments)??0;
        klasHeading.Text=$"{(state.Settings.KlasCollapsed?"▸":"▾")} klas  {(snapshot==null?"—":count.ToString())}";
        klasItems.Visibility=state.Settings.KlasCollapsed?Visibility.Collapsed:Visibility.Visible;
        klasItems.Children.Clear();
        void Label(string text,string color="#939DA5",int size=11) => klasItems.Children.Add(new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,Foreground=Brush(color),FontSize=size,Margin=new Thickness(0,4,0,4)});
        if(snapshot==null||klas.Busy||klas.LastRefreshFailed)Label(klas.Status);
        if(snapshot==null)return;
        Label($"{string.Join(" · ",snapshot.Courses.Select(c=>c.Semester).Distinct())} · 확인 {snapshot.CheckedAt.LocalDateTime:MM/dd HH:mm}","#879198",10);
        if(snapshot.Courses.Count==0){Label("등록된 수강 과목이 없습니다. KLAS에서 학기를 확인하세요.");return;}
        if(count==0){Label(klas.LastRefreshFailed?"이전 확인에서는 남은 항목이 없었습니다. 최신 상태는 확인하지 못했습니다.":"남아있는 항목이 없습니다. 깔끔하네요!","#8D9C92");return;}
        foreach(var course in snapshot.Courses.Where(c=>c.Lectures+c.Assignments>0).OrderBy(c=>new[]{c.LectureDue??DateTimeOffset.MaxValue,c.AssignmentDue??DateTimeOffset.MaxValue}.Min()))
        {
            Label(course.Name,"#D1D9DE",14);
            if(course.Lectures>0)Label($"  강의 {course.Lectures}개 · {Deadline(course.LectureDue!.Value)}");
            if(course.Assignments>0)Label($"  과제 {course.Assignments}개 · {Deadline(course.AssignmentDue!.Value)}");
        }
        Label("마감 전 미완료 항목 · 추가 제출 기한 포함","#879198",10);
    }
    private static string Deadline(DateTimeOffset due)
    {
        var left=due-DateTimeOffset.Now;
        string label=left.TotalSeconds<0?"기한 경과 · ↻ 확인":left.TotalHours<1?"곧 마감":left.TotalDays<1?$"{(int)left.TotalHours}시간 후 마감":$"{(int)left.TotalDays}일 후 마감";
        return $"{due.LocalDateTime:MM/dd HH:mm} · {label}";
    }
    private async void EditTask(TaskItem? task) {bool createRemote=task==null&&notion.Cache.Enabled&&selected==null;var result=Dialogs.EditTask(task,createRemote?Workspace.Empty():state,selected); if(result!=null){if(createRemote)await notion.Create(result);else Commit(state with {Tasks=task==null ? state.Tasks.Append(result).ToList():state.Tasks.Select(t=>t.Id==result.Id?result:t).ToList()});} }
    private async void EditNotionTask(NotionTask task){var edited=Dialogs.EditTask(task.AsTask(),Workspace.Empty(),null);if(edited!=null)await notion.Update(task,edited);}
    private async void RefreshProjects()
    {
        if(refreshing)return;
        refreshing=true;if(refreshButton!=null)refreshButton.IsEnabled=false;notice.Text="연결된 작업 목록을 읽는 중…";
        try
        {
            await notion.Refresh();
            await klas.Refresh();
            var sources=state.Projects.Where(p=>p.ProgressMode=="폴더 자동").ToArray();
            if(sources.Length==0){notice.Text=klas.Snapshot!=null?klas.Status:notion.Cache.Enabled?notion.Status:"연결된 항목이 없습니다. 프로젝트 폴더 또는 KLAS를 연결하세요.";return;}
            // 순차 읽기로 메모리 사용을 제한한다. 갱신 중 편집된 다른 정보는 덮어쓰지 않는다.
            var results=await System.Threading.Tasks.Task.Run(()=>sources.Select(p=>(Project:p,Result:FolderProgress.Read(p))).ToArray());
            var merged=state.Projects.Select(p=>{var match=results.FirstOrDefault(r=>r.Project.Id==p.Id&&r.Project.ProgressMode==p.ProgressMode&&r.Project.FolderPath==p.FolderPath&&r.Project.PlanFile==p.PlanFile);return match.Project==null?p:p with {Snapshot=match.Result};}).ToList();
            if(Commit(state with {Projects=merged}))notice.Text=$"{DateTime.Now:HH:mm} 갱신 완료 · {results.Count(r=>r.Result.Error==null)}/{results.Length}개 계산 가능";
        }
        catch(Exception ex){notice.Text="새로고침 실패 · 기존 결과를 유지합니다.";App.Trace("refresh failed: "+ex.GetType().Name);}
        finally{refreshing=false;if(refreshButton!=null)refreshButton.IsEnabled=true;}
    }
    private void EditProject(Project? project)
    {
        if(project!=null)
        {
            var menu=new ContextMenu(); var edit=new MenuItem {Header="프로젝트 수정"}; edit.Click+=(_,_)=>SaveProject(project); menu.Items.Add(edit); var delete=new MenuItem {Header="프로젝트 삭제"}; delete.Click+=(_,_)=>DeleteProject(project); menu.Items.Add(delete); menu.IsOpen=true;
        }
        else SaveProject(null);
    }
    private void SaveProject(Project? project)
    {
        var result=Dialogs.EditProject(project); if(result==null)return;
        if(result.Status=="완료" && project?.Status!="완료" && state.Tasks.Any(t=>t.ProjectId==result.Id && t.CompletedAt==null) && MessageBox.Show("미완료 할 일이 남아 있습니다. 프로젝트 상태만 완료로 변경할까요?","완료 확인",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        Commit(state with {Projects=project==null?state.Projects.Append(result).ToList():state.Projects.Select(p=>p.Id==result.Id?result:p).ToList()});
    }
    private void DeleteProject(Project project)
    {
        var w=Dialogs.Form("프로젝트 삭제",out var panel); Dialogs.Label(panel,$"‘{project.Name}’의 연결된 할 일 처리");
        void Delete(bool remove) {var old=state;if(Commit(Rules.DeleteProject(state,project.Id,remove))) {undo=old;selected=null;Render();notice.Text="프로젝트를 삭제했습니다. 실행 취소할 수 있습니다.";w.Close();}}
        panel.Children.Add(Dialogs.Button("할 일을 독립 항목으로 유지",()=>Delete(false))); panel.Children.Add(Dialogs.Button("연결된 할 일도 삭제",()=>Delete(true))); panel.Children.Add(Dialogs.Button("취소",w.Close)); w.ShowDialog();
    }
    private async void Undo()
    {
        if(notion.CanUndo){await notion.Undo();return;}
        if(undo==null)return;
        // 삭제 이후에 추가·수정한 데이터는 유지하고 삭제된 항목만 복원한다.
        var restoredProjects=state.Projects.Concat(undo.Projects.Where(p=>state.Projects.All(n=>n.Id!=p.Id))).ToList();
        var restoredIds=restoredProjects.Where(p=>state.Projects.All(n=>n.Id!=p.Id)).Select(p=>p.Id).ToHashSet();
        var restoredTasks=state.Tasks.Select(t=> {var old=undo.Tasks.FirstOrDefault(o=>o.Id==t.Id);return old?.ProjectId is {} id && restoredIds.Contains(id) && t.ProjectId==null ? t with {ProjectId=id}:t;}).Concat(undo.Tasks.Where(t=>state.Tasks.All(n=>n.Id!=t.Id))).ToList();
        if(Commit(state with {Projects=restoredProjects,Tasks=restoredTasks})) {undo=null;Render();}
    }
    private void Collapse() {SaveGeometry();bool collapsed=!state.Settings.Collapsed;if(Commit(state with {Settings=state.Settings with {Collapsed=collapsed}})) {MinHeight=collapsed?125:400;Height=collapsed?125:state.Settings.Height;}}
    private void SaveGeometry() {if(!ready)return;var pos=host.Position();Commit(state with {Settings=state.Settings with {X=pos.X,Y=pos.Y,Width=Width,Height=state.Settings.Collapsed?state.Settings.Height:Height}},false);}
    private void ApplyMode()
    {
        if(recoveryMode || state.Settings.AlwaysOnTop) {if(host.Attached)host.Detach();Topmost=true;} else {Topmost=false;if(!host.Attached && !host.Attach())notice.Text="바탕화면 연결 실패 · 일반 창으로 실행 중 (트레이에서 재표시 가능)";}
        Background=Brush("#000000");
    }
    public void ShowWidget() {App.Trace("reveal start");Show();ApplyMode();host.Clamp();host.Reveal();App.Trace("reveal complete "+IsVisible);}
    private void Quit() {SaveGeometry();quitting=true;timer.Stop();geometryTimer.Stop();klas.Close();tray.Dispose();Close();Application.Current.Shutdown();}
    private void Settings()
    {
        var w=Dialogs.Form("위젯 설정",out var panel);
        panel.Children.Add(Dialogs.Button("노션 양방향 연결",()=>{w.Close();notion.Settings();}));
        panel.Children.Add(Dialogs.Button("KLAS 연결 · 수강 과목 현황",()=>{w.Close();klas.Connect();}));
        var top=new CheckBox {Content="항상 위에 표시 (바탕화면 고정 해제)",IsChecked=state.Settings.AlwaysOnTop}; panel.Children.Add(top);
        var startup=new CheckBox {Content="Windows 로그인 시 자동 시작",IsChecked=state.Settings.AutoStart}; panel.Children.Add(startup);
        Dialogs.Label(panel,"배경 불투명도");var opacity=new Slider {Minimum=.4,Maximum=1,Value=state.Settings.Opacity,TickFrequency=.05,IsSnapToTickEnabled=true};panel.Children.Add(opacity);
        panel.Children.Add(Dialogs.Button("설정 저장",()=>{try {StartupRegistration.Set(startup.IsChecked==true);if(Commit(state with {Settings=state.Settings with {AlwaysOnTop=top.IsChecked==true,AutoStart=startup.IsChecked==true,Opacity=opacity.Value}})){ApplyMode();w.Close();}}catch(Exception ex){MessageBox.Show(ex.Message,"설정 오류");}}));
        panel.Children.Add(Dialogs.Button("JSON 내보내기",()=>{var d=new Microsoft.Win32.SaveFileDialog {Filter="JSON|*.json",FileName="workspace-export.json"};if(d.ShowDialog()==true)try{File.WriteAllText(d.FileName,JsonSerializer.Serialize(state,new JsonSerializerOptions {WriteIndented=true}));}catch(Exception ex){MessageBox.Show(ex.Message,"내보내기 오류");}}));
        panel.Children.Add(Dialogs.Button("JSON 가져오기",()=>{var d=new Microsoft.Win32.OpenFileDialog {Filter="JSON|*.json"};if(d.ShowDialog()!=true)return;try {if(new FileInfo(d.FileName).Length>20*1024*1024)throw new FormatException("20MB 이하 파일만 가져올 수 있습니다.");var imported=Rules.Parse(File.ReadAllText(d.FileName));if(MessageBox.Show("현재 할 일과 프로젝트를 가져온 데이터로 덮어쓸까요? 창 설정은 유지됩니다.","가져오기 확인",MessageBoxButton.YesNo)==MessageBoxResult.Yes && Commit(imported with {Settings=state.Settings})) {undo=null;selected=null;Render();w.Close();}}catch(Exception ex){MessageBox.Show(ex.Message,"가져오기 오류");}}));
        Dialogs.Label(panel,"저장 위치: "+repository.FilePath);w.ShowDialog();
    }
}
