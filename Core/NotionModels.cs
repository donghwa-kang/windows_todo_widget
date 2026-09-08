using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
namespace TerminalWidget.Core;
public sealed record NotionFields(string Title,bool Done,string Priority,DateOnly? Due);
public sealed record NotionTask(Guid Id,NotionFields Fields,DateTimeOffset? KnownCompletedAt=null,DateTimeOffset? LastEdited=null)
{
    public TaskItem AsTask()=>new(Id,Fields.Title,null,Fields.Priority,Fields.Due,Fields.Done?(KnownCompletedAt??DateTimeOffset.MinValue):null);
}
public sealed record NotionCache(bool Enabled,List<NotionTask> Tasks,DateTimeOffset? LastSync=null,NotionFields? PendingCreate=null,string? SourceId=null);
public static class NotionMapping
{
    public static NotionFields FromTask(TaskItem t)=>new(t.Title,t.CompletedAt!=null,t.Priority,t.Due);
    public static NotionTask Parse(JsonElement page,NotionTask? old=null)
    {
        var props=page.GetProperty("properties");var title=string.Concat(props.GetProperty("이름").GetProperty("title").EnumerateArray().Select(x=>x.GetProperty("plain_text").GetString()));
        if(string.IsNullOrWhiteSpace(title))title="(제목 없음)";
        var priority=props.GetProperty("우선순위").GetProperty("select");var name=priority.ValueKind==JsonValueKind.Null?null:priority.GetProperty("name").GetString();
        var date=props.GetProperty("일정").GetProperty("date");DateOnly? due=null;
        if(date.ValueKind!=JsonValueKind.Null){string start=date.GetProperty("start").GetString()!;due=start.Length==10?DateOnly.Parse(start):DateOnly.FromDateTime(DateTimeOffset.Parse(start).LocalDateTime);}
        bool done=props.GetProperty("완료").GetProperty("checkbox").GetBoolean();
        return new(Guid.Parse(page.GetProperty("id").GetString()!),new(title,done,name=="1 · 높음"?"높음":name=="3 · 여유"?"낮음":"보통",due),done&&old?.Fields.Done==true?old.KnownCompletedAt:null,page.TryGetProperty("last_edited_time",out var edited)?DateTimeOffset.Parse(edited.GetString()!):null);
    }
    public static Dictionary<string,object?> Properties(NotionFields value,NotionFields? baseline=null)
    {
        var p=new Dictionary<string,object?>();
        if(baseline==null||value.Title!=baseline.Title)p["이름"]=new {title=new[]{new {text=new {content=value.Title}}}};
        if(baseline==null||value.Done!=baseline.Done)p["완료"]=new {checkbox=value.Done};
        if(baseline==null||value.Priority!=baseline.Priority)p["우선순위"]=new {select=new {name=value.Priority=="높음"?"1 · 높음":value.Priority=="낮음"?"3 · 여유":"2 · 보통"}};
        if(baseline==null||value.Due!=baseline.Due)p["일정"]=new {date=value.Due is {} due?new {start=due.ToString("yyyy-MM-dd")}:null};
        if(baseline==null)p["유형"]=new {select=new {name="할 일"}};
        return p;
    }
}
