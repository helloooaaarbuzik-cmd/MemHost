using System.Text.Json;

namespace MemHost;

internal sealed class ServerOptions
{
    public string Name { get; set; } = "Сервер";
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

internal static class ServerStore
{
    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MemHost");

    public static string ServersRoot => Path.Combine(Root, "servers");

    public static List<(string Name, string Dir)> All()
    {
        var list = new List<(string, string)>();
        if (File.Exists(Path.Combine(Root, "memhost.json")) || File.Exists(Path.Combine(Root, "paper.jar")))
        {
            list.Add(("Мой сервер", Root));
        }
        if (Directory.Exists(ServersRoot))
        {
            foreach (var dir in Directory.GetDirectories(ServersRoot))
            {
                list.Add((Path.GetFileName(dir), dir));
            }
        }
        return list;
    }

    public static string NewDirectory(string name)
    {
        var clean = new string(name.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(clean)) clean = "Сервер";
        Directory.CreateDirectory(ServersRoot);
        var dir = Path.Combine(ServersRoot, clean);
        int index = 2;
        while (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "memhost.json")) == false && Directory.GetFileSystemEntries(dir).Length > 0)
        {
            dir = Path.Combine(ServersRoot, clean + " " + index++);
        }
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void Delete(string dir)
    {
        if (!Directory.Exists(dir)) return;
        if (!dir.StartsWith(ServersRoot, StringComparison.OrdinalIgnoreCase) && !dir.Equals(Root, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        Directory.Delete(dir, true);
    }
}
