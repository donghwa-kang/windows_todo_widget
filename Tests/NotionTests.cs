using System.Net;
using System.Text;
using System.Text.Json;
using TerminalWidget.Core;
using TerminalWidget.Platform;
public static class NotionTests
{
    private sealed class Fake(Func<HttpRequestMessage,Task<HttpResponseMessage>> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(request);}
    private static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json")};
    public static async Task Run(Action<bool,string> check)
    {
        const string sourceId="11111111-1111-4111-8111-111111111111"; var id=Guid.NewGuid();
        foreach(var invalid in new[]{"",Guid.Empty.ToString(),"https://example.com/data"})
        {bool rejected=false;try{using var invalidApi=new NotionClient("fake",invalid);}catch(ArgumentException){rejected=true;}check(rejected,"잘못된 데이터 소스 ID 차단");}
        var cache=new NotionCache(true,new(),SourceId:sourceId);
        check(JsonSerializer.Deserialize<NotionCache>(JsonSerializer.Serialize(cache))?.SourceId==sourceId,"사용자별 데이터 소스 설정 저장·복원");
        using(var api=new NotionClient("fake",sourceId,new Fake(req=>Task.FromResult(Json(new {parent=new {data_source_id="22222222-2222-4222-8222-222222222222"}})))))
        {bool rejected=false;try{await api.Get(id);}catch(InvalidOperationException){rejected=true;}check(rejected,"다른 데이터 소스의 항목 연결 차단");}
        object Page(bool done=false,string title="노션 테스트",string edited="2026-09-08T00:00:00Z")=>new {id=id.ToString(),last_edited_time=edited,in_trash=false,parent=new {data_source_id=sourceId},properties=new Dictionary<string,object?>{{"이름",new {title=new[]{new {plain_text=title}}}},{"완료",new {checkbox=done}},{"우선순위",new {select=new {name="3 · 여유"}}},{"일정",new {date=new {start="2026-09-08",end="2026-09-09"}}}}};
        using var page=JsonDocument.Parse(JsonSerializer.Serialize(Page()));var task=NotionMapping.Parse(page.RootElement);
        check(task.Fields.Priority=="낮음"&&task.Fields.Due==new DateOnly(2026,9,8),"노션 필드 변환");
        var patch=NotionMapping.Properties(task.Fields with {Done=true},task.Fields);
        check(patch.Count==1&&patch.ContainsKey("완료"),"완료 체크 시 일정 범위와 다른 속성 보존");
        int requests=0;
        using(var api=new NotionClient("fake",sourceId,new Fake(async req=>{requests++;string body=await req.Content!.ReadAsStringAsync();check(req.Headers.Contains("Notion-Version")&&req.RequestUri!.Host=="api.notion.com","인증 요청 대상과 API 버전");return Json(new {results=new[]{Page()},has_more=requests==1,next_cursor=requests==1?"second":null});})))
        {var rows=await api.List(Array.Empty<NotionTask>());check(requests==2&&rows.Count==1,"페이지네이션 끝까지 읽고 중복 ID 제거");}
        requests=0;
        using(var api=new NotionClient("fake",sourceId,new Fake(req=>{requests++;return Task.FromResult(Json(Page(title:"원격 변경")));})))
        {bool conflict=false;try{await api.Update(task,task.Fields with {Done=true});}catch(InvalidOperationException){conflict=true;}check(conflict&&requests==1,"원격 수정 충돌 시 PATCH 금지");}
        requests=0;
        using(var api=new NotionClient("fake",sourceId,new Fake(req=>{requests++;return Task.FromResult(Json(Page(edited:"2026-09-08T01:00:00Z")));})))
        {bool conflict=false;try{await api.Trash(task);}catch(InvalidOperationException){conflict=true;}check(conflict&&requests==1,"속성 외 편집 시 삭제 충돌 감지");}
        using(var api=new NotionClient("fake",sourceId,new Fake(async req=>{if(req.Method==HttpMethod.Patch){using var b=JsonDocument.Parse(await req.Content!.ReadAsStringAsync());check(b.RootElement.GetProperty("in_trash").GetBoolean(),"삭제는 휴지통 이동");}return Json(Page());})))await api.Trash(task);
        using(var api=new NotionClient("fake",sourceId,new Fake(async req=>{using var b=JsonDocument.Parse(await req.Content!.ReadAsStringAsync());check(b.RootElement.GetProperty("parent").GetProperty("data_source_id").GetString()==sourceId,"새 할 일은 지정된 원본에 생성");return Json(Page());})))await api.Create(task.Fields);
        using(var api=new NotionClient("fake",sourceId,new Fake(req=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)))))
        {bool limited=false;try{await api.List(Array.Empty<NotionTask>());}catch(NotionHttpException e){limited=e.Status==429;}check(limited,"요청 제한을 빈 목록으로 오인하지 않음");}
        var doneTask=task with {Fields=task.Fields with {Done=true}};
        check(!Rules.CompletedToday(doneTask.AsTask(),DateOnly.FromDateTime(DateTime.Now)),"외부 완료 시각을 오늘로 추측하지 않음");
    }
}
