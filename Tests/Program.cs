using TerminalWidget.Core;
using System.Text.Json;

int count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
var state=Workspace.Empty(); var id=Guid.NewGuid();state.Projects.Add(new Project(id,"검증","","진행 중"));
Check(Rules.Progress(state,id)==(0,0,0),"할 일 없는 진행률");
var now=DateTimeOffset.Now;var task=new TaskItem(Guid.NewGuid(),"테스트",id,"보통",DateOnly.FromDateTime(now.LocalDateTime),null);
state.Tasks.Add(task);state.Tasks.Add(task with {Id=Guid.NewGuid(),CompletedAt=now});
Check(Rules.Progress(state,id)==(1,2,50),"진행률 50%");
Check(Rules.Toggle(Rules.Toggle(task,now),now).CompletedAt==null,"완료 취소 시 완료 시각 초기화");
var today=DateOnly.FromDateTime(now.LocalDateTime);
Check(Rules.CompletedToday(task with {CompletedAt=now},today),"로컬 오늘 완료");
var midnight=new DateTimeOffset(DateTime.Today);
Check(!Rules.CompletedToday(task with {CompletedAt=midnight.AddTicks(-1)},today),"자정 직전은 어제");
Check(Rules.CompletedToday(task with {CompletedAt=midnight},today),"자정은 오늘");
Check(Rules.DeleteProject(state,id,false).Tasks.All(t=>t.ProjectId==null),"프로젝트 삭제 시 독립 할 일 유지");
Check(Rules.DeleteProject(state,id,true).Tasks.Count==0,"프로젝트와 할 일 함께 삭제");
var folder=Path.Combine(Path.GetTempPath(),"TerminalWidgetTests-"+Guid.NewGuid());
try {
 var repository=new WorkspaceRepository(folder);repository.Save(state);
 Check(repository.Load().Tasks.Count==2,"저장 후 복원");
 repository.Save(state with {Tasks=new()});Check(File.Exists(repository.FilePath+".bak"),"원자적 교체 백업 생성");
 File.WriteAllText(repository.FilePath,"broken");var restored=repository.Load();
 Check(restored.Tasks.Count==2 && repository.RecoveryMessage!=null,"손상 데이터 백업 복구");
 Check(Directory.GetFiles(folder,"*.corrupt-*").Length==1,"손상 원본 보존");
 bool invalid=false;try {Rules.Parse(JsonSerializer.Serialize(state with {SchemaVersion=999}));}catch(FormatException){invalid=true;}
 Check(invalid,"미지원 스키마 거부");
 invalid=false;try {Rules.Parse(JsonSerializer.Serialize(state with {Tasks=new(){task with {ProjectId=Guid.NewGuid()}}}));}catch(FormatException){invalid=true;}
 Check(invalid,"존재하지 않는 프로젝트 참조 거부");
} finally {Directory.Delete(folder,true);}
Check(FolderProgress.Count("- [x] A\n- [ ] B")== (1,2),"마크다운 체크리스트 계산");
Check(FolderProgress.Count("- [ ] 큰 단계\n  - [x] 구현\n  - [ ] 검증\n- [x] 문서")== (2,3),"큰 단계 중복 집계 제외");
Check(FolderProgress.Count("```md\n- [x] 예시\n```\n<!-- - [x] 주석 -->\n- [ ] 실제")== (0,1),"코드 예시와 주석 제외");
var linkFolder=Path.Combine(Path.GetTempPath(),"WidgetFolderTest-"+Guid.NewGuid());Directory.CreateDirectory(linkFolder);
try
{
 var linked=new Project(Guid.NewGuid(),"연결","","진행 중","폴더 자동",0,linkFolder);
 Check(FolderProgress.Read(linked).Error!=null && FolderProgress.Read(linked).Percent==null,"기준 없는 폴더는 퍼센트 추측 금지");
 File.WriteAllText(Path.Combine(linkFolder,"PROGRESS.md"),"- [x] A\n- [ ] B");
 var firstRead=FolderProgress.Read(linked);Check(firstRead.Percent==50&&firstRead.Total==2,"실제 폴더 최초 계산");
 linked=linked with {Snapshot=firstRead};
 File.WriteAllText(Path.Combine(linkFolder,"PROGRESS.md"),"- [x] A\n- [x] B");
 Check(linked.Snapshot.Percent==50,"파일 변경만으로 캐시 자동 갱신하지 않음");
 Check(FolderProgress.Read(linked).Percent==100,"명시적 재읽기에서 갱신");
 File.WriteAllText(Path.Combine(linkFolder,"TODO.md"),"- [ ] C");
 var ambiguous=FolderProgress.Read(linked);Check(ambiguous.Error!=null&&ambiguous.Percent==50,"기준 중복 오류 시 이전 값 보존");
 Check(FolderProgress.Read(linked with {PlanFile="PROGRESS.md"}).Percent==100,"명시적으로 기준 파일 선택");
 File.WriteAllText(Path.Combine(linkFolder,"PROGRESS.md"),new string('a',1024*1024+1));
 Check(FolderProgress.Read(linked with {PlanFile="PROGRESS.md"}).Error!=null,"파일 크기 제한");
 Check(FolderProgress.Read(linked with {FolderPath=Path.Combine(linkFolder,"missing")}).Percent==50,"폴더 접근 실패 시 이전 값 보존");
 var manual=new Project(Guid.NewGuid(),"수동","","진행 중","수동",35);
 var saved=Workspace.Empty() with {Projects=new(){manual,linked}};
 var parsed=Rules.Parse(JsonSerializer.Serialize(saved));Check(parsed.Projects[0].ManualPercent==35&&parsed.Projects[1].Snapshot?.Percent==50,"수동 값과 자동 캐시 저장 복원");
 bool invalid=false;try{Rules.Parse(JsonSerializer.Serialize(saved with {Projects=new(){manual with {ManualPercent=101}}}));}catch(FormatException){invalid=true;}Check(invalid,"수동 범위 검증");
 var legacy=JsonSerializer.SerializeToNode(Workspace.Empty() with {Projects=new(){manual}})!;
 var oldProject=legacy["Projects"]![0]!.AsObject();foreach(var field in new[]{"ProgressMode","ManualPercent","FolderPath","PlanFile","Snapshot"})oldProject.Remove(field);
 Check(Rules.Parse(legacy.ToJsonString()).Projects[0].ProgressMode=="할 일 기준","이전 저장 파일 호환");
}
finally {Directory.Delete(linkFolder,true);}
Console.WriteLine($"{count} tests passed");
await NotionTests.Run(Check);
KlasTests.Run(Check);
Console.WriteLine($"Total: {count} tests passed");
