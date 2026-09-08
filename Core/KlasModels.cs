using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace TerminalWidget.Core;

public sealed record KlasCourse(string Name, string Semester, int Lectures, int Assignments, DateTimeOffset? LectureDue, DateTimeOffset? AssignmentDue);
public sealed record KlasSnapshot(int SchemaVersion, List<KlasCourse> Courses, DateTimeOffset CheckedAt);

public static class KlasRules
{
    // KLAS 서버의 시간은 한국 표준시다. PC 시간대가 달라도 마감 판정은 같다.
    private static DateTimeOffset Date(string value, bool lecture)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new FormatException("KLAS 마감일 형식이 바뀌었습니다. 기존 목록을 유지합니다.");
        if (lecture && value.Trim().Length == 16) date = date.AddSeconds(59);
        return new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), TimeSpan.FromHours(9));
    }
    private static string Text(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
        ? value.GetString()! : throw new FormatException($"KLAS 응답의 {key} 필드를 확인할 수 없습니다.");
    private static JsonElement Rows(JsonElement item, string key)
    {
        var rows = item.GetProperty(key);
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 10000) throw new FormatException("KLAS 목록 형식을 확인할 수 없습니다.");
        return rows;
    }
    public static KlasSnapshot ParseResponse(string json, DateTimeOffset now)
    {
        if (json.Length > 8 * 1024 * 1024) throw new FormatException("KLAS 응답이 너무 큽니다.");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > 100) throw new FormatException("수강 과목을 확인할 수 없습니다.");
        var courses = new List<KlasCourse>();
        foreach (var subject in root.EnumerateArray())
        {
            var lectures = new List<DateTimeOffset>();
            var assignments = new List<DateTimeOffset>();
            foreach (var item in Rows(subject, "lectures").EnumerateArray())
            {
                if (Text(item, "evltnSe") != "lesson" || Text(item, "isonoff") == "OFF") continue;
                var progress = item.GetProperty("prog");
                if (!double.TryParse(progress.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) || !double.IsFinite(percent) || percent < 0 || percent > 100)
                    throw new FormatException("강의 수강률을 확인할 수 없습니다.");
                if (percent >= 100) continue;
                var due = Date(Text(item, "endDate"), true);
                if (due >= now) lectures.Add(due);
            }
            foreach (var item in Rows(subject, "assignments").EnumerateArray())
            {
                string submitted = Text(item, "submityn");
                if (submitted == "Y") continue;
                if (submitted != "N") throw new FormatException("과제 제출 여부를 확인할 수 없습니다.");
                var due = Date(Text(item, "expiredate"), false);
                if (due < now && item.TryGetProperty("reexpiredate", out var extra) && extra.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(extra.GetString()))
                    due = Date(extra.GetString()!, false);
                if (due >= now) assignments.Add(due);
            }
            string name = Text(subject, "name"), semester = Text(subject, "semester");
            if (string.IsNullOrWhiteSpace(name) || name.Length > 500 || string.IsNullOrWhiteSpace(semester) || semester.Length > 40) throw new FormatException("수강 과목 정보가 올바르지 않습니다.");
            courses.Add(new(name, semester, lectures.Count, assignments.Count, lectures.Count > 0 ? lectures.Min() : null, assignments.Count > 0 ? assignments.Min() : null));
        }
        return new(1, courses, now);
    }
    public static KlasSnapshot ParseCache(string json)
    {
        var value = JsonSerializer.Deserialize<KlasSnapshot>(json);
        if (value == null || value.SchemaVersion != 1 || value.Courses == null || value.Courses.Count > 100 || value.CheckedAt == default ||
            value.Courses.Any(c => c == null || string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 500 || string.IsNullOrWhiteSpace(c.Semester) || c.Semester.Length > 40 || c.Lectures is < 0 or > 10000 || c.Assignments is < 0 or > 10000 || (c.Lectures > 0) != c.LectureDue.HasValue || (c.Assignments > 0) != c.AssignmentDue.HasValue))
            throw new FormatException("KLAS 저장 목록을 확인할 수 없습니다.");
        return value;
    }
}
