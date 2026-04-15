using System.Text.Json;
using HarnessMcp.ControlPlane.Support;

namespace HarnessMcp.ControlPlane;

public class SessionStore
{
    private readonly string _sessionsRoot;
    private readonly JsonSerializerOptions _jsonOptions;

    public SessionStore(string sessionsRoot)
    {
        _sessionsRoot = sessionsRoot;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public Session? Load(string sessionId)
    {
        var path = GetPath(sessionId);
        if (!File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Session>(json, _jsonOptions);
    }

    public void Save(Session session)
    {
        EnsureDirectoryExists();
        session.UpdatedUtc = UtcClock.UtcNow;
        var json = JsonSerializer.Serialize(session, _jsonOptions);
        File.WriteAllText(GetPath(session.SessionId), json);
    }

    public void Delete(string sessionId)
    {
        var path = GetPath(sessionId);
        if (File.Exists(path))
            File.Delete(path);
    }

    public IEnumerable<Session> LoadAll()
    {
        EnsureDirectoryExists();
        foreach (var file in Directory.GetFiles(_sessionsRoot, "*.json"))
        {
            var json = File.ReadAllText(file);
            yield return JsonSerializer.Deserialize<Session>(json, _jsonOptions)!;
        }
    }

    private string GetPath(string sessionId) => Path.Combine(_sessionsRoot, $"{sessionId}.json");

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_sessionsRoot))
            Directory.CreateDirectory(_sessionsRoot);
    }
}