using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MemHost;

public sealed class MainForm : Form
{
    private const int CanvasPixels = 330;
    private const int MenuHeight = 24;
    private const string ServerAddress = "localhost:25565";

    private static readonly (string Name, Color Color)[] Palette =
    {
        ("STONE", Color.FromArgb(0x80, 0x80, 0x80)),
        ("COBBLESTONE", Color.FromArgb(0x6E, 0x6E, 0x6E)),
        ("DIRT", Color.FromArgb(0x8B, 0x5A, 0x2B)),
        ("GRASS_BLOCK", Color.FromArgb(0x6A, 0xA8, 0x4F)),
        ("OAK_PLANKS", Color.FromArgb(0xB8, 0x94, 0x5F)),
        ("SAND", Color.FromArgb(0xE8, 0xDC, 0xA0)),
        ("GLASS", Color.FromArgb(0xBF, 0xE9, 0xF5)),
        ("BRICKS", Color.FromArgb(0xA0, 0x4A, 0x3A)),
        ("OBSIDIAN", Color.FromArgb(0x1A, 0x10, 0x20)),
        ("GLOWSTONE", Color.FromArgb(0xF7, 0xD6, 0x7B)),
        ("IRON_BLOCK", Color.FromArgb(0xD8, 0xD8, 0xD8)),
        ("GOLD_BLOCK", Color.FromArgb(0xF8, 0xD3, 0x3E)),
        ("DIAMOND_BLOCK", Color.FromArgb(0x6F, 0xE3, 0xE1)),
        ("EMERALD_BLOCK", Color.FromArgb(0x2F, 0xBF, 0x71)),
        ("RED_WOOL", Color.FromArgb(0xB0, 0x2E, 0x26)),
        ("ORANGE_WOOL", Color.FromArgb(0xF9, 0x80, 0x1D)),
        ("YELLOW_WOOL", Color.FromArgb(0xFE, 0xD8, 0x3D)),
        ("LIME_WOOL", Color.FromArgb(0x80, 0xC7, 0x1F)),
        ("BLUE_WOOL", Color.FromArgb(0x3C, 0x44, 0xAA)),
        ("PURPLE_WOOL", Color.FromArgb(0x89, 0x32, 0xB8)),
        ("WHITE_WOOL", Color.FromArgb(0xE9, 0xEC, 0xEC)),
        ("BLACK_WOOL", Color.FromArgb(0x1D, 0x1D, 0x21)),
    };

    private readonly string _serverDir;
    private ServerOptions _options;
    private string?[,] _cells;
    private int _selected;
    private bool _painting;
    private bool _erasing;
    private Process? _server;
    private TaskCompletionSource<bool>? _ready;

    private readonly BufferedPanel _canvas = new();
    private readonly FlowLayoutPanel _palette = new();
    private readonly RichTextBox _log = new();
    private readonly TextBox _input = new();
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _start = new();
    private readonly Button _stop = new();
    private readonly Button _join = new();
    private readonly Label _hint = new();

    private int Cell => Math.Max(6, CanvasPixels / _options.Size);

