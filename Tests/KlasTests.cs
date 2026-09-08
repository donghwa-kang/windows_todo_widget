using System.Text.Json;
using TerminalWidget.Core;

internal static class KlasTests
{
    public static void Run(Action<bool, string> check)
    {
        var now = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.FromHours(9));
        string Response(object[] lectures, object[] assignments) => JsonSerializer.Serialize(new[] { new { name = "테스트 과목", semester = "2026,2", lectures, assignments } });
        object Lecture(object progress, string end = "2026-09-09 12:00", string mode = "ON", string type = "lesson") => new { prog = progress, endDate = end, isonoff = mode, evltnSe = type };
        object Task(string submitted, string end, string? extra = null) => new { submityn = submitted, expiredate = end, reexpiredate = extra };
        var snapshot = KlasRules.ParseResponse(Response(new[] { Lecture(0), Lecture(100), Lecture(20, mode: "OFF"), Lecture(0, type: "test"), Lecture(0, "2026-09-07 12:00") },
            new[] { Task("N", "2026-09-09 12:00:00"), Task("Y", "2026-09-09 12:00:00"), Task("N", "2026-09-07 12:00:00"), Task("N", "2026-09-07 12:00:00", "2026-09-10 12:00:00") }), now);
        check(snapshot.Courses[0].Lectures == 1, "KLAS 완료·오프라인·기한 경과 강의 제외");
        check(snapshot.Courses[0].Assignments == 2, "KLAS 제출 과제 제외 및 추가 제출 기한 포함");
        check(snapshot.Courses[0].AssignmentDue == now.AddDays(1), "KLAS 가장 가까운 마감 표시");
        var boundary = KlasRules.ParseResponse(Response(new[] { Lecture("0", "2026-09-08 11:59"), Lecture("100"), Lecture(0, "2026-09-08 12:00") }, Array.Empty<object>()), now);
        check(boundary.Courses[0].Lectures == 1 && boundary.Courses[0].LectureDue == now.AddSeconds(59), "KLAS 분 단위 강의 마감 59초 경계 및 문자열 진행률");
        var utc = KlasRules.ParseResponse(Response(new[] { Lecture(0, "2026-09-08 12:00") }, Array.Empty<object>()), now.ToUniversalTime());
        check(utc.Courses[0].LectureDue == boundary.Courses[0].LectureDue, "KLAS 한국 표준시 마감은 PC 시간대에 독립적");
        var empty = KlasRules.ParseResponse(Response(Array.Empty<object>(), Array.Empty<object>()), now);
        check(empty.Courses.Count == 1 && empty.Courses[0].Lectures + empty.Courses[0].Assignments == 0, "KLAS 정상 조회의 빈 현황");
        void Reject(string json, string label)
        {
            bool failed = false;
            try { KlasRules.ParseResponse(json, now); } catch (Exception e) when (e is FormatException or JsonException or InvalidOperationException or KeyNotFoundException) { failed = true; }
            check(failed, label);
        }
        Reject("<html>로그인</html>", "KLAS 로그인 응답을 빈 목록으로 오인하지 않음");
        Reject(Response(new[] { Lecture(0, "알 수 없음") }, Array.Empty<object>()), "KLAS 잘못된 마감일 거부");
        Reject(Response(Array.Empty<object>(), new[] { Task("UNKNOWN", "2026-09-09 12:00:00") }), "KLAS 알 수 없는 제출 상태 거부");
        Reject(Response(new[] { Lecture("unknown") }, Array.Empty<object>()), "KLAS 알 수 없는 진행률 거부");
        var restored = KlasRules.ParseCache(JsonSerializer.Serialize(snapshot));
        check(restored.Courses.SequenceEqual(snapshot.Courses) && restored.CheckedAt == now, "KLAS 스키마 포함 저장 목록 복원");
        bool badCache = false;
        try { KlasRules.ParseCache(JsonSerializer.Serialize(snapshot with { SchemaVersion = 99 })); } catch (FormatException) { badCache = true; }
        check(badCache, "KLAS 미지원 캐시 버전 거부");
        var workspace = Workspace.Empty() with { Settings = new Preferences(TasksCollapsed: true, ProjectsCollapsed: true, KlasCollapsed: true) };
        check(Rules.Parse(JsonSerializer.Serialize(workspace)).Settings == workspace.Settings, "각 섹션 접힘 상태 저장 및 복원");
        var old = JsonSerializer.SerializeToNode(workspace)!;
        foreach (var key in new[] { "TasksCollapsed", "ProjectsCollapsed", "KlasCollapsed" }) old["Settings"]!.AsObject().Remove(key);
        var settings = Rules.Parse(old.ToJsonString()).Settings;
        check(!settings.TasksCollapsed && !settings.ProjectsCollapsed && !settings.KlasCollapsed, "이전 위젯 설정은 섹션 펼침으로 호환");
    }
}
