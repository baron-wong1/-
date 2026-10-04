using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace InvoiceAssistant
{
    public sealed class MainForm : Form
    {
        internal Session Current;
        readonly TextBox person = new TextBox { Width = 130, MaxLength = 80 };
        readonly TextBox month = new TextBox { Width = 105, MaxLength = 7 };
        internal readonly DataGridView Grid = new DataGridView();
        readonly Label summary = new Label { AutoSize = true, Padding = new Padding(0, 10, 0, 0) };
        readonly Label progress = new Label { AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(0, 8, 0, 0) };
        readonly Button fileButton = Button("选择文件"), folderButton = Button("选择文件夹"), reviewButton = Button("核对所选"), splitButton = Button("复制拆分"), removeButton = Button("移除"), clearButton = Button("清空"), exportButton = Button("生成打印件和统计表"), cancelButton = Button("取消");
        readonly System.Windows.Forms.Timer saveTimer = new System.Windows.Forms.Timer { Interval = 500 };
        readonly Importer importer;
        CancellationTokenSource operation; bool busy, saveFailed;
        internal string TestDestination;
        public MainForm(Session session = null, string cache = null)
        {
            Text = "发票整理助手"; MinimumSize = new Size(900, 620); Size = new Size(1180, 760); StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 10); BackColor = Color.White; AutoScaleMode = AutoScaleMode.Dpi; AllowDrop = true;
            if (session != null) Current = session;
            else
            {
                try { Current = Session.Load(); }
                catch { Current = new Session(); Shown += (_, __) => MessageBox.Show(this, "上次的记录文件无法读取。原件仍在原位置，请重新导入。", "恢复记录", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            }
            importer = new Importer(cache ?? Path.Combine(Session.Root, "cache"));
            person.Text = Current.Person; month.Text = Current.Month;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 7 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.Controls.Add(new Label { Text = "发票整理助手", Font = new Font(Font.FontFamily, 21, FontStyle.Bold), AutoSize = true }, 0, 0);
            var toolbar = Flow(); toolbar.Controls.AddRange(new Control[] { fileButton, folderButton, Label("报销月份"), month, Label("报销人"), person }); layout.Controls.Add(toolbar, 0, 1);
            var drop = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(246, 248, 250), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, 16) };
            drop.Controls.Add(new Label { Text = "拖入票据或文件夹　·　PDF / 图片 / Word", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(65, 77, 91) }); layout.Controls.Add(drop, 0, 2);
            Grid.Dock = DockStyle.Fill; Grid.BackgroundColor = Color.White; Grid.BorderStyle = BorderStyle.FixedSingle; Grid.ReadOnly = true; Grid.AllowUserToAddRows = false; Grid.AllowUserToDeleteRows = false; Grid.RowHeadersVisible = false;
            Grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; Grid.MultiSelect = true; Grid.AutoGenerateColumns = false; Grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; Grid.AllowUserToResizeRows = false;
            Grid.RowTemplate.Height = 36; Grid.ColumnHeadersHeight = 38; Grid.EnableHeadersVisualStyles = false; Grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(246, 248, 250); Grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(231, 239, 250); Grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            Column("文件", "Filename", 25); Column("页", "PageLabel", 5); Column("类别", "Category", 9); Column("类型", "Kind", 6); Column("发票号码", "Number", 22); Column("开票日期", "Date", 12); Column("金额", "Amount", 9); Column("状态", "Status", 10);
            layout.Controls.Add(Grid, 0, 3);
            var actions = Flow(); actions.Controls.AddRange(new Control[] { reviewButton, splitButton, removeButton, clearButton }); layout.Controls.Add(actions, 0, 4);
            layout.Controls.Add(progress, 0, 5);
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 }; bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 245));
            bottom.Controls.Add(summary, 0, 0); bottom.Controls.Add(cancelButton, 1, 0); bottom.Controls.Add(exportButton, 2, 0); exportButton.Dock = DockStyle.Fill; exportButton.BackColor = Color.FromArgb(37, 99, 181); exportButton.ForeColor = Color.White; exportButton.FlatStyle = FlatStyle.Flat; cancelButton.Visible = false;
            layout.Controls.Add(bottom, 0, 6); Controls.Add(layout);
            fileButton.Click += async (_, __) => { using (var dialog = new OpenFileDialog { Multiselect = true, Filter = "票据|*.pdf;*.jpg;*.jpeg;*.png;*.doc;*.docx", Title = "选择票据" }) if (dialog.ShowDialog(this) == DialogResult.OK) await ImportPaths(dialog.FileNames); };
            folderButton.Click += async (_, __) => { using (var dialog = new FolderBrowserDialog { Description = "选择包含票据的文件夹" }) if (dialog.ShowDialog(this) == DialogResult.OK) await ImportPaths(new[] { dialog.SelectedPath }); };
            DragEnter += (_, e) => e.Effect = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            DragDrop += async (_, e) => { if (!busy && e.Data.GetDataPresent(DataFormats.FileDrop)) await ImportPaths((string[])e.Data.GetData(DataFormats.FileDrop)); };
            Grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ReviewSelected(); }; reviewButton.Click += (_, __) => ReviewSelected(); splitButton.Click += (_, __) => SplitSelected();
            removeButton.Click += (_, __) => RemoveSelected();
            clearButton.Click += (_, __) => { if (MessageBox.Show(this, "清空本次列表和保存的记录？原文件不会删除。", "清空列表", MessageBoxButtons.OKCancel) == DialogResult.OK) { Current.Rows.Clear(); RefreshRows(); Save(); } };
            exportButton.Click += async (_, __) => await ExportFromButton(); cancelButton.Click += (_, __) => operation?.Cancel();
            person.TextChanged += (_, __) => ScheduleSave(); month.TextChanged += (_, __) => ScheduleSave();
            saveTimer.Tick += (_, __) => { saveTimer.Stop(); Save(); };
            FormClosing += (_, e) => { if (busy) { operation.Cancel(); e.Cancel = true; progress.Text = "正在取消，完成后可关闭窗口"; } else { saveTimer.Stop(); Save(); } };
            FormClosed += (_, __) => { saveTimer.Dispose(); operation?.Dispose(); };
            RefreshRows(); progress.Text = "扫描票据使用系统已安装的中文识别；Word 转换需要本机 Microsoft Word";
        }
        static FlowLayoutPanel Flow() => new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 8, 0, 0), Margin = new Padding(0) };
        static Label Label(string text) => new Label { Text = text, AutoSize = true, Padding = new Padding(12, 4, 6, 0) };
        internal static Button Button(string text) => new Button { Text = text, AutoSize = true, Height = 34, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(0, 0, 8, 0) };
        void Column(string header, string property, int weight) => Grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = header, DataPropertyName = property, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
        internal void RefreshRows()
        {
            var ids = Grid.SelectedRows.Cast<DataGridViewRow>().Where(r => r.DataBoundItem is Ticket).Select(r => ((Ticket)r.DataBoundItem).Id).ToHashSet();
            Rules.Reconcile(Current.Rows); Grid.DataSource = null; Grid.DataSource = Current.Rows.ToArray(); Grid.ClearSelection();
            foreach (DataGridViewRow row in Grid.Rows)
            {
                var item = (Ticket)row.DataBoundItem; row.Selected = ids.Contains(item.Id); row.Cells[row.Cells.Count - 1].ToolTipText = item.Issues;
                if (item.Status == "待核对" || item.Status == "冲突" || item.Status == "失败") row.Cells[row.Cells.Count - 1].Style.ForeColor = Color.FromArgb(174, 82, 24);
                if (item.Status == "重复") row.DefaultCellStyle.ForeColor = Color.Gray;
            }
            var ready = Current.Rows.Where(r => r.Status == "就绪" && !r.Attachment).ToArray();
            summary.Text = ready.Length + " 张就绪　¥ " + ready.Sum(r => Rules.Money(r.Amount).Value).ToString("N2") + "　" + Current.Rows.Count(r => r.Status == "待核对" || r.Status == "冲突" || r.Status == "失败") + " 项待处理";
        }
        internal void SetInfo(string name, string date) { person.Text = name; month.Text = date; }
        void ScheduleSave() { saveTimer.Stop(); saveTimer.Start(); }
        void Save()
        {
            Current.Person = person.Text; Current.Month = month.Text;
            // Test sessions never overwrite the user's saved data.
            if (TestDestination != null) return;
            try { Current.Save(); saveFailed = false; }
            catch (Exception ex) { progress.Text = "记录保存失败：" + ex.Message; if (!saveFailed) { saveFailed = true; MessageBox.Show(this, "记录无法保存，关闭前请导出或检查磁盘空间。", "保存记录", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
        }
        void SetBusy(bool value)
        {
            busy = value; foreach (var b in new Control[] { fileButton, folderButton, reviewButton, splitButton, removeButton, clearButton, exportButton, person, month, Grid }) b.Enabled = !value;
            cancelButton.Visible = value; UseWaitCursor = value;
            if (value) { operation?.Dispose(); operation = new CancellationTokenSource(); }
        }
        internal async Task ImportPaths(IEnumerable<string> paths)
        {
            if (busy) return; SetBusy(true); int added = 0, duplicate = 0;
            try
            {
                var files = await Task.Run(() => importer.Discover(paths, Current.Outputs, operation.Token).ToArray(), operation.Token);
                var known = new HashSet<string>(Current.Rows.Select(r => r.SourceHash).Where(h => h != ""));
                foreach (var file in files)
                {
                    operation.Token.ThrowIfCancellationRequested(); progress.Text = "正在读取 " + Path.GetFileName(file);
                    var hash = await Task.Run(() => Rules.Hash(file), operation.Token);
                    if (known.Contains(hash)) { duplicate++; continue; }
                    var rows = await Task.Run(() => importer.Read(file, operation.Token), operation.Token);
                    Current.Rows.AddRange(rows); known.Add(hash); added += rows.Count; RefreshRows(); Save();
                }
                progress.Text = added == 0 && duplicate == 0 ? "没有找到支持的票据文件" : "已加入 " + added + " 项，跳过 " + duplicate + " 个重复文件；双击行查看原件并核对";
            }
            catch (OperationCanceledException) { progress.Text = "已取消；已完成的票据保留在列表"; }
            catch (Exception ex) { ShowError(ex); }
            finally { SetBusy(false); }
        }
        internal void ReviewSelected()
        {
            if (busy || Grid.SelectedRows.Count == 0) return;
            var row = (Ticket)Grid.SelectedRows[0].DataBoundItem;
            if (row.Error != "") { MessageBox.Show(this, row.Error, "无法导入", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            using (var dialog = new ReviewForm(row)) if (dialog.ShowDialog(this) == DialogResult.OK) { Current.Rows[Current.Rows.IndexOf(row)] = dialog.Result; RefreshRows(); Save(); }
        }
        internal void SplitSelected()
        {
            if (busy || Grid.SelectedRows.Count == 0) return;
            var row = (Ticket)Grid.SelectedRows[0].DataBoundItem; if (row.Error != "") return;
            var copy = row.Copy(); copy.Id = Guid.NewGuid().ToString("N"); copy.Crop = null; copy.Number = ""; copy.Amount = ""; copy.Date = ""; copy.Reviewed = false; copy.KeepConflict = false; copy.RequiresSplit = true; copy.Warning = "请框选另一张票据并分别核对，裁剪区域不能重叠";
            Current.Rows.Insert(Current.Rows.IndexOf(row) + 1, copy); RefreshRows(); Save();
            foreach (DataGridViewRow r in Grid.Rows) r.Selected = ((Ticket)r.DataBoundItem).Id == copy.Id;
            ReviewSelected();
        }
        internal void RemoveSelected()
        {
            if (busy) return; foreach (var row in Grid.SelectedRows.Cast<DataGridViewRow>().Select(r => (Ticket)r.DataBoundItem).ToArray()) Current.Rows.Remove(row); RefreshRows(); Save();
        }
        internal async Task<string> ExportFromButton()
        {
            if (busy) return null; string destination = TestDestination;
            try { Rules.Prepare(Current.Rows); if (person.Text.Trim() == "" || !System.Text.RegularExpressions.Regex.IsMatch(month.Text, @"^20\d{2}-(0[1-9]|1[0-2])$")) throw new ArgumentException("请填写报销人和月份（如 2026-10）"); }
            catch (Exception ex) { if (TestDestination != null) throw; ShowError(ex); return null; }
            if (destination == null) using (var dialog = new FolderBrowserDialog { Description = "选择保存打印件与统计表的位置" }) { if (dialog.ShowDialog(this) != DialogResult.OK) return null; destination = dialog.SelectedPath; }
            SetBusy(true); progress.Text = "正在生成分类打印件和统计表";
            try
            {
                var rows = Current.Rows.Select(r => r.Copy()).ToList(); var name = person.Text; var date = month.Text;
                string output = await Task.Run(() => Exporter.Write(rows, name, date, destination, operation.Token), operation.Token);
                Current.Outputs.Add(output); Save(); progress.Text = "已生成：" + output;
                if (TestDestination == null && MessageBox.Show(this, "打印件与统计表已生成。打开输出文件夹？", "生成完成", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) Process.Start(new ProcessStartInfo(output) { UseShellExecute = true });
                return output;
            }
            catch (OperationCanceledException) { progress.Text = "已取消，未保留半成品输出"; return null; }
            catch (Exception ex) { if (TestDestination != null) throw; ShowError(ex); return null; }
            finally { SetBusy(false); }
        }
        void ShowError(Exception ex) { progress.Text = "操作未完成：" + ex.Message; MessageBox.Show(this, ex.Message, "请检查", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