    public MainForm()
    {
        _serverDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MemHost");
        _options = ServerOptions.Load(_serverDir);
        _cells = new string?[_options.Size, _options.Size];

        Text = "Mem Host — Paint сервер Paper";
        ClientSize = new Size(988, 690);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        BuildMenu();

        _canvas.BackColor = Color.FromArgb(0x3A, 0x3A, 0x3A);
        _canvas.Paint += CanvasPaint;
        _canvas.MouseDown += CanvasDown;
        _canvas.MouseMove += CanvasMove;
        _canvas.MouseUp += (_, _) => _painting = false;
        Controls.Add(_canvas);

        _hint.Text = "ЛКМ — рисовать, ПКМ — стереть. Один пиксель = один блок.";
        _hint.Bounds = new Rectangle(12, 380, 356, 20);
        Controls.Add(_hint);

        Controls.Add(new Label { Text = "Блоки", Bounds = new Rectangle(378, 36, 300, 18) });

        _palette.Bounds = new Rectangle(378, 56, 300, 330);
        _palette.AutoScroll = true;
        _palette.WrapContents = true;
        for (int i = 0; i < Palette.Length; i++)
        {
            int index = i;
            var button = new Button
            {
                Text = Palette[i].Name.Replace('_', ' '),
                Size = new Size(140, 30),
                BackColor = Palette[i].Color,
                ForeColor = Palette[i].Color.GetBrightness() > 0.6f ? Color.Black : Color.White,
                FlatStyle = FlatStyle.Flat,
                Tag = index,
            };
            button.FlatAppearance.BorderSize = i == 0 ? 3 : 1;
            button.Click += (_, _) => SelectBlock(index);
            _palette.Controls.Add(button);
        }
        Controls.Add(_palette);
        SelectBlock(0);

        MakeButton("Создать сервер…", 36, CreateClicked);
        MakeButton("Применить рисунок", 76, ApplyClicked);
        MakeButton("Открыть папку сервера", 116, (_, _) => OpenFolder());

        _start.Text = "Запустить";
        _start.Bounds = new Rectangle(690, 156, 290, 40);
        _start.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
        _start.Click += StartClicked;
        Controls.Add(_start);

        _stop.Text = "Остановить";
        _stop.Bounds = new Rectangle(690, 204, 290, 34);
        _stop.Enabled = false;
        _stop.Click += StopClicked;
        Controls.Add(_stop);

        _join.Text = "Зайти на сервер";
        _join.Bounds = new Rectangle(690, 246, 290, 44);
        _join.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
        _join.Click += JoinClicked;
        Controls.Add(_join);

        _status.Bounds = new Rectangle(378, 402, 602, 34);
        _status.Text = File.Exists(Path.Combine(_serverDir, "paper.jar"))
            ? "Сервер готов: " + _serverDir
            : "Сервер ещё не создан — меню «Сервер» → «Создать сервер…»";
        Controls.Add(_status);

        _progress.Bounds = new Rectangle(378, 440, 602, 14);
        _progress.Visible = false;
        Controls.Add(_progress);

        _log.Bounds = new Rectangle(12, 452, 964, 170);
        _log.ReadOnly = true;
        _log.BackColor = Color.FromArgb(0x1E, 0x1E, 0x1E);
        _log.ForeColor = Color.Gainsboro;
        _log.Font = new Font("Consolas", 8.5f);
        _log.WordWrap = true;
        _log.DetectUrls = false;
        Controls.Add(_log);

        _input.Bounds = new Rectangle(12, 632, 880, 26);
        _input.PlaceholderText = "Команда серверу (например: memhost build, op ник, stop)";
        _input.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            SendCommand(_input.Text);
            _input.Clear();
        };
        Controls.Add(_input);

        var send = new Button { Text = "Отправить", Bounds = new Rectangle(900, 632, 76, 26) };
        send.Click += (_, _) => { SendCommand(_input.Text); _input.Clear(); };
        Controls.Add(send);

