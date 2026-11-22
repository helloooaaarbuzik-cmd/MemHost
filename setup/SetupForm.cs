using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MemHostSetup;

public sealed class SetupForm : Form
{
    private readonly string _root = AppContext.BaseDirectory;
    private readonly TextBox _log = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _install = new();
    private readonly Button _run = new();
    private readonly Button _launcher = new();
    private readonly Label _status = new();

    public SetupForm()
    {
        Text = "Mem Host Setup";
        ClientSize = new Size(620, 430);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        Controls.Add(new Label
        {
            Text = "Mem Host — установка",
            Font = new Font(Font.FontFamily, 14f, FontStyle.Bold),
            Bounds = new Rectangle(18, 14, 400, 28),
        });

        Controls.Add(new Label
        {
            Text = "Папка: " + _root,
            ForeColor = Color.DimGray,
            Bounds = new Rectangle(20, 44, 580, 18),
        });

        _log.Bounds = new Rectangle(18, 70, 584, 260);
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.BackColor = Color.FromArgb(0x1E, 0x1E, 0x1E);
        _log.ForeColor = Color.Gainsboro;
        _log.Font = new Font("Consolas", 9f);
        Controls.Add(_log);

        _progress.Bounds = new Rectangle(18, 338, 584, 16);
        Controls.Add(_progress);

        _status.Bounds = new Rectangle(18, 360, 584, 20);
        _status.Text = "Нажмите «Установить»";
        Controls.Add(_status);

        _install.Text = "Установить";
        _install.Bounds = new Rectangle(18, 386, 140, 34);
        _install.Click += InstallClicked;
        Controls.Add(_install);

        _launcher.Text = "Лаунчер Minecraft";
        _launcher.Bounds = new Rectangle(168, 386, 150, 34);
        _launcher.Click += (_, _) => OpenLauncher();
        Controls.Add(_launcher);

        _run.Text = "Запустить Mem Host";
        _run.Bounds = new Rectangle(328, 386, 160, 34);
        _run.Enabled = false;
        _run.Click += (_, _) => LaunchMemHost();
        Controls.Add(_run);

        var close = new Button { Text = "Закрыть", Bounds = new Rectangle(498, 386, 104, 34) };
        close.Click += (_, _) => Close();
        Controls.Add(close);

        Log("Mem Host Setup. Проверяю, что нужно для работы.");
    }

    private void Log(string line)
    {
        _log.AppendText(line + Environment.NewLine);
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private string MinecraftVersion()
    {
        try
        {
            var options = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MemHost", "memhost.json");
            if (File.Exists(options))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(options));
                if (document.RootElement.TryGetProperty("Version", out var version))
                {
                    var value = version.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }
        }
        catch
        {
        }
        return "26.3";
    }

    private async void InstallClicked(object? sender, EventArgs e)
    {
        _install.Enabled = false;
        try
        {
            await InstallAsync();
        }
        catch (Exception ex)
        {
            _status.Text = "Ошибка";
            Log("Ошибка: " + ex.Message);
        }
        finally
        {
            _install.Enabled = true;
        }
    }

    private async Task InstallAsync()
    {
        _progress.Style = ProgressBarStyle.Marquee;

        if (!File.Exists(Path.Combine(_root, "MemHost.exe")))
        {
            _status.Text = "Нет MemHost.exe";
            Log("ОШИБКА: рядом с Setup нет MemHost.exe. Распакуйте архив целиком.");
            return;
        }
        Log("[ok] MemHost.exe найден");
        Log("[ok] C# и .NET уже внутри MemHost.exe — ничего ставить не нужно");

        var javaPath = await EnsureJavaAsync();
        if (javaPath == null)
        {
            _status.Text = "Без Java не запустить";
            return;
        }

        var version = MinecraftVersion();
        var client = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".minecraft", "versions", version, version + ".jar");
        if (File.Exists(client))
        {
            Log("[ok] Minecraft " + version + " найден — кнопка «Зайти на сервер» будет работать");
        }
        else
        {
            Log("[!] Minecraft " + version + " не установлен. Поставьте эту версию в официальном лаунчере.");
        }

        CreateShortcut();
        Log("[ok] Ярлык «Mem Host» на рабочем столе");

        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Value = 100;
        _status.Text = "Готово";
        _run.Enabled = true;
        Log("--- Установка завершена, можно запускать Mem Host ---");
    }

    private async Task<string?> EnsureJavaAsync()
    {
        var jdkFolder = Path.Combine(_root, "jdk");
        if (Directory.Exists(jdkFolder))
        {
            var bundled = Directory.EnumerateFiles(jdkFolder, "java.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (bundled != null)
            {
                Log("[ok] Java 25 уже рядом: " + bundled);
                return bundled;
            }
        }

        int system = SystemJavaMajor();
        if (system >= 25)
        {
            Log("[ok] В системе есть Java " + system);
            return "java";
        }

        Log("Скачиваю Java 25 (Temurin, около 140 МБ)...");
        _status.Text = "Скачиваю Java 25…";
        var zip = Path.Combine(_root, "jdk.zip");
        await DownloadAsync("https://api.adoptium.net/v3/binary/latest/25/ga/windows/x64/jdk/hotspot/normal/eclipse?project=jdk", zip);
        Log("Распаковываю Java...");
        _status.Text = "Распаковываю Java…";
        Directory.CreateDirectory(jdkFolder);
        ZipFile.ExtractToDirectory(zip, jdkFolder, true);
        File.Delete(zip);
        var path = Directory.EnumerateFiles(jdkFolder, "java.exe", SearchOption.AllDirectories).First();
        Log("[ok] Java 25 установлена: " + path);
        return path;
    }

    private static int SystemJavaMajor()
    {
        try
        {
            var psi = new ProcessStartInfo("java", "-version")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process == null) return 0;
            var text = process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            var match = Regex.Match(text, "version \"(\\d+)");
            if (!match.Success) return 0;
            int major = int.Parse(match.Groups[1].Value);
            return major == 1 ? 8 : major;
        }
        catch
        {
            return 0;
        }
    }

    private async Task DownloadAsync(string url, string target)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? 0;
        _progress.Style = ProgressBarStyle.Continuous;
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var destination = File.Create(target);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            _status.Text = $"Скачиваю Java 25: {done / 1048576} МБ" + (total > 0 ? $" из {total / 1048576} МБ" : string.Empty);
            if (total > 0) _progress.Value = (int)Math.Min(100, done * 100 / total);
        }
    }

    private void CreateShortcut()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(desktop, "Mem Host.lnk");
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type == null) return;
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic shortcut = shell.CreateShortcut(path);
        shortcut.TargetPath = Path.Combine(_root, "MemHost.exe");
        shortcut.WorkingDirectory = _root;
        shortcut.IconLocation = Path.Combine(_root, "MemHost.exe") + ",0";
        shortcut.Description = "Mem Host — Paint сервер Minecraft";
        shortcut.Save();
    }

    private void OpenLauncher()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Minecraft Launcher", "MinecraftLauncher.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Minecraft Launcher", "MinecraftLauncher.exe"),
        };
        var found = candidates.FirstOrDefault(File.Exists);
        if (found == null)
        {
            Log("[!] Лаунчер Minecraft не найден в Program Files");
            return;
        }
        Process.Start(new ProcessStartInfo { FileName = found, UseShellExecute = true });
    }

    private void LaunchMemHost()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(_root, "MemHost.exe"),
            WorkingDirectory = _root,
        });
    }
}
