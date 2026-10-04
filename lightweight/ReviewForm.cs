using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace InvoiceAssistant
{
    public sealed class ReviewForm : Form
    {
        public Ticket Result { get; private set; }
        readonly Ticket row;
        readonly ComboBox category = new ComboBox { Dock = DockStyle.Fill, MaxLength = 70 };
        readonly ComboBox kind = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        readonly TextBox number = new TextBox { Dock = DockStyle.Fill, MaxLength = 30 }, date = new TextBox { Dock = DockStyle.Fill }, amount = new TextBox { Dock = DockStyle.Fill };
        readonly NumericUpDown start = new NumericUpDown { Minimum = 1 }, end = new NumericUpDown { Minimum = 1 };
        readonly CheckBox keep = new CheckBox { Text = "已核对，同号字段冲突仍保留此项", AutoSize = true };
        readonly CheckBox confirmed = new CheckBox { Text = "已查看原件并核对字段与票据范围", AutoSize = true };
        readonly Label warning = new Label { AutoSize = true, MaximumSize = new Size(300, 0), ForeColor = Color.FromArgb(154, 75, 23) };
        internal readonly CropView Preview = new CropView { Dock = DockStyle.Fill, BackColor = Color.FromArgb(235, 238, 242) };
        readonly CancellationTokenSource lifetime = new CancellationTokenSource(); CancellationTokenSource rendering; int generation;
        readonly Button recognize = MainForm.Button("重新识别框选区域"), save = MainForm.Button("确认并保存");
        bool loading;
        public ReviewForm(Ticket original)
        {
            row = original.Copy(); Text = "核对 · " + row.Filename; Font = new Font("Microsoft YaHei UI", 10); MinimumSize = new Size(850, 640); Size = new Size(1100, 800); StartPosition = FormStartPosition.CenterParent; BackColor = Color.White;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2 }; layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var fields = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 0, 12, 0) };
            kind.Items.AddRange(new[] { "发票", "付款附件（不计金额）" }); kind.SelectedIndex = row.Attachment ? 1 : 0;
            category.Items.AddRange(Rules.Categories); category.Text = row.Category; number.Text = row.Number; date.Text = row.Date; amount.Text = row.Amount;
            foreach (var pair in new[] { ("类型", (Control)kind), ("类别（可输入自定义类别）", category), ("发票号码", number), ("开票日期（yyyy-MM-dd）", date), ("金额", amount) })
            {
                var panel = new TableLayoutPanel { Width = 300, Height = 64, RowCount = 2 }; panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); panel.Controls.Add(new Label { Text = pair.Item1, AutoSize = true }, 0, 0); panel.Controls.Add(pair.Item2, 0, 1); fields.Controls.Add(panel);
            }
            start.Maximum = end.Maximum = row.Pages; start.Value = row.Start; end.Value = row.End;
            var range = new FlowLayoutPanel { Width = 300, Height = 52 }; range.Controls.AddRange(new Control[] { new Label { Text = "页范围", AutoSize = true }, start, new Label { Text = "至", AutoSize = true }, end }); start.Width = end.Width = 70; fields.Controls.Add(range);
            fields.Controls.Add(new Label { Text = "在右侧拖动框选一张票据。无需裁剪时保留完整页；多票请在主界面复制拆分。", MaximumSize = new Size(300, 0), AutoSize = true });
            var reset = MainForm.Button("恢复完整页"); fields.Controls.Add(reset); fields.Controls.Add(recognize); fields.Controls.Add(keep); keep.Checked = row.KeepConflict; fields.Controls.Add(confirmed); confirmed.Checked = row.Reviewed; fields.Controls.Add(warning); warning.Text = row.Warning;
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 }; right.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); right.Controls.Add(Preview, 0, 0);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) }; var cancel = MainForm.Button("取消"); cancel.DialogResult = DialogResult.Cancel; buttons.Controls.AddRange(new Control[] { save, cancel }); right.Controls.Add(buttons, 0, 1);
            layout.Controls.Add(fields, 0, 0); layout.Controls.Add(right, 1, 0); Controls.Add(layout); CancelButton = cancel;
            Preview.Crop = row.Crop; Preview.Changed += (_, __) => confirmed.Checked = false;
            reset.Click += (_, __) => { Preview.Crop = null; Preview.Invalidate(); confirmed.Checked = false; };
            start.ValueChanged += async (_, __) => { if (end.Value < start.Value) end.Value = start.Value; confirmed.Checked = false; await LoadPreview(); };
            end.ValueChanged += (_, __) => confirmed.Checked = false;
            foreach (var control in new Control[] { number, date, amount, category, kind }) control.TextChanged += (_, __) => { if (!loading) confirmed.Checked = false; };
            recognize.Click += async (_, __) => await Recognize(); save.Click += (_, __) => Save(); Shown += async (_, __) => await LoadPreview();
            FormClosed += (_, __) => { lifetime.Cancel(); rendering?.Cancel(); Preview.SetImage(null); };
        }
        async Task LoadPreview()
        {
            int current = ++generation; rendering?.Cancel(); rendering = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var token = rendering.Token;
            try
            {
                Rules.CheckSource(row); var bitmap = await SystemServices.Render(row.Pdf, (int)start.Value, token);
                if (current != generation || IsDisposed) bitmap.Dispose(); else Preview.SetImage(bitmap);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!IsDisposed) warning.Text = "预览失败：" + ex.Message; }
        }
        void FillRow()
        {
            row.Category = category.Text.Trim(); row.Attachment = kind.SelectedIndex == 1; row.Number = number.Text.Trim(); row.Date = date.Text.Trim(); row.Amount = amount.Text.Trim(); row.Start = (int)start.Value; row.End = (int)end.Value; row.Crop = Preview.Crop; row.Reviewed = confirmed.Checked; row.KeepConflict = keep.Checked;
        }
        async Task Recognize()
        {
            FillRow(); recognize.Enabled = save.Enabled = false;
            try
            {
                await Task.Run(() => Importer.Recognize(row, lifetime.Token), lifetime.Token);
                if (IsDisposed) return;
                loading = true; number.Text = row.Number; date.Text = row.Date; amount.Text = row.Amount; category.Text = row.Category; kind.SelectedIndex = row.Attachment ? 1 : 0; warning.Text = row.Warning; confirmed.Checked = false; loading = false;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!IsDisposed) warning.Text = ex.Message; }
            finally { if (!IsDisposed) recognize.Enabled = save.Enabled = true; }
        }
        void Save()
        {
            FillRow(); var errors = Rules.Validate(row).ToArray();
            if (errors.Length > 0) { warning.Text = string.Join("；", errors); return; }
            Result = row; DialogResult = DialogResult.OK; Close();
        }
    }

    public sealed class CropView : Control
    {
        Bitmap image; PointF? anchor;
        public double[] Crop { get; set; }
        public event EventHandler Changed;
        public CropView() { DoubleBuffered = true; Cursor = Cursors.Cross; }
        public void SetImage(Bitmap bitmap) { var old = image; image = bitmap; old?.Dispose(); Invalidate(); }
        RectangleF ImageBounds
        {
            get { if (image == null) return RectangleF.Empty; float scale = Math.Min((float)Width / image.Width, (float)Height / image.Height); return new RectangleF((Width - image.Width * scale) / 2, (Height - image.Height * scale) / 2, image.Width * scale, image.Height * scale); }
        }
        PointF Normalized(Point point) { var b = ImageBounds; return new PointF(Math.Max(0, Math.Min(1, (point.X - b.Left) / b.Width)), Math.Max(0, Math.Min(1, (point.Y - b.Top) / b.Height))); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (image == null) return; var b = ImageBounds; e.Graphics.DrawImage(image, b);
            if (Crop != null) using (var pen = new Pen(Color.FromArgb(34, 103, 201), 2))
                e.Graphics.DrawRectangle(pen, b.Left + (float)Crop[0] * b.Width, b.Top + (float)Crop[1] * b.Height, (float)(Crop[2] - Crop[0]) * b.Width, (float)(Crop[3] - Crop[1]) * b.Height);
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left && image != null && ImageBounds.Contains(e.Location)) { anchor = Normalized(e.Location); Capture = true; } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (!anchor.HasValue) return; var a = anchor.Value; var p = Normalized(e.Location); Crop = new double[] { Math.Min(a.X, p.X), Math.Min(a.Y, p.Y), Math.Max(a.X, p.X), Math.Max(a.Y, p.Y) }; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (!anchor.HasValue) return; anchor = null; Capture = false; if (!Rules.ValidCrop(Crop) || Crop[2] - Crop[0] < 0.01 || Crop[3] - Crop[1] < 0.01) Crop = null; Changed?.Invoke(this, EventArgs.Empty); Invalidate(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
        protected override void Dispose(bool disposing) { if (disposing) image?.Dispose(); image = null; base.Dispose(disposing); }
    }
}
