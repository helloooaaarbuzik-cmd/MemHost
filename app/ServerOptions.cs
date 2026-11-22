using System.Text.Json;

namespace MemHost;

internal sealed class ServerOptions
{
    public string Type { get; set; } = "MemPaint";
    public int Size { get; set; } = 10;
    public string Version { get; set; } = "26.3";
    public int MaxPlayers { get; set; } = 10;
    public bool AllowAll { get; set; } = true;
    public bool Whitelist { get; set; }
    public bool Rcon { get; set; }
    public string RconPassword { get; set; } = "memhost";
    public string Motd { get; set; } = "MemPaint";

    private static string FilePath(string serverDir) => Path.Combine(serverDir, "memhost.json");

    public static ServerOptions Load(string serverDir)
    {
        try
        {
            var path = FilePath(serverDir);
            if (!File.Exists(path)) return new ServerOptions();
            return JsonSerializer.Deserialize<ServerOptions>(File.ReadAllText(path)) ?? new ServerOptions();
        }
        catch
        {
            return new ServerOptions();
        }
    }

    public void Save(string serverDir)
    {
        Directory.CreateDirectory(serverDir);
        File.WriteAllText(FilePath(serverDir), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
