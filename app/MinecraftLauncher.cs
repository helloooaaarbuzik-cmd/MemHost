using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MemHost;

internal static class MinecraftLauncher
{
    public static string GameDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");

    public static bool IsInstalled(string version) =>
        File.Exists(Path.Combine(GameDirectory, "versions", version, version + ".json"))
        && File.Exists(Path.Combine(GameDirectory, "versions", version, version + ".jar"));

    public static Process Launch(string address, string javaPath, string version)
    {
        var gameDir = GameDirectory;
        var versionDir = Path.Combine(gameDir, "versions", version);
        var jsonPath = Path.Combine(versionDir, version + ".json");
        var clientJar = Path.Combine(versionDir, version + ".jar");
        if (!File.Exists(jsonPath) || !File.Exists(clientJar))
            throw new FileNotFoundException(
                "Не нашёл Minecraft " + version + " в " + gameDir + ". Установите эту версию в официальном лаунчере.");

        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var root = document.RootElement;

        var natives = Path.Combine(Path.GetTempPath(), "memhost-natives-" + version);
        Directory.CreateDirectory(natives);

        var classpath = new List<string>();
        foreach (var library in root.GetProperty("libraries").EnumerateArray())
        {
            if (!RulesAllow(library)) continue;
            if (!library.TryGetProperty("downloads", out var downloads)) continue;
            if (!downloads.TryGetProperty("artifact", out var artifact)) continue;
            var relative = artifact.GetProperty("path").GetString();
            if (relative == null) continue;
            var full = Path.Combine(gameDir, "libraries", relative.Replace('/', '\\'));
            if (File.Exists(full)) classpath.Add(full);
        }
        classpath.Add(clientJar);

        var values = new Dictionary<string, string>
        {
            ["auth_player_name"] = "MemHost",
            ["version_name"] = version,
            ["game_directory"] = gameDir,
            ["assets_root"] = Path.Combine(gameDir, "assets"),
            ["assets_index_name"] = root.GetProperty("assetIndex").GetProperty("id").GetString() ?? "legacy",
            ["auth_uuid"] = "00000000000000000000000000000001",
            ["auth_access_token"] = "0",
            ["clientid"] = "",
            ["auth_xuid"] = "",
            ["version_type"] = "release",
            ["natives_directory"] = natives,
            ["launcher_name"] = "MemHost",
            ["launcher_version"] = "1.0",
            ["classpath"] = string.Join(";", classpath),
            ["quickPlayMultiplayer"] = address,
            ["resolution_width"] = "1280",
            ["resolution_height"] = "720",
        };

        var arguments = root.GetProperty("arguments");
        var processArgs = new List<string> { "-Xmx2G" };
        processArgs.AddRange(Collect(arguments.GetProperty("jvm"), values));
        processArgs.Add(root.GetProperty("mainClass").GetString() ?? "net.minecraft.client.main.Main");
        processArgs.AddRange(Collect(arguments.GetProperty("game"), values));

        var info = new ProcessStartInfo(javaPath)
        {
            WorkingDirectory = gameDir,
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        foreach (var argument in processArgs) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new InvalidOperationException("Не удалось запустить Minecraft");
    }

    private static IEnumerable<string> Collect(JsonElement part, Dictionary<string, string> values)
    {
        foreach (var item in part.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                yield return Substitute(item.GetString()!, values);
                continue;
            }
            if (!RulesAllow(item)) continue;
            var value = item.GetProperty("value");
            if (value.ValueKind == JsonValueKind.String)
            {
                yield return Substitute(value.GetString()!, values);
                continue;
            }
            foreach (var single in value.EnumerateArray())
                yield return Substitute(single.GetString()!, values);
        }
    }

    private static string Substitute(string text, Dictionary<string, string> values) =>
        Regex.Replace(text, "\\$\\{([a-zA-Z_]+)\\}", match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? value : string.Empty);

    private static bool RulesAllow(JsonElement item)
    {
        if (!item.TryGetProperty("rules", out var rules)) return true;
        bool allow = false;
        foreach (var rule in rules.EnumerateArray())
        {
            if (!RuleMatches(rule)) continue;
            allow = rule.GetProperty("action").GetString() == "allow";
        }
        return allow;
    }

    private static bool RuleMatches(JsonElement rule)
    {
        if (rule.TryGetProperty("features", out var features))
        {
            foreach (var feature in features.EnumerateObject())
            {
                bool enabled = feature.Name == "is_quick_play_multiplayer";
                if (feature.Value.GetBoolean() != enabled) return false;
            }
        }
        if (!rule.TryGetProperty("os", out var os)) return true;
        if (os.TryGetProperty("name", out var name) && name.GetString() != "windows") return false;
        if (os.TryGetProperty("arch", out var arch) && arch.GetString() != "x86_64") return false;
        return true;
    }
}
