namespace MemHost;

internal sealed class StartForm : Form
{
    private static readonly (string Code, string Label)[] Modes =
    {
        ("MemPaint", "MemPaint"),
        ("Standart", "Standart"),
        ("OneBlock", "OneBlock"),
        ("SkyBlock", "SkyBlock"),
        ("Objects", "WHOOOOOOO"),
    };

    private readonly ListBox _list = new();
    private readonly Label _status = new();
    private readonly List<(string Name, string Dir)> _servers = new();

    public StartForm()
    {
        Text = "Mem Host";
        ClientSize = new Size(560, 430);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        Controls.Add(new Label
        {
            Text = "Mem Host",
            Font = new Font(Font.FontFamily, 16f, FontStyle.Bold),
            Bounds = new Rectangle(18, 12, 300, 32),
        });
        Controls.Add(new Label
        {
            Text = "Minecraft-сервер из рисунка и готовых режимов",
            ForeColor = Color.DimGray,
            Bounds = new Rectangle(20, 46, 500, 18),
        });
        Controls.Add(new Label { Text = "Мои серверы", Bounds = new Rectangle(20, 74, 300, 18) });

        _list.Bounds = new Rectangle(18, 96, 524, 240);
        _list.Font = new Font("Segoe UI", 10f);
        _list.DoubleClick += (_, _) => OpenSelected();
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        Controls.Add(_list);

        _status.Bounds = new Rectangle(20, 342, 522, 18);
        _status.ForeColor = Color.DimGray;
        Controls.Add(_status);

        var create = new Button { Text = "Создать сервер…", Bounds = new Rectangle(18, 370, 170, 40) };
        create.Click += (_, _) => CreateServer();
        Controls.Add(create);

        var open = new Button { Text = "Открыть", Bounds = new Rectangle(196, 370, 130, 40) };
        open.Click += (_, _) => OpenSelected();
        Controls.Add(open);

        var delete = new Button { Text = "Удалить", Bounds = new Rectangle(334, 370, 110, 40) };
        delete.Click += (_, _) => DeleteSelected();
        Controls.Add(delete);

        var exit = new Button { Text = "Выход", Bounds = new Rectangle(452, 370, 90, 40) };
        exit.Click += (_, _) => Close();
        Controls.Add(exit);

        RefreshList();
        UpdateButtons();
    }

    private static string ModeName(string code)
    {
        foreach (var mode in Modes)
        {
            if (mode.Code == code) return mode.Label;
        }
        return code;
    }

    private void RefreshList()
    {
        _servers.Clear();
        _servers.AddRange(ServerStore.All());
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var (name, dir) in _servers)
        {
            var options = ServerOptions.Load(dir);
            _list.Items.Add(name + "   ·   " + ModeName(options.Type) + " " + options.Version);
        }
        _list.EndUpdate();
        if (_servers.Count > 0) _list.SelectedIndex = 0;
        _status.Text = _servers.Count == 0
            ? "Серверов пока нет — нажмите «Создать сервер…»"
            : "Папка: " + ServerStore.Root;
    }

    private void UpdateButtons()
    {
        bool has = _list.SelectedIndex >= 0 && _list.SelectedIndex < _servers.Count;
        foreach (Control control in Controls)
        {
            if (control is Button button && (button.Text == "Открыть" || button.Text == "Удалить"))
            {
                button.Enabled = has;
            }
        }
    }

    private void CreateServer()
    {
        var defaults = new ServerOptions { Name = "Сервер " + (ServerStore.All().Count + 1) };
        using var dialog = new CreateServerForm(defaults);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var options = dialog.Result;
        var dir = ServerStore.NewDirectory(options.Name);
        options.Name = Path.GetFileName(dir);
        options.Save(dir);
        RefreshList();
        int index = _servers.FindIndex(s => s.Dir == dir);
        if (index >= 0) _list.SelectedIndex = index;
        Open(dir, options);
    }

    private void OpenSelected()
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _servers.Count) return;
        var (_, dir) = _servers[_list.SelectedIndex];
        Open(dir, ServerOptions.Load(dir));
    }

    private void Open(string dir, ServerOptions options)
    {
        Hide();
        var form = new MainForm(dir, options);
        form.FormClosed += (_, _) =>
        {
            RefreshList();
            Show();
            Activate();
        };
        form.Show();
    }

    private void DeleteSelected()
    {
        if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _servers.Count) return;
        var (name, dir) = _servers[_list.SelectedIndex];
        var answer = MessageBox.Show(this, "Удалить сервер «" + name + "» вместе с миром?", "Mem Host",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;
        try
        {
            ServerStore.Delete(dir);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось удалить: " + ex.Message, "Mem Host");
        }
        RefreshList();
        UpdateButtons();
    }
}
