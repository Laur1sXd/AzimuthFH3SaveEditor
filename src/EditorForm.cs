using System.Data;
using System.Globalization;
using System.Text;

namespace AzimuthFH3;

public sealed class EditorForm : Form
{
    SaveSession? session;
    readonly DialogLocations dialogLocations = DialogLocations.Load();
    readonly TabControl tabs = new() { Dock = DockStyle.Fill, Padding = new Point(18, 9) };
    readonly EditorGrid dbGrid = Grid(), profileGrid = Grid(), stateGrid = Grid();
    readonly ListBox tables = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false };
    readonly TextBox profileFilter = Filter("Filter by name, e.g. Credits, XP, NumSkillTokens"), stateFilter = Filter("Filter by object, property, or value");
    readonly NumericUpDown flags = new() { Maximum = uint.MaxValue, Width = 230, ThousandsSeparator = true, Enabled = false };
    readonly Label location = new() { AutoEllipsis = true, ForeColor = Color.FromArgb(76, 91, 107), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Text = "Open a save file or save folder from the File menu." };
    readonly ToolStripStatusLabel status = new("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolStripMenuItem newRevision = new("Save as &new revision"), exportRevision = new("Export &revision to new folder…"), exportDb = new("Export PlayerDatabase as &SQLite…");
    readonly ToolStripMenuItem saveAll = new("&Save all"), saveAs = new("Save &as…"), saveAllTo = new("Save all &to folder…"), closeFile = new("&Close save");
    readonly ToolStripButton saveButton = new("Save all"), exportButton = new("Save as…");
    readonly Panel body = new() { Dock = DockStyle.Fill, Padding = new Padding(16, 8, 16, 12) };
    readonly MenuStrip menu = new();
    readonly ToolStrip toolbar = new() { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(12, 6, 12, 6), BackColor = Color.White, AutoSize = true };
    readonly Label versionInfo = new() { AutoSize = true, MaximumSize = new Size(850, 0) };
    bool binding, busy;
    string? selectedTable;
    static readonly Color Accent = Color.FromArgb(0, 110, 132);
    static EditorGrid Grid() => new()
    {
        Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false,
        EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2, EnableHeadersVisualStyles = false,
        ColumnHeadersHeight = 36, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        RowTemplate = { Height = 29 }, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        GridColor = Color.FromArgb(232, 237, 241), ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
    };
    static TextBox Filter(string hint) => new() { Dock = DockStyle.Top, PlaceholderText = hint, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, 8) };
    public EditorForm(string? initial)
    {
        Text = "Azimuth FH3 Save Editor"; ClientSize = new Size(1180, 760); MinimumSize = new Size(880, 580);
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(246, 248, 250);
        var file = new ToolStripMenuItem("&File");
        var open = new ToolStripMenuItem("Open save &folder…", null, async (_, _) => await ChooseFolder()) { ShortcutKeys = Keys.Control | Keys.O };
        var single = new ToolStripMenuItem("&Open single file…", null, async (_, _) => await ChooseFile()) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.O };
        saveAll.ShortcutKeys = Keys.Control | Keys.S; saveAs.ShortcutKeys = Keys.F12;
        saveAll.Click += async (_, _) => await SaveCurrent(); saveAs.Click += async (_, _) => await SaveAs(); saveAllTo.Click += async (_, _) => await SaveFolderAs(); closeFile.Click += (_, _) => CloseSave();
        newRevision.Click += async (_, _) => await SaveNew(); exportRevision.Click += async (_, _) => await ExportRevision(); exportDb.Click += (_, _) => ExportDatabase();
        file.DropDownItems.AddRange([open, single, new ToolStripSeparator(), saveAll, saveAs, saveAllTo, newRevision, exportRevision, new ToolStripSeparator(), exportDb, closeFile]);
        var help = new ToolStripMenuItem("&Help"); help.DropDownItems.Add("About", null, (_, _) => MessageBox.Show(this,
            "Azimuth FH3 Save Editor 1.0\n\nOpen individual FH3 ProfileData or VersionFlags files, packed decrypted profiles, SQLite/PlayerDatabase files, or BXML SaveState files. Filenames and save locations may be changed. File and folder pickers remember their locations between launches.\n\nSave all writes back to the loaded files and updates an existing Azimuth commit. Replaced files are backed up automatically. Close the game before saving.\n\nFH3 encryption runs inside this editor. No command windows or external crypto program.\n\nFormat research: Doliman100 / ForzaTech; Nenkai / ForzaTools.\nFH3 TFIT constants: DVS / Doliman100 compatibility data.\nInterface reference: qiangqiang101 / AnthemSaveEditor.", "About", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.AddRange([file, help]); MainMenuStrip = menu;
        var titlePanel = new TableLayoutPanel { Dock = DockStyle.Top, Height = 84, ColumnCount = 1, Padding = new Padding(18, 10, 18, 5), BackColor = Color.White };
        titlePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 37)); titlePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        titlePanel.Controls.Add(new Label { Text = "AZIMUTH  /  FH3 SAVE EDITOR", Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 18), ForeColor = Accent, TextAlign = ContentAlignment.MiddleLeft });
        titlePanel.Controls.Add(location);
        var openButton = new ToolStripButton("Open save folder…"); openButton.Click += async (_, _) => await ChooseFolder();
        var singleButton = new ToolStripButton("Open single file…"); singleButton.Click += async (_, _) => await ChooseFile();
        saveButton.Click += async (_, _) => await SaveCurrent(); exportButton.Click += async (_, _) => await SaveAs();
        toolbar.Items.AddRange([singleButton, openButton, new ToolStripSeparator(), saveButton, exportButton]);
        var statusBar = new StatusStrip { BackColor = Color.White, SizingGrip = true }; statusBar.Items.Add(status);
        body.Controls.Add(tabs);
        var dbTab = new TabPage("PlayerDatabase") { BackColor = Color.White, Padding = new Padding(10) };
        var splitter = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 8, FixedPanel = FixedPanel.Panel1, Panel1MinSize = 180 };
        splitter.Panel1.Controls.Add(tables); splitter.Panel1.Padding = new Padding(0, 5, 8, 0); splitter.Panel2.Controls.Add(dbGrid);
        dbTab.Controls.Add(splitter); tabs.TabPages.Add(dbTab);
        var profileTab = new TabPage("ProfileData") { BackColor = Color.White, Padding = new Padding(10) };
        profileTab.Controls.Add(profileGrid); profileTab.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10 }); profileTab.Controls.Add(profileFilter); tabs.TabPages.Add(profileTab);
        var stateTab = new TabPage("SaveState") { BackColor = Color.White, Padding = new Padding(10) };
        stateTab.Controls.Add(stateGrid); stateTab.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 10 }); stateTab.Controls.Add(stateFilter); tabs.TabPages.Add(stateTab);
        var versionTab = new TabPage("VersionFlags") { BackColor = Color.White, Padding = new Padding(22) };
        var versionPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        versionPanel.Controls.Add(new Label { Text = "Version flags (uint32)", AutoSize = true, Margin = new Padding(0, 8, 0, 10), Font = new Font("Segoe UI Semibold", 12) });
        versionPanel.Controls.Add(flags); versionPanel.Controls.Add(new Label { Text = "Encrypted files are decrypted for editing and encrypted again when saved.", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 14, 0, 24) });
        versionPanel.Controls.Add(versionInfo); versionTab.Controls.Add(versionPanel); tabs.TabPages.Add(versionTab);
        Controls.Add(body); Controls.Add(toolbar); Controls.Add(titlePanel); Controls.Add(menu); Controls.Add(statusBar);
        foreach (var grid in new[] { dbGrid, profileGrid, stateGrid })
        {
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(237, 243, 246); grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(40, 58, 73);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(216, 237, 243); grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            grid.DefaultCellStyle.FormatProvider = CultureInfo.InvariantCulture;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 252, 253);
            grid.DataError += (_, e) => { e.ThrowException = false; e.Cancel = true; status.Text = "Invalid cell value: " + e.Exception?.Message; };
            grid.CellBeginEdit += (_, e) => grid.Rows[e.RowIndex].ErrorText = "";
        }
        tables.SelectedIndexChanged += (_, _) => BindDatabase(); profileFilter.TextChanged += (_, _) => BindProfile(); stateFilter.TextChanged += (_, _) => BindState();
        profileGrid.CellValidating += ValidateProfile; stateGrid.CellValidating += ValidateState; dbGrid.CellValidating += ValidateDatabase;
        flags.ValueChanged += (_, _) => { if (!binding && session != null) { session.VersionFlags = (uint)flags.Value; session.Dirty = true; UpdateStatus(); } };
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; return; } if (!ConfirmDiscard()) { e.Cancel = true; return; } session?.Dispose(); };
        Shown += async (_, _) => { splitter.SplitterDistance = 255; if (initial != null) await Open(initial); };
        UpdateActions();
    }
    bool ConfirmDiscard() => session?.Dirty != true || MessageBox.Show(this, "Discard your unsaved edits?", "Unsaved changes", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    void UpdateActions()
    {
        newRevision.Enabled = !busy && session?.CanSaveRevision == true;
        saveAll.Enabled = saveAs.Enabled = saveAllTo.Enabled = closeFile.Enabled = exportRevision.Enabled = saveButton.Enabled = exportButton.Enabled = !busy && session != null;
        exportDb.Enabled = !busy && session?.HasDatabase == true;
    }
    void UpdateStatus()
    {
        if (session == null) return;
        Text = "Azimuth FH3 Save Editor" + (session.Dirty ? " *" : "") + " — " + session.DisplayName;
        var counts = new List<string>();
        if (session.HasProfile) counts.Add(session.Profile.Values.Count + " profile values");
        if (session.HasState) counts.Add(session.State.Properties.Count + " state properties");
        if (session.HasDatabase) counts.Add(session.Tables.Count + " tables");
        if (session.HasFlags) counts.Add("VersionFlags");
        status.Text = (session.Dirty ? "Unsaved edits · " : "") + session.ManifestStatus + " · " + string.Join(" · ", counts);
    }
    async Task RunBusy(string message, Func<Task> action)
    {
        if (busy) return; busy = true; body.Enabled = menu.Enabled = toolbar.Enabled = false; UseWaitCursor = true; status.Text = message; UpdateActions();
        try { await action(); }
        catch (Exception ex) { status.Text = "Operation failed: " + ex.Message; MessageBox.Show(this, ex.Message, "Azimuth FH3 Save Editor", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { busy = false; body.Enabled = menu.Enabled = toolbar.Enabled = true; UseWaitCursor = false; UpdateActions(); }
    }
    async Task Open(string folder)
    {
        if (!ConfirmDiscard()) return;
        await RunBusy("Decrypting and verifying the FH3 save locally…", async () =>
        {
            var next = await Task.Run(() => new SaveSession(folder)); ReplaceSession(next);
        });
    }
    void ReplaceSession(SaveSession next)
    {
        var old = session; session = next; old?.Dispose(); binding = true;
        try
        {
            profileFilter.Clear(); stateFilter.Clear(); selectedTable = null; dbGrid.DataSource = profileGrid.DataSource = stateGrid.DataSource = null;
            tables.DataSource = session.Tables; flags.Value = session.HasFlags ? session.VersionFlags : 0; flags.Enabled = session.HasFlags;
            location.Text = session.SourceFile ?? session.DataFolder;
            versionInfo.Text = session.ManifestStatus + "\n\n" + (session.HasFlags ? "VersionFlags loaded." : "No VersionFlags file is loaded.") + "\n\nSave all writes to the loaded files.\nSave as lets you choose another file or folder.\nReplaced files are backed up automatically.";
        }
        finally { binding = false; }
        BindProfile(); BindState(); BindDatabase();
        tabs.SelectedIndex = session.HasProfile ? 1 : session.HasDatabase ? 0 : session.HasState ? 2 : 3;
        UpdateStatus();
    }
    void CloseSave()
    {
        if (!ConfirmDiscard()) return;
        binding = true;
        try
        {
            session?.Dispose(); session = null; selectedTable = null; tables.DataSource = null;
            dbGrid.DataSource = profileGrid.DataSource = stateGrid.DataSource = null;
            profileFilter.Clear(); stateFilter.Clear(); flags.Value = 0; flags.Enabled = false; versionInfo.Text = "";
            Text = "Azimuth FH3 Save Editor"; location.Text = "Open a save file or save folder from the File menu."; status.Text = "Ready";
        }
        finally { binding = false; }
        UpdateActions();
    }
    async Task ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Select Azimuth's titles folder, an FH3 revision, or its data folder.", UseDescriptionForTitle = true, InitialDirectory = dialogLocations.StartFolder(false, false, session?.DataFolder) };
        if (dialog.ShowDialog(this) == DialogResult.OK) { dialogLocations.Remember(dialog.SelectedPath, false, false); await Open(dialog.SelectedPath); }
    }
    async Task ChooseFile()
    {
        using var dialog = new OpenFileDialog { Title = "Open a save file", Filter = "Save files|ProfileData;VersionFlags;PlayerDatabase;SaveState;*.ProfileData;*.VersionFlags;*.sqlite;*.db;*.bin|All files|*.*", InitialDirectory = dialogLocations.StartFolder(false, true, session?.DataFolder) };
        if (dialog.ShowDialog(this) == DialogResult.OK) { dialogLocations.Remember(dialog.FileName, false, true); await Open(dialog.FileName); }
    }
    void BindProfile()
    {
        if (binding || session?.HasProfile != true) return;
        binding = true;
        try
        {
            var dt = new DataTable(); dt.Columns.Add("_id", typeof(int)); dt.Columns.Add("Name"); dt.Columns.Add("Type"); dt.Columns.Add("Value");
            for (int i = 0; i < session.Profile.Values.Count; i++) { var v = session.Profile.Values[i]; if (v.Name.Contains(profileFilter.Text, StringComparison.OrdinalIgnoreCase)) dt.Rows.Add(i, v.Name, v.Type, v.Value); }
            profileGrid.DataSource = dt; profileGrid.Columns["_id"]!.Visible = false; profileGrid.Columns["Name"]!.ReadOnly = profileGrid.Columns["Type"]!.ReadOnly = true;
            profileGrid.SetColumnWeights(55, 18, 27);
        }
        finally { binding = false; }
    }
    void BindState()
    {
        if (binding || session?.HasState != true) return; binding = true;
        try
        {
            var dt = new DataTable(); dt.Columns.Add("_id", typeof(int)); dt.Columns.Add("Object"); dt.Columns.Add("Property"); dt.Columns.Add("Value");
            for (int i = 0; i < session.State.Properties.Count; i++) { var p = session.State.Properties[i]; if ((p.Object + " " + p.Property + " " + p.Value).Contains(stateFilter.Text, StringComparison.OrdinalIgnoreCase)) dt.Rows.Add(i, p.Object, p.Property, p.Value); }
            stateGrid.DataSource = dt; stateGrid.Columns["_id"]!.Visible = false; stateGrid.Columns["Object"]!.ReadOnly = stateGrid.Columns["Property"]!.ReadOnly = true;
            stateGrid.SetColumnWeights(43, 32, 25);
        }
        finally { binding = false; }
    }
    void BindDatabase()
    {
        if (binding || session?.HasDatabase != true || tables.SelectedItem is not string table) return;
        binding = true;
        try
        {
            dbGrid.SetColumnWeights();
            var dt = session.ReadTable(table); selectedTable = table; dbGrid.DataSource = dt;
            bool hasRow = dt.Columns.Contains("__az_rowid"); dbGrid.ReadOnly = !hasRow;
            if (hasRow) dbGrid.Columns["__az_rowid"]!.Visible = false;
            foreach (DataGridViewColumn c in dbGrid.Columns) { c.MinimumWidth = 110; c.Width = 150; if (dt.Columns[c.Name]!.DataType == typeof(byte[])) c.ReadOnly = true; }
            if (dt.Columns.Count <= 7) dbGrid.SetColumnWeights(dbGrid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).Select(_ => 1).ToArray());
            status.Text = table + " · " + dt.Rows.Count + " rows" + (!hasRow ? " · read only (no rowid)" : " · double-click a value or press F2 to edit");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cannot load table"); }
        finally { binding = false; }
    }
    void ValidateProfile(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (binding || session == null || e.RowIndex < 0 || profileGrid.Columns[e.ColumnIndex].Name != "Value") return;
        var row = (DataRowView)profileGrid.Rows[e.RowIndex].DataBoundItem!; var leaf = session.Profile.Values[(int)row["_id"]]; string value = Convert.ToString(e.FormattedValue) ?? "";
        if (value == leaf.Value) return;
        try { leaf.Set(value); session.Dirty = true; UpdateStatus(); }
        catch (Exception ex) { e.Cancel = true; profileGrid.Rows[e.RowIndex].ErrorText = ex.Message; status.Text = ex.Message; }
    }
    void ValidateState(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (binding || session == null || e.RowIndex < 0 || stateGrid.Columns[e.ColumnIndex].Name != "Value") return;
        string value = Convert.ToString(e.FormattedValue) ?? "";
        if (Encoding.UTF8.GetByteCount(value) > ushort.MaxValue) { e.Cancel = true; status.Text = "SaveState value is too long."; return; }
        var row = (DataRowView)stateGrid.Rows[e.RowIndex].DataBoundItem!; var property = session.State.Properties[(int)row["_id"]];
        if (property.Value != value) { property.Value = value; session.Dirty = true; UpdateStatus(); }
    }
    void ValidateDatabase(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (binding || session == null || selectedTable == null || e.RowIndex < 0 || !dbGrid.IsCurrentCellDirty) return;
        var row = (DataRowView)dbGrid.Rows[e.RowIndex].DataBoundItem!; string column = dbGrid.Columns[e.ColumnIndex].Name;
        try
        {
            var type = row.DataView.Table!.Columns[column]!.DataType;
            string text = Convert.ToString(e.FormattedValue, CultureInfo.InvariantCulture) ?? "";
            object? value = type == typeof(long) ? long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)
                : type == typeof(double) ? double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)
                : type == typeof(string) || type == typeof(object) ? text : Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
            if (value is double number && !double.IsFinite(number)) throw new FormatException("Enter a finite number using a decimal point.");
            session.UpdateCell(selectedTable, Convert.ToInt64(row["__az_rowid"], CultureInfo.InvariantCulture), column, value); UpdateStatus();
        }
        catch (Exception ex) { e.Cancel = true; dbGrid.Rows[e.RowIndex].ErrorText = ex.Message; status.Text = ex.Message; }
    }
    bool EndEdit()
    {
        if (!Validate()) return false;
        foreach (var grid in new[] { dbGrid, profileGrid, stateGrid }) { if (!grid.EndEdit()) return false; if (grid.DataSource != null) ((CurrencyManager)BindingContext![grid.DataSource]!).EndCurrentEdit(); }
        return true;
    }
    async Task SaveCurrent()
    {
        if (session == null || !EndEdit()) return;
        await RunBusy("Saving and verifying the loaded save files…", async () =>
        {
            string backup = await Task.Run(session.SaveAll); UpdateStatus();
            status.Text = "Saved: " + (session.SourceFile ?? session.DataFolder) + (backup.Length > 0 ? " · Backup: " + backup : " · no changes");
        });
    }
    async Task SaveAs()
    {
        if (session == null || !EndEdit()) return;
        if (!session.IsSingleFile) { await SaveFolderAs(); return; }
        using var dialog = new SaveFileDialog { Title = "Save file as", Filter = "All files|*.*", FileName = Path.GetFileName(session.SourceFile!), InitialDirectory = dialogLocations.StartFolder(true, true, session.DataFolder), AddExtension = false, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string destination = dialog.FileName;
        dialogLocations.Remember(destination, true, true);
        await RunBusy("Saving and verifying the selected file…", async () =>
        {
            string backup = await Task.Run(() => session.SaveSingleAs(destination));
            var next = await Task.Run(() => new SaveSession(destination)); ReplaceSession(next);
            status.Text = "Saved: " + destination + (backup.Length > 0 ? " · Backup: " + backup : "");
        });
    }
    async Task SaveFolderAs()
    {
        if (session == null || !EndEdit()) return;
        using var dialog = new FolderBrowserDialog { Description = "Choose a folder for the save files. Existing files will be backed up before replacement.", UseDescriptionForTitle = true, InitialDirectory = dialogLocations.StartFolder(true, false, session.DataFolder) };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        dialogLocations.Remember(dialog.SelectedPath, true, false);
        await RunBusy("Saving and verifying all files to the selected folder…", async () =>
        {
            string backup = await Task.Run(() => session.SaveAllTo(dialog.SelectedPath));
            string reopen = session.IsSingleFile ? Path.Combine(dialog.SelectedPath, Path.GetFileName(session.SourceFile!)) : dialog.SelectedPath;
            var next = await Task.Run(() => new SaveSession(reopen)); ReplaceSession(next);
            status.Text = "Saved to: " + dialog.SelectedPath + (backup.Length > 0 ? " · Backup: " + backup : "");
        });
    }
    async Task SaveNew()
    {
        if (session == null || !EndEdit()) return;
        await RunBusy("Re-encrypting, verifying, and writing a new revision…", async () =>
        {
            string result = await Task.Run(session.SaveNewRevision); session.Dirty = false; UpdateStatus();
            status.Text = "Saved and verified new revision: " + result;
            MessageBox.Show(this, "Saved and verified:\n" + result + "\n\nThe loaded revision was kept. Start FH3 to test the edited save.", "New revision saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }
    async Task ExportRevision()
    {
        if (session == null || !EndEdit()) return;
        using var dialog = new FolderBrowserDialog { Description = "Choose a parent folder. A new FH3-Edited timestamp folder will be created inside it.", UseDescriptionForTitle = true, InitialDirectory = dialogLocations.StartFolder(true, false, session.DataFolder) };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        dialogLocations.Remember(dialog.SelectedPath, true, false);
        string target = Path.Combine(dialog.SelectedPath, "FH3-Edited-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        await RunBusy("Re-encrypting and verifying the exported revision…", async () => { string saved = await Task.Run(() => session.SaveToNewFolder(target)); status.Text = "Exported and verified: " + saved; MessageBox.Show(this, "Exported a complete revision:\n" + saved, "Revision exported"); });
    }
    void ExportDatabase()
    {
        if (session == null || !EndEdit()) return;
        using var dialog = new SaveFileDialog { Title = "Export PlayerDatabase", Filter = "SQLite database|*.sqlite", FileName = "FH3-PlayerDatabase.sqlite", InitialDirectory = dialogLocations.StartFolder(true, true, session.DataFolder) };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        dialogLocations.Remember(dialog.FileName, true, true);
        try { File.WriteAllBytes(dialog.FileName, session.DatabaseBytes()); status.Text = "Exported SQLite: " + dialog.FileName; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export failed"); }
    }
}
