using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TerminalWidget.Core;
namespace TerminalWidget.Platform;
public sealed class NotionHttpException : Exception
{
    public int Status {get;}
    public NotionHttpException(int status):base(status==401?"노션 인증 토큰을 확인하세요.":status==403||status==404?"원본 데이터베이스의 연결 권한을 확인하세요.":status==429?"요청 제한에 도달했습니다. 잠시 후 다시 시도하세요.":$"노션 요청 실패 ({status})"){Status=status;}
}
public sealed class NotionClient : IDisposable
{
    private readonly HttpClient http;
    private readonly string sourceId;
    public NotionClient(string token,string sourceId,HttpMessageHandler? handler=null)
    {
        if(!Guid.TryParse(sourceId,out var source)||source==Guid.Empty)throw new ArgumentException("올바른 노션 데이터 소스 ID를 입력하세요.",nameof(sourceId));
        this.sourceId=source.ToString();
        http=handler==null?new HttpClient(new HttpClientHandler {AllowAutoRedirect=false}):new HttpClient(handler);
        http.BaseAddress=new Uri("https://api.notion.com/v1/");http.Timeout=TimeSpan.FromSeconds(15);
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);http.DefaultRequestHeaders.Add("Notion-Version","2025-09-03");
    }
    private async Task<JsonDocument> Request(HttpMethod method,string path,object? body=null)
    {
        using var request=new HttpRequestMessage(method,path);if(body!=null)request.Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json");
        using var response=await http.SendAsync(request);
        if(!response.IsSuccessStatusCode)throw new NotionHttpException((int)response.StatusCode);
        var content=await response.Content.ReadAsStringAsync();if(content.Length>8*1024*1024)throw new InvalidOperationException("노션 응답이 너무 큽니다.");return JsonDocument.Parse(content);
    }
    public async Task Validate()
    {
        using var json=await Request(HttpMethod.Get,$"data_sources/{sourceId}");var p=json.RootElement.GetProperty("properties");
        foreach(var pair in new[]{("이름","title"),("완료","checkbox"),("우선순위","select"),("일정","date"),("유형","select")})if(!p.TryGetProperty(pair.Item1,out var v)||v.GetProperty("type").GetString()!=pair.Item2)throw new InvalidOperationException("노션 속성 구성이 달라 연결할 수 없습니다: "+pair.Item1);
    }
    public async Task<List<NotionTask>> List(IReadOnlyList<NotionTask> previous)
    {
        var output=new List<NotionTask>();string? cursor=null;
        do
        {
            var query=new Dictionary<string,object?>{{"page_size",100}};if(cursor!=null)query["start_cursor"]=cursor;
            using var doc=await Request(HttpMethod.Post,$"data_sources/{sourceId}/query",query);
            foreach(var page in doc.RootElement.GetProperty("results").EnumerateArray())
            {
                var id=Guid.Parse(page.GetProperty("id").GetString()!);if(page.TryGetProperty("in_trash",out var trash)&&trash.GetBoolean())continue;
                output.Add(NotionMapping.Parse(page,previous.FirstOrDefault(p=>p.Id==id)));
                if(output.Count>5000)throw new InvalidOperationException("노션 항목이 5,000개를 넘었습니다. 기존 목록을 유지합니다.");
            }
            cursor=doc.RootElement.GetProperty("has_more").GetBoolean()?doc.RootElement.GetProperty("next_cursor").GetString():null;
            if(cursor!=null)await Task.Delay(350);
        }while(cursor!=null);
        return output.DistinctBy(t=>t.Id).ToList();
    }
    public async Task<NotionTask> Get(Guid id)
    {
        using var doc=await Request(HttpMethod.Get,$"pages/{id}");var page=doc.RootElement;
        if(!page.GetProperty("parent").TryGetProperty("data_source_id",out var source)||Guid.Parse(source.GetString()!)!=Guid.Parse(sourceId))throw new InvalidOperationException("연결된 데이터베이스의 항목이 아닙니다.");
        if(page.TryGetProperty("in_trash",out var trash)&&trash.GetBoolean())throw new InvalidOperationException("노션에서 이미 삭제된 항목입니다. 새로고침하세요.");
        return NotionMapping.Parse(page);
    }
    public async Task<NotionTask> Create(NotionFields fields)
    {
        using var doc=await Request(HttpMethod.Post,"pages",new {parent=new {type="data_source_id",data_source_id=sourceId},properties=NotionMapping.Properties(fields)});
        return NotionMapping.Parse(doc.RootElement) with {KnownCompletedAt=fields.Done?DateTimeOffset.Now:null};
    }
    public async Task<NotionTask> Update(NotionTask old,NotionFields next)
    {
        var latest=await Get(old.Id);if(latest.Fields!=old.Fields||latest.LastEdited!=old.LastEdited)throw new InvalidOperationException("노션에서 먼저 수정되었습니다. 새로고침 후 다시 변경하세요.");
        using var doc=await Request(HttpMethod.Patch,$"pages/{old.Id}",new {properties=NotionMapping.Properties(next,old.Fields)});
        return NotionMapping.Parse(doc.RootElement) with {KnownCompletedAt=next.Done?(old.KnownCompletedAt??(!old.Fields.Done?DateTimeOffset.Now:null)):null};
    }
    public async Task Trash(NotionTask old)
    {
        var latest=await Get(old.Id);if(latest.Fields!=old.Fields||latest.LastEdited!=old.LastEdited)throw new InvalidOperationException("노션 항목이 수정되어 삭제하지 않았습니다. 새로고침하세요.");
        using var doc=await Request(HttpMethod.Patch,$"pages/{old.Id}",new {in_trash=true});
    }
    public async Task Restore(Guid id){using var doc=await Request(HttpMethod.Patch,$"pages/{id}",new {in_trash=false});}
    public void Dispose()=>http.Dispose();
}
