using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TerminalWidget.Core;
public sealed record TaskItem(Guid Id, string Title, Guid? ProjectId, string Priority, DateOnly? Due, DateTimeOffset? CompletedAt);
public sealed record Project(Guid Id, string Name, string Description, string Status, string ProgressMode = "할 일 기준", int ManualPercent = 0, string? FolderPath = null, string? PlanFile = null, ProgressSnapshot? Snapshot = null);
public sealed record ProgressSnapshot(int? Percent, int Done, int Total, string? Source, string? Error, DateTimeOffset CheckedAt, DateTimeOffset? SuccessAt);
public sealed record Preferences(double X = 40, double Y = 80, double Width = 380, double Height = 560, bool Collapsed = false, bool AlwaysOnTop = false, double Opacity = .96, bool AutoStart = true, bool TasksCollapsed = false, bool ProjectsCollapsed = false, bool KlasCollapsed = false);
public sealed record Workspace(int SchemaVersion, List<TaskItem> Tasks, List<Project> Projects, Preferences Settings)
{
    public static Workspace Empty() => new(1, new(), new(), new());
}
public static class Rules
{
    public static readonly string[] Priorities = { "높음", "보통", "낮음" };
    public static readonly string[] Statuses = { "예정", "진행 중", "보류", "완료" };
    public static readonly string[] ProgressModes = { "폴더 자동", "수동", "할 일 기준" };
    public static (int Done, int Total, int Percent) Progress(Workspace state, Guid id)
    {
        var tasks = state.Tasks.Where(t => t.ProjectId == id).ToList();
        int done = tasks.Count(t => t.CompletedAt != null);
        return (done, tasks.Count, tasks.Count == 0 ? 0 : (int)Math.Round(100d * done / tasks.Count));
    }
    public static TaskItem Toggle(TaskItem task, DateTimeOffset now) => task with { CompletedAt = task.CompletedAt == null ? now : null };
    public static bool CompletedToday(TaskItem task, DateOnly today) => task.CompletedAt is {} time && DateOnly.FromDateTime(time.LocalDateTime) == today;
    public static Workspace DeleteProject(Workspace state, Guid id, bool deleteTasks) => state with
    {
        Projects = state.Projects.Where(p => p.Id != id).ToList(),
        Tasks = state.Tasks.Where(t => !deleteTasks || t.ProjectId != id).Select(t => t.ProjectId == id ? t with { ProjectId = null } : t).ToList()
    };
    public static Workspace Parse(string json)
    {
        var s = JsonSerializer.Deserialize<Workspace>(json) ?? throw new FormatException("데이터가 비어 있습니다.");
        if (s.SchemaVersion != 1 || s.Tasks == null || s.Projects == null || s.Settings == null) throw new FormatException("지원하지 않는 데이터 형식입니다.");
        if (s.Tasks.Count > 100000 || s.Projects.Count > 10000) throw new FormatException("데이터 크기가 너무 큽니다.");
        if (s.Projects.Any(p => p == null || p.Id == Guid.Empty || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 200 || p.Description == null || p.Description.Length > 10000 || !Statuses.Contains(p.Status))) throw new FormatException("프로젝트 데이터가 올바르지 않습니다.");
        if(s.Projects.Any(p=>!ProgressModes.Contains(p.ProgressMode) || p.ManualPercent<0 || p.ManualPercent>100 || (p.PlanFile!=null && !FolderProgress.Files.Contains(p.PlanFile)) || (p.FolderPath!=null && (p.FolderPath.Length>32000 || !System.IO.Path.IsPathFullyQualified(p.FolderPath))) || (p.ProgressMode=="폴더 자동" && string.IsNullOrWhiteSpace(p.FolderPath))))throw new FormatException("진행률 연결 설정이 올바르지 않습니다.");
        if(s.Projects.Any(p=>p.Snapshot is {} c && (c.Percent is <0 or >100 || c.Done<0 || c.Total<0 || c.Done>c.Total || (c.Source!=null && !FolderProgress.Files.Contains(c.Source)) || (c.Error?.Length??0)>1000)))throw new FormatException("진행률 저장 결과가 올바르지 않습니다.");
        var ids = s.Projects.Select(p => p.Id).ToHashSet();
        if (ids.Count != s.Projects.Count || s.Tasks.Select(t => t?.Id).Distinct().Count() != s.Tasks.Count) throw new FormatException("중복된 ID가 있습니다.");
        if (s.Tasks.Any(t => t == null || t.Id == Guid.Empty || string.IsNullOrWhiteSpace(t.Title) || t.Title.Length > 500 || !Priorities.Contains(t.Priority) || (t.ProjectId.HasValue && !ids.Contains(t.ProjectId.Value)))) throw new FormatException("할 일 데이터가 올바르지 않습니다.");
        var v = s.Settings;
        if (!double.IsFinite(v.X) || !double.IsFinite(v.Y) || !double.IsFinite(v.Width) || !double.IsFinite(v.Height) || v.Width < 340 || v.Width > 3000 || v.Height < 400 || v.Height > 3000 || !double.IsFinite(v.Opacity) || v.Opacity < .4 || v.Opacity > 1) throw new FormatException("창 설정이 올바르지 않습니다.");
        return s;
    }
}
