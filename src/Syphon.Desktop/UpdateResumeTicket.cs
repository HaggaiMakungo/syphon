using System.IO;
using System.Text.Json;
namespace Syphon.Desktop;

public sealed record UpdateResumeTicket(string Version, DateTimeOffset CreatedAt, Guid[] Tasks)
{
    public void Save(string path)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this)); File.Move(temporary, path, true);
    }
    public static Guid[] Consume(string path, string installedVersion, DateTimeOffset now)
    {
        if (!File.Exists(path)) return [];
        try {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > 65536) return [];
            var ticket = JsonSerializer.Deserialize<UpdateResumeTicket>(File.ReadAllText(path));
            return ticket is not null && ticket.Version == installedVersion && ticket.Tasks is not null && ticket.CreatedAt <= now && ticket.CreatedAt > now.AddDays(-1)
                ? ticket.Tasks.Distinct().Take(1000).ToArray() : [];
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return []; }
        finally { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
