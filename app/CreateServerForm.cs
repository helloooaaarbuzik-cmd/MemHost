using System.Net.Http;
using System.Text.Json;

namespace MemHost;

internal sealed class CreateServerForm : Form
{
    private static readonly int[] Sizes = { 10, 16, 24, 32, 48, 64 };

    private readonly ComboBox _type = new();
    private readonly ComboBox _size = new();
    private readonly ComboBox _version = new();
    private readonly NumericUpDown _players = new();
    private readonly CheckBox _allowAll = new();
    private readonly CheckBox _whitelist = new();
    private readonly CheckBox _rcon = new();
    private readonly TextBox _rconPassword = new();
    private readonly TextBox _motd = new();
    private readonly Label _status = new();

    public ServerOptions Result { get; private set; }

    public CreateServerForm(ServerOptions current)
    {
        Result = new ServerOptions
        {
            Type = current.Type,
            Size = current.Size,
            Version = current.Version,
            MaxPlayers = current.MaxPlayers,
            AllowAll = current.AllowAll,
            Whitelist = current.Whitelist,
            Rcon = current.Rcon,
            RconPassword = current.RconPassword,
            Motd = current.Motd,
        };

        Text = "Создать сервер";
        ClientSize = new Size(470, 396);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9f);

        int y = 14;
        AddLabel("Тип сервера", y);
        _type.Bounds = new Rectangle(190, y - 3, 260, 24);
        _type.DropDownStyle = ComboBoxStyle.DropDownList;
        _type.Items.Add("MemPaint — рисунок блоками");
        _type.SelectedIndex = 0;
        Controls.Add(_type);
        y += 34;

        AddLabel("Размер мира, блоков", y);
        _size.Bounds = new Rectangle(190, y - 3, 260, 24);
        _size.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (int size in Sizes) _size.Items.Add(size + " × " + size);
        _size.SelectedIndex = Array.IndexOf(Sizes, Sizes.Contains(Result.Size) ? Result.Size : 10);
        _size.SelectedIndexChanged += (_, _) => UpdateMotdHint();
        Controls.Add(_size);
        y += 34;

        AddLabel("Версия Paper", y);
        _version.Bounds = new Rectangle(190, y - 3, 260, 24);
        _version.DropDownStyle = ComboBoxStyle.DropDownList;
        _version.Items.Add(Result.Version);
        _version.SelectedIndex = 0;
        Controls.Add(_version);
        y += 34;

        AddLabel("Максимум игроков", y);
        _players.Bounds = new Rectangle(190, y - 3, 80, 24);
        _players.Minimum = 1;
        _players.Maximum = 200;
        _players.Value = Math.Clamp(Result.MaxPlayers, 1, 200);
        Controls.Add(_players);
        y += 34;

        _allowAll.Text = "Пускать всех (без проверки лицензии)";
        _allowAll.Bounds = new Rectangle(18, y, 420, 22);
        _allowAll.Checked = Result.AllowAll;
        Controls.Add(_allowAll);
        y += 28;

        _whitelist.Text = "Whitelist — заходить только из списка";
        _whitelist.Bounds = new Rectangle(18, y, 420, 22);
        _whitelist.Checked = Result.Whitelist;
        Controls.Add(_whitelist);
        y += 28;

        _rcon.Text = "RCON";
        _rcon.Bounds = new Rectangle(18, y, 70, 22);
        _rcon.Checked = Result.Rcon;
        _rcon.CheckedChanged += (_, _) => _rconPassword.Enabled = _rcon.Checked;
        Controls.Add(_rcon);
        _rconPassword.Bounds = new Rectangle(190, y - 2, 260, 24);
        _rconPassword.Text = Result.RconPassword;
        _rconPassword.Enabled = Result.Rcon;
        Controls.Add(_rconPassword);
        y += 34;

        AddLabel("Описание сервера", y);
        _motd.Bounds = new Rectangle(190, y - 3, 260, 24);
        _motd.Text = Result.Motd;
        _motd.MaxLength = 60;
        Controls.Add(_motd);
        y += 36;

        _status.Bounds = new Rectangle(18, y, 436, 34);
        _status.ForeColor = Color.DimGray;
        _status.Text = "Версии загружаются…";
        Controls.Add(_status);

        var ok = new Button { Text = "Создать", Bounds = new Rectangle(250, 344, 100, 34) };
        ok.Click += (_, _) => Apply();
        var cancel = new Button { Text = "Отмена", Bounds = new Rectangle(358, 344, 100, 34), DialogResult = DialogResult.Cancel };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        Load += async (_, _) => await LoadVersionsAsync();
    }

    private void AddLabel(string text, int y)
    {
        Controls.Add(new Label { Text = text, Bounds = new Rectangle(18, y + 2, 165, 20) });
    }

    private int SelectedSize => Sizes[Math.Max(0, _size.SelectedIndex)];

    private void UpdateMotdHint()
    {
        _status.Text = "Мир " + SelectedSize + "×" + SelectedSize + " блоков: рисунок на холсте станет миром.";
    }

    private async Task LoadVersionsAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var json = await http.GetStringAsync("https://fill.papermc.io/v3/projects/paper");
            using var document = JsonDocument.Parse(json);
            var versions = new List<string>();
            foreach (var group in document.RootElement.GetProperty("versions").EnumerateObject())
            {
                foreach (var version in group.Value.EnumerateArray())
                {
                    var name = version.GetString();
                    if (name == null || name.Contains('-')) continue;
                    versions.Add(name);
                }
            }
            versions.Sort(CompareVersions);
            versions.Reverse();
            _version.BeginUpdate();
            _version.Items.Clear();
            foreach (var version in versions) _version.Items.Add(version);
            if (_version.Items.Contains(Result.Version)) _version.SelectedItem = Result.Version;
            else if (_version.Items.Count > 0) _version.SelectedIndex = 0;
            _version.EndUpdate();
            _status.Text = "Доступно версий Paper: " + versions.Count;
        }
        catch (Exception ex)
        {
            _status.Text = "Не удалось получить список версий: " + ex.Message;
        }
    }

    private static int CompareVersions(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length && int.TryParse(a[i], out var parsedA) ? parsedA : 0;
            int y = i < b.Length && int.TryParse(b[i], out var parsedB) ? parsedB : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    private void Apply()
    {
        Result.Type = "MemPaint";
        Result.Size = SelectedSize;
        Result.Version = _version.SelectedItem as string ?? Result.Version;
        Result.MaxPlayers = (int)_players.Value;
        Result.AllowAll = _allowAll.Checked;
        Result.Whitelist = _whitelist.Checked;
        Result.Rcon = _rcon.Checked;
        Result.RconPassword = string.IsNullOrWhiteSpace(_rconPassword.Text) ? "memhost" : _rconPassword.Text.Trim();
        Result.Motd = string.IsNullOrWhiteSpace(_motd.Text) ? "MemPaint" : _motd.Text.Trim();
        DialogResult = DialogResult.OK;
        Close();
    }
}