        LoadArt();
        LayoutCanvas();
        UpdateModeUi();
        UpdateTitle();
    }

    private void BuildMenu()
    {
        var menu = new MenuStrip { Dock = DockStyle.Top };
        var server = new ToolStripMenuItem("Сервер");
        server.DropDownItems.Add("Создать сервер…", null, (_, _) => CreateClicked(this, EventArgs.Empty));
        server.DropDownItems.Add("Запустить", null, (_, _) => StartClicked(this, EventArgs.Empty));
        server.DropDownItems.Add("Остановить", null, (_, _) => StopClicked(this, EventArgs.Empty));
        server.DropDownItems.Add(new ToolStripSeparator());
        server.DropDownItems.Add("Зайти на сервер", null, (_, _) => JoinClicked(this, EventArgs.Empty));
        server.DropDownItems.Add("Открыть папку сервера", null, (_, _) => OpenFolder());
        server.DropDownItems.Add(new ToolStripSeparator());
        server.DropDownItems.Add("Выход", null, (_, _) => Close());

        var art = new ToolStripMenuItem("Рисунок");
        art.DropDownItems.Add("Применить на сервере", null, (_, _) => ApplyClicked(this, EventArgs.Empty));
        art.DropDownItems.Add("Очистить холст", null, (_, _) => ClearCanvas());

        menu.Items.Add(server);
        menu.Items.Add(art);
        Controls.Add(menu);
        MainMenuStrip = menu;
    }

    private void MakeButton(string text, int y, EventHandler handler)
    {
        var button = new Button { Text = text, Bounds = new Rectangle(690, y, 290, 34) };
        button.Click += handler;
        Controls.Add(button);
    }

    private void SelectBlock(int index)
    {
        _selected = index;
        foreach (Control control in _palette.Controls)
        {
            if (control is Button button && control.Tag is int i) button.FlatAppearance.BorderSize = i == index ? 3 : 1;
        }
    }

    private void LayoutCanvas()
    {
        int cell = Cell;
        _canvas.Bounds = new Rectangle(12, 12 + MenuHeight, _options.Size * cell + 2, _options.Size * cell + 2);
        _canvas.Invalidate();
    }

    private static string ModeName(string type) => type switch
    {
        "Standart" => "Standart",
        "OneBlock" => "OneBlock",
        "SkyBlock" => "SkyBlock",
        "Objects" => "WHOOOOOOO",
        _ => "MemPaint",
    };

    private void UpdateTitle()
    {
        Text = "Mem Host — " + ModeName(_options.Type) + " · " + _options.Version
               + (_options.Type == "MemPaint" ? " · " + _options.Size + "×" + _options.Size : string.Empty);
    }

    private void UpdateModeUi()
    {
        bool paint = _options.Type == "MemPaint";
        _canvas.Enabled = paint;
        _palette.Enabled = paint;
        _hint.Text = paint
            ? "ЛКМ — рисовать, ПКМ — стереть. Один пиксель = один блок."
            : "Режим " + ModeName(_options.Type) + ": рисунок не нужен, мир строит плагин.";
    }

    private string?[,] ResizeGrid(int size)
    {
        int old = _cells.GetLength(0);
        var next = new string?[size, size];
        for (int z = 0; z < Math.Min(size, old); z++)
            for (int x = 0; x < Math.Min(size, old); x++)
                next[z, x] = _cells[z, x];
        return next;
    }

    private void CanvasPaint(object? sender, PaintEventArgs e)
    {
        int cell = Cell;
        int size = _options.Size;
        for (int z = 0; z < size; z++)
        {
            for (int x = 0; x < size; x++)
            {
                var rect = new Rectangle(x * cell, z * cell, cell, cell);
                Color color = ((x + z) % 2 == 0) ? Color.FromArgb(0x4A, 0x4A, 0x4A) : Color.FromArgb(0x44, 0x44, 0x44);
                if (_cells[z, x] is { } name)
                {
                    color = Palette.First(p => p.Name == name).Color;
                }
                using var brush = new SolidBrush(color);
                e.Graphics.FillRectangle(brush, rect);
                if (cell >= 10) e.Graphics.DrawRectangle(Pens.Black, rect);
            }
        }
    }

    private void CanvasDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
        _painting = true;
        _erasing = e.Button == MouseButtons.Right;
        PaintCell(e.Location);
    }

    private void CanvasMove(object? sender, MouseEventArgs e)
    {
        if (_painting) PaintCell(e.Location);
    }

    private void PaintCell(Point point)
    {
        int cell = Cell;
        int size = _options.Size;
        int x = point.X / cell;
        int z = point.Y / cell;
        if (x < 0 || z < 0 || x >= size || z >= size) return;
        string? value = _erasing ? null : Palette[_selected].Name;
        if (_cells[z, x] == value) return;
        _cells[z, x] = value;
        _canvas.Invalidate();
    }

    private void ClearCanvas()
    {
        Array.Clear(_cells);
        _canvas.Invalidate();
    }

    private void AppendLog(string? line)
    {
        if (string.IsNullOrEmpty(line)) return;
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(line));
            return;
        }
        _log.AppendText(line + Environment.NewLine);
        if (_log.Lines.Length > 800)
        {
            _log.Select(0, _log.GetFirstCharIndexFromLine(_log.Lines.Length - 600));
            _log.SelectedText = string.Empty;
        }
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
        if (line.Contains("Done (")) _ready?.TrySetResult(true);
    }

    private void SetStatus(string text) => _status.Text = text;

    private void ApplyClicked(object? sender, EventArgs e)
    {
        try
        {
            WriteArt();
            if (_server is { HasExited: false })
            {
                SendCommand("memhost build");
                SetStatus("Рисунок отправлен на сервер");
            }
            else
            {
                SetStatus("Рисунок сохранён — он появится при запуске сервера");
            }
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка: " + ex.Message);
        }
    }

    private void WriteArt()
    {
        var text = new StringBuilder();
        for (int z = 0; z < _options.Size; z++)
        {
            for (int x = 0; x < _options.Size; x++)
            {
                if (x > 0) text.Append(' ');
                text.Append(_cells[z, x] ?? "AIR");
            }
            text.Append('\n');
        }
        var folder = Path.Combine(_serverDir, "plugins", "MemHostPaint");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "paint.txt"), text.ToString(), new UTF8Encoding(false));
    }

    private void LoadArt()
    {
        var file = Path.Combine(_serverDir, "plugins", "MemHostPaint", "paint.txt");
        if (!File.Exists(file)) return;
        var lines = File.ReadAllLines(file).Where(line => line.Trim().Length > 0).ToArray();
        if (lines.Length == 0) return;
        int width = lines.Max(line => line.Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length);
        int size = Math.Max(width, lines.Length);
        if (size != _options.Size)
        {
            _cells = ResizeGrid(size);
            _options.Size = size;
        }
        var known = Palette.Select(p => p.Name).ToHashSet();
        for (int z = 0; z < lines.Length; z++)
        {
            var row = lines[z].Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int x = 0; x < Math.Min(row.Length, size); x++)
            {
                var name = row[x].Trim().ToUpperInvariant();
                _cells[z, x] = known.Contains(name) ? name : null;
            }
        }
    }

    private void OpenFolder()
    {
        Directory.CreateDirectory(_serverDir);
        Process.Start(new ProcessStartInfo { FileName = _serverDir, UseShellExecute = true });
    }

    private void SendCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        if (_server is not { HasExited: false })
        {
            SetStatus("Сервер не запущен");
            return;
        }
        _server.StandardInput.WriteLine(command);
        _server.StandardInput.Flush();
    }

    private async void CreateClicked(object? sender, EventArgs e)
    {
        using var dialog = new CreateServerForm(_options);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var options = dialog.Result;
        _cells = ResizeGrid(options.Size);
        _options = options;
        _options.Save(_serverDir);
        LayoutCanvas();
        UpdateModeUi();
        UpdateTitle();
        await BuildServerAsync();
    }

    private async Task BuildServerAsync()
    {
        if (_server is { HasExited: false })
        {
            SetStatus("Сервер запущен — сначала нажмите «Остановить»");
            return;
        }
        try
        {
            SetBusy(true);
            SetStatus("Готовлю папку " + _serverDir);
            Directory.CreateDirectory(_serverDir);
            Directory.CreateDirectory(Path.Combine(_serverDir, "plugins"));

            File.WriteAllText(Path.Combine(_serverDir, "eula.txt"), "eula=true\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(_serverDir, "server.properties"), BuildProperties(), new UTF8Encoding(false));
            WriteWhitelist();
            WriteArt();
            WritePluginConfig();

            var plugin = Path.Combine(AppContext.BaseDirectory, "MemHostPaint.jar");
            if (!File.Exists(plugin)) throw new FileNotFoundException("Рядом с программой нет MemHostPaint.jar", plugin);
            File.Copy(plugin, Path.Combine(_serverDir, "plugins", "MemHostPaint.jar"), true);

            var java = await EnsureJavaAsync();
            var paper = Path.Combine(_serverDir, "paper.jar");
            var versionFile = Path.Combine(_serverDir, "paper.version");
            bool needPaper = !File.Exists(paper) || new FileInfo(paper).Length < 1_000_000
                             || !File.Exists(versionFile) || File.ReadAllText(versionFile).Trim() != _options.Version;
            if (needPaper)
            {
                await DownloadPaperAsync(paper, _options.Version);
                File.WriteAllText(versionFile, _options.Version, new UTF8Encoding(false));
            }

            var javaForBatch = java == "java" ? "java" : Path.GetRelativePath(_serverDir, java);
            File.WriteAllText(Path.Combine(_serverDir, "start.bat"),
                "@echo off\r\ncd /d \"%~dp0\"\r\n\"" + javaForBatch + "\" -Xms1G -Xmx2G -jar paper.jar --nogui\r\npause\r\n");

            SetStatus("Сервер готов: " + _serverDir);
            AppendLog("=== Paper " + _options.Version + ", MemPaint " + _options.Size + "×" + _options.Size
                      + ", игроков " + _options.MaxPlayers
                      + ", online-mode=" + (!_options.AllowAll)
                      + ", whitelist=" + _options.Whitelist
                      + ", rcon=" + _options.Rcon + " ===");
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка: " + ex.Message);
            AppendLog("Ошибка: " + ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private string BuildProperties()
    {
        var text = new StringBuilder();
        if (_options.Type == "Standart")
        {
            text.AppendLine("level-type=minecraft\\:normal");
        }
        else
        {
            text.AppendLine("level-type=minecraft\\:flat");
            text.AppendLine("generator-settings={\"layers\":[{\"block\":\"minecraft:air\",\"height\":1}],\"biome\":\"minecraft:plains\"}");
        }
        text.AppendLine("online-mode=" + (_options.AllowAll ? "false" : "true"));
        text.AppendLine("white-list=" + (_options.Whitelist ? "true" : "false"));
        text.AppendLine("max-players=" + _options.MaxPlayers);
        text.AppendLine("view-distance=6");
        text.AppendLine("simulation-distance=6");
        text.AppendLine("spawn-protection=0");
        text.AppendLine("motd=" + _options.Motd);
        text.AppendLine("enable-rcon=" + (_options.Rcon ? "true" : "false"));
        text.AppendLine("rcon.port=25575");
        text.AppendLine("rcon.password=" + _options.RconPassword);
        return text.ToString();
    }

    private void WritePluginConfig()
    {
        string mode = _options.Type switch
        {
            "Standart" => "standart",
            "OneBlock" => "oneblock",
            "SkyBlock" => "skyblock",
            "Objects" => "objects",
            _ => "paint",
        };
        var folder = Path.Combine(_serverDir, "plugins", "MemHostPaint");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "config.yml"),
            "mode: " + mode + "\nworld: world\nfallback: STONE\n", new UTF8Encoding(false));
    }

    private void WriteWhitelist()
    {
        var file = Path.Combine(_serverDir, "whitelist.json");
        if (!_options.Whitelist)
        {
            if (File.Exists(file)) File.Delete(file);
            return;
        }
        var entry = new[] { new { uuid = OfflineUuid("MemHost"), name = "MemHost" } };
        File.WriteAllText(file, JsonSerializer.Serialize(entry), new UTF8Encoding(false));
    }

    private static string OfflineUuid(string name)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return hex[..8] + "-" + hex[8..12] + "-" + hex[12..16] + "-" + hex[16..20] + "-" + hex[20..];
    }

    private void SetBusy(bool busy)
    {
        _progress.Visible = busy;
        _progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        UseWaitCursor = busy;
    }

    private async void StartClicked(object? sender, EventArgs e)
    {
        if (await StartServerAsync()) SetStatus("Сервер запущен: " + ServerAddress);
    }

    private async void JoinClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_server is not { HasExited: false })
            {
                SetStatus("Запускаю сервер…");
                if (!await StartServerAsync()) return;
            }
            SetStatus("Жду, пока сервер будет готов…");
            if (!await WaitReadyAsync()) return;
            var java = await EnsureJavaAsync();
            if (!MinecraftLauncher.IsInstalled(_options.Version))
            {
                SetStatus("Minecraft " + _options.Version + " не установлен в официальном лаунчере");
                AppendLog("Не нашёл версию " + _options.Version + " в " + MinecraftLauncher.GameDirectory);
                return;
            }
            MinecraftLauncher.Launch(ServerAddress, java, _options.Version);
            SetStatus("Minecraft " + _options.Version + " запускается и заходит на сервер");
            AppendLog("=== Запуск клиента Minecraft " + _options.Version + " на " + ServerAddress + " ===");
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка входа в игру: " + ex.Message);
            AppendLog("Ошибка входа в игру: " + ex);
        }
    }

    private async Task<bool> StartServerAsync()
    {
        if (_server is { HasExited: false }) return true;
        if (!File.Exists(Path.Combine(_serverDir, "paper.jar")))
        {
            SetStatus("Сначала создайте сервер: меню «Сервер» → «Создать сервер…»");
            return false;
        }
        try
        {
            SetBusy(true);
            var java = await EnsureJavaAsync();
            _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var psi = new ProcessStartInfo(java)
            {
                WorkingDirectory = _serverDir,
                Arguments = "-Xms1G -Xmx2G -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 -jar paper.jar --nogui",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            _server = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _server.OutputDataReceived += (_, args) => AppendLog(args.Data);
            _server.ErrorDataReceived += (_, args) => AppendLog(args.Data);
            _server.Exited += (_, _) =>
            {
                _ready?.TrySetResult(false);
                BeginInvoke(() =>
                {
                    _start.Enabled = true;
                    _stop.Enabled = false;
                    SetStatus("Сервер остановлен");
                });
                AppendLog("=== Сервер остановлен ===");
            };
            _server.Start();
            _server.BeginOutputReadLine();
            _server.BeginErrorReadLine();
            _start.Enabled = false;
            _stop.Enabled = true;
            SetStatus("Сервер запускается…");
            AppendLog("=== Запуск Paper " + _options.Version + " ===");
            return true;
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка запуска: " + ex.Message);
            AppendLog("Ошибка запуска: " + ex);
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<bool> WaitReadyAsync()
    {
        var ready = _ready;
        if (ready == null) return _server is { HasExited: false };
        var finished = await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromMinutes(3)));
        if (finished == ready.Task) return await ready.Task;
        SetStatus("Сервер не ответил за 3 минуты — смотрите консоль");
        return false;
    }

    private async void StopClicked(object? sender, EventArgs e)
    {
        if (_server is not { HasExited: false }) return;
        SetStatus("Останавливаю сервер…");
        SendCommand("stop");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _server.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (_server is { HasExited: false }) _server.Kill();
        }
        SetStatus("Сервер остановлен");
    }

    private async Task<string> EnsureJavaAsync()
    {
        foreach (var folder in new[] { Path.Combine(AppContext.BaseDirectory, "jdk"), Path.Combine(_serverDir, "jdk") })
        {
            if (!Directory.Exists(folder)) continue;
            var bundled = Directory.EnumerateFiles(folder, "java.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (bundled != null) return bundled;
        }
        if (SystemJavaMajor() >= 25) return "java";

        var jdkFolder = Path.Combine(_serverDir, "jdk");
        SetBusy(true);
        SetStatus("Скачиваю Java 25 (Temurin)…");
        var zip = Path.Combine(_serverDir, "jdk.zip");
        await DownloadAsync("https://api.adoptium.net/v3/binary/latest/25/ga/windows/x64/jdk/hotspot/normal/eclipse?project=jdk", zip, "Java 25");
        ZipFile.ExtractToDirectory(zip, jdkFolder, true);
        File.Delete(zip);
        var path = Directory.EnumerateFiles(jdkFolder, "java.exe", SearchOption.AllDirectories).First();
        SetBusy(false);
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
            string text = process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd();
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

    private async Task DownloadPaperAsync(string target, string version)
    {
        SetStatus("Ищу последнюю сборку Paper " + version + "…");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        string json = await http.GetStringAsync("https://fill.papermc.io/v3/projects/paper/versions/" + version + "/builds");
        using var document = JsonDocument.Parse(json);
        string? url = null;
        foreach (var build in document.RootElement.EnumerateArray())
        {
            if (build.GetProperty("channel").GetString() != "STABLE") continue;
            url = build.GetProperty("downloads").GetProperty("server:default").GetProperty("url").GetString();
            break;
        }
        if (url == null) throw new InvalidOperationException("Нет стабильной сборки Paper " + version);
        await DownloadAsync(url, target, "Paper " + version);
    }

    private async Task DownloadAsync(string url, string target, string label)
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
            string megabytes = $"{done / 1048576} МБ";
            SetStatus(total > 0
                ? $"{label}: {megabytes} из {total / 1048576} МБ"
                : $"{label}: {megabytes}");
            if (total > 0) _progress.Value = (int)Math.Min(100, done * 100 / total);
        }
    }
}

internal sealed class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }
}
