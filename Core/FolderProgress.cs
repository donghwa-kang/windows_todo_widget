using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace TerminalWidget.Core;
public static class FolderProgress
{
    public static readonly string[] Files={"PROGRESS.md","TASKS.md","TODO.md","PLAN.md"};
    private const int MaxBytes=1024*1024;
    public static (int Done,int Total) Count(string markdown)
    {
        var items=new List<(int Indent,bool Done)>();
        char fence='\0';int fenceLength=0;
        markdown=Regex.Replace(markdown,@"<!--[\s\S]*?-->","",RegexOptions.None,TimeSpan.FromSeconds(1));
        foreach(string line in markdown.TrimStart('\uFEFF').Split('\n'))
        {
            string trim=line.TrimStart();
            if(trim.StartsWith("```")||trim.StartsWith("~~~")) {int n=trim.TakeWhile(c=>c==trim[0]).Count();if(fence=='\0'){fence=trim[0];fenceLength=n;}else if(fence==trim[0]&&n>=fenceLength){fence='\0';}continue;}
            if(fence!='\0')continue;
            var match=Regex.Match(line,@"^(\s*)[-*+]\s+\[([ xX])\]\s+\S",RegexOptions.None,TimeSpan.FromSeconds(1));
            if(match.Success)items.Add((match.Groups[1].Value.Replace("\t","    ").Length,match.Groups[2].Value!=" "));
        }
        int done=0,total=0;
        for(int i=0;i<items.Count;i++) {if(i+1<items.Count&&items[i+1].Indent>items[i].Indent)continue;total++;if(items[i].Done)done++;}
        return(done,total);
    }
    public static ProgressSnapshot Read(Project project)
    {
        var now=DateTimeOffset.Now;
        ProgressSnapshot Fail(string message)=>new(project.Snapshot?.Percent,project.Snapshot?.Done??0,project.Snapshot?.Total??0,project.Snapshot?.Source,message,now,project.Snapshot?.SuccessAt);
        try
        {
            if(string.IsNullOrWhiteSpace(project.FolderPath)||!Directory.Exists(project.FolderPath))return Fail("연결 불가 · 폴더 경로 확인 필요");
            if((File.GetAttributes(project.FolderPath)&FileAttributes.ReparsePoint)!=0)return Fail("연결 불가 · 바로가기 대신 실제 폴더를 선택하세요");
            string? file=project.PlanFile;
            if(file==null){var found=Files.Where(f=>File.Exists(Path.Combine(project.FolderPath,f))).ToArray();if(found.Length==0)return Fail("자동 계산 불가 · 작업 기준 없음");if(found.Length>1)return Fail("자동 계산 불가 · 기준 파일을 하나 선택하세요");file=found[0];}
            if(!Files.Contains(file))return Fail("자동 계산 불가 · 지원하지 않는 기준 파일");
            string path=Path.Combine(project.FolderPath,file);
            if(!File.Exists(path))return Fail("자동 계산 불가 · 기준 파일 없음");
            if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)return Fail("연결 불가 · 기준 파일은 실제 파일이어야 합니다");
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            if(stream.Length>MaxBytes)return Fail("자동 계산 불가 · 기준 파일은 1MB 이하여야 합니다");
            using var bytes=new MemoryStream();var buffer=new byte[8192];int read;
            while((read=stream.Read(buffer,0,buffer.Length))>0){if(bytes.Length+read>MaxBytes)return Fail("자동 계산 불가 · 기준 파일은 1MB 이하여야 합니다");bytes.Write(buffer,0,read);}
            var count=Count(new UTF8Encoding(false,true).GetString(bytes.ToArray()));
            if(count.Total==0)return Fail("자동 계산 불가 · 체크 가능한 작업 없음");
            return new((int)Math.Round(100d*count.Done/count.Total),count.Done,count.Total,file,null,now,now);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException or RegexMatchTimeoutException){return Fail("연결 불가 · 파일 접근 또는 형식 확인 필요");}
    }
}
