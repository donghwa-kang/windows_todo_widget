using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace TerminalWidget.Core;
public sealed class WorkspaceRepository
{
    public string FilePath { get; }
    public string? RecoveryMessage { get; private set; }
    public WorkspaceRepository(string directory) { Directory.CreateDirectory(directory); FilePath = Path.Combine(directory, "workspace.json"); }
    public Workspace Load()
    {
        if (!File.Exists(FilePath)) return Workspace.Empty();
        try { return Rules.Parse(File.ReadAllText(FilePath)); }
        catch (Exception ex) when (ex is JsonException or FormatException or IOException)
        {
            string preserved = FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmssfff");
            File.Copy(FilePath, preserved);
            Workspace restored = Workspace.Empty();
            try { restored = Rules.Parse(File.ReadAllText(FilePath + ".bak")); RecoveryMessage = "백업에서 복원했습니다."; }
            catch (Exception backup) when (backup is IOException or JsonException or FormatException) { RecoveryMessage = "복원할 백업이 없어 빈 상태로 시작합니다."; }
            RecoveryMessage += " 손상된 원본 보존: " + preserved;
            return restored;
        }
    }
    public void Save(Workspace state)
    {
        string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        Rules.Parse(json);
        string temp = FilePath + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json); stream.Write(bytes); stream.Flush(true);
        }
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
        else File.Move(temp, FilePath);
    }
}
