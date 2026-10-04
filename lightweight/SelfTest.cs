using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using PdfSharp.Pdf.IO;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace InvoiceAssistant
{
    // Runs the real application and system APIs against synthetic invoices only.
    internal static class SelfTest
    {
        static readonly List<string> checks = new List<string>();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wparam, IntPtr lparam);
        static Control Named(Control root, string name) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c))).First(c => c.Name == name);
        static IEnumerable<Control> Descendants(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
        static void Check(bool passed, string name) { if (!passed) throw new Exception("FAILED: " + name); checks.Add(name); }
        static void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { checks.Add(name); return; } throw new Exception("FAILED: " + name); }
        public static int Run(string root)
        {
            Directory.CreateDirectory(root); string report = Path.Combine(root, "self-test.txt"); int exit = 1;
            var session = new Session(); var form = new MainForm(session, Path.Combine(root, "cache")) { TestDestination = Path.Combine(root, "exports") };
            form.Shown += async (_, __) =>
            {
                try { await Verify(form, root); exit = 0; File.WriteAllLines(report, new[] { "PASS " + checks.Count + " checks" }.Concat(checks)); }
                catch (Exception ex) { File.WriteAllText(report, ex.ToString() + "\nCompleted:\n" + string.Join("\n", checks)); }
                finally { form.Close(); }
            };
            Application.Run(form); return exit;
        }
        static async Task Verify(MainForm form, string root)
        {
            var token = CancellationToken.None;
            Check(form.Current.Rows.Count == 0 && form.Current.Person == "" && form.Current.Month == "", "Empty initial session");
            Check(Rules.Money("0.10") + Rules.Money("0.20") == 0.30m, "Exact decimal money");
            Reject(() => Rules.Money("1.001"), "Reject three decimal places"); Reject(() => Rules.Money("NaN"), "Reject nonfinite money");
            var parser = new Ticket(); Parser.Apply(parser, "发票号码：00123456789012345678\n开票日期：2026年10月3日\n酒店住宿\n价税合计（小写）￥123.45");
            Check(parser.Number == "00123456789012345678" && parser.Date == "2026-10-03" && parser.Amount == "123.45" && parser.Category == "住宿", "Parse full length leading-zero number, labelled date and total");
            var multi = new Ticket(); Parser.Apply(multi, "发票号码：12345678\n发票号码：87654321\n开票日期：2026-10-03\n价税合计￥10.00");
            Check(multi.RequiresSplit && multi.Amount == "" && multi.Date == "", "Multi-invoice page never borrows first fields"); multi.Reviewed = true; multi.Number = "12345678"; multi.Amount = "10.00"; multi.Date = "2026-10-03";
            Check(Rules.Validate(multi).Any(x => x.Contains("拆分")), "Manual field editing cannot bypass multi-invoice split");
            var a = parser.Copy(); a.Reviewed = true; var b = a.Copy(); b.Id = Guid.NewGuid().ToString("N");
            var pair = new List<Ticket> { a, b }; Rules.Reconcile(pair); Check(a.Status == "就绪" && b.Status == "重复", "Same-number identical fields excluded");
            b.Amount = "2.00"; Rules.Reconcile(pair); Check(pair.All(r => r.Status == "冲突"), "Same-number amount conflict blocks export");
            a.KeepConflict = b.KeepConflict = true; Rules.Reconcile(pair); Check(pair.All(r => r.Status == "就绪"), "Explicit conflict confirmation");
            a.SourceHash = b.SourceHash = "same"; Reject(() => Rules.Prepare(pair), "Overlapping page ranges rejected");
            a.Crop = new double[] { 0, 0, 1, 0.5 }; b.Crop = new double[] { 0, 0.5, 1, 1 }; Check(Rules.Prepare(pair).Count == 2, "Disjoint page crops accepted");

            await form.ImportPaths(new[] { Path.Combine(root, "fixtures", "invoice.pdf"), Path.Combine(root, "fixtures", "copy.pdf") });
            Check(form.Current.Rows.Count == 1, "Import and duplicate file skip through real desktop");
            var invoice = form.Current.Rows[0];
            Check(invoice.Number == "00123456789012345678" && invoice.Amount == "123.45" && invoice.Date == "2026-10-03" && invoice.Status == "就绪", "Text PDF import extracts actual invoice fields");
            using (var preview = await SystemServices.Render(invoice.Pdf, 1, token)) { Check(preview.Width == 1500 && preview.Height > 0, "Windows PDF preview without bundled renderer"); preview.Save(Path.Combine(root, "preview.png"), ImageFormat.Png); }
            using (var dialog = new ReviewForm(invoice))
            {
                Exception dialogError = null;
                dialog.Shown += async (_, __) =>
                {
                    try
                    {
                        for (int tries = 0; !dialog.Preview.HasImage && tries < 40; tries++) await Task.Delay(50);
                        Check(dialog.Preview.HasImage, "Review window displays actual source preview");
                        var area = dialog.Preview.VisibleImageBounds;
                        int x1 = (int)(area.Left + area.Width * 0.01), y1 = (int)(area.Top + area.Height * 0.01), x2 = (int)(area.Left + area.Width * 0.99), y2 = (int)(area.Top + area.Height * 0.99);
                        SendMessage(dialog.Preview.Handle, 0x201, new IntPtr(1), new IntPtr((y1 << 16) | x1));
                        SendMessage(dialog.Preview.Handle, 0x200, new IntPtr(1), new IntPtr((y2 << 16) | x2));
                        SendMessage(dialog.Preview.Handle, 0x202, IntPtr.Zero, new IntPtr((y2 << 16) | x2));
                        Check(Rules.ValidCrop(dialog.Preview.Crop), "Native mouse drag creates valid crop");
                        Named(dialog, "amount").Text = "123.40"; ((CheckBox)Named(dialog, "confirmed")).Checked = true;
                        using (var image = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size)); image.Save(Path.Combine(root, "review.png"), ImageFormat.Png); }
                        ((Button)Named(dialog, "save")).PerformClick();
                    }
                    catch (Exception ex) { dialogError = ex; dialog.Close(); }
                };
                var accepted = dialog.ShowDialog(form); if (dialogError != null) throw dialogError;
                Check(accepted == DialogResult.OK && dialog.Result.Amount == "123.40" && dialog.Result.Reviewed && Rules.ValidCrop(dialog.Result.Crop), "Review confirmation saves manual edits and crop through actual controls");
            }
            var importer = new Importer(Path.Combine(root, "cache"));
            var bad = await importer.Read(Path.Combine(root, "fixtures", "bad.pdf"), token); Check(bad.Single().Error != "", "Bad PDF isolated as visible error record");
            var images = await importer.Read(Path.Combine(root, "fixtures", "receipt.png"), token); Check(images.Single().Error == "" && File.Exists(images[0].Pdf), "PNG imports without OCR language pack");
            var jpg = await importer.Read(Path.Combine(root, "fixtures", "receipt.jpg"), token); Check(jpg.Single().Error == "", "JPEG imports without bundled image runtime");
            var many = await importer.Read(Path.Combine(root, "fixtures", "multiple.pdf"), token); Check(many.Single().RequiresSplit, "Multiple invoices in one actual PDF require split");
            var pages = await importer.Read(Path.Combine(root, "fixtures", "pages.pdf"), token); Check(pages.Count == 2 && pages.All(r => r.Pages == 2 && r.Warning.Contains("多页")), "Multi-page PDF retains independent review ranges");
            var rotated = await importer.Read(Path.Combine(root, "fixtures", "rotated.pdf"), token); Check(rotated.Single().Number == "00999999999999999999", "Rotated PDF text import");
            using (var preview = await SystemServices.Render(rotated[0].Pdf, 1, token)) Check(preview.Width > preview.Height, "System preview respects PDF rotation");
            rotated[0].Reviewed = true;
            var rotatedOutput = Exporter.Write(rotated, "旋转测试", "2026-10", Path.Combine(root, "rotated-exports"), token);
            using (var preview = await SystemServices.Render(Directory.GetFiles(rotatedOutput, "*合并打印.pdf").Single(), 1, token)) { preview.Save(Path.Combine(root, "rotated-print.png"), ImageFormat.Png); Check(preview.Width > 0, "Rotated source exports through vector form and renders"); }
            var top = many[0].Copy(); top.Number = "12345678"; top.Date = "2026-10-03"; top.Amount = "10.00"; top.Reviewed = true; top.Crop = new double[] { 0, 0, 1, 0.5 };
            var bottom = top.Copy(); bottom.Id = Guid.NewGuid().ToString("N"); bottom.Number = "87654321"; bottom.Amount = "20.00"; bottom.Crop = new double[] { 0, 0.5, 1, 1 };
            var splitOutput = Exporter.Write(new List<Ticket> { top, bottom }, "拆分测试", "2026-10", Path.Combine(root, "split-exports"), token);
            using (var preview = await SystemServices.Render(Directory.GetFiles(splitOutput, "*合并打印.pdf").Single(), 1, token)) { preview.Save(Path.Combine(root, "split-print.png"), ImageFormat.Png); Check(preview.Width > 0, "Two disjoint crops export and render"); }

            var languages = OcrEngine.AvailableRecognizerLanguages;
            checks.Add("Installed system OCR languages: " + string.Join(",", languages.Select(l => l.LanguageTag)));
            var english = languages.FirstOrDefault(l => l.LanguageTag.StartsWith("en"));
            if (english != null)
                using (var image = new Bitmap(Path.Combine(root, "fixtures", "ocr-english.png"))) Check((await SystemServices.OcrWithLanguage(image, english, token)).Contains("12345678"), "Real Windows OCR using installed language");
            else checks.Add("System OCR recognition test unavailable: runner has no English language pack");
            if (!languages.Any(l => l.LanguageTag.StartsWith("zh"))) Check(images[0].Warning.Contains("中文") && Rules.Validate(images[0]).Any(), "Missing Chinese OCR gives manual fallback and blocks missing fields");
            if (Type.GetTypeFromProgID("Word.Application") == null)
            {
                var word = await importer.Read(Path.Combine(root, "fixtures", "invoice.docx"), token); Check(word[0].Error.Contains("Word"), "Missing Office produces explicit per-file error");
            }
            else checks.Add("Office installed: separate real-document conversion acceptance required");
            using (var canceled = new CancellationTokenSource()) { canceled.Cancel(); try { await importer.Read(invoice.Source, canceled.Token); throw new Exception("Cancellation failed"); } catch (OperationCanceledException) { checks.Add("Import cancellation"); } }

            // A second category and an attachment exercise classification, counts and PDF page sizes.
            var longRows = await importer.Read(Path.Combine(root, "fixtures", "long.pdf"), token); var longTicket = longRows.Single(); longTicket.Reviewed = true;
            var attachment = images[0]; attachment.Attachment = true; attachment.Category = "住宿"; attachment.Reviewed = true;
            form.Current.Rows.Add(longTicket); form.Current.Rows.Add(attachment); form.SetInfo("=SUM(A1:A2)", "2026-10"); form.RefreshRows();
            var before = form.Current.Rows.Select(r => Rules.Hash(r.Source)).ToArray();
            var output = await form.ExportFromButton(); Check(Directory.Exists(output), "Export through real desktop handler");
            Check(Directory.GetFiles(output, "*.pdf").Length == 3 && Directory.GetFiles(output, "*.xlsx").Length == 1, "Two category PDFs, combined PDF and workbook");
            var combined = Directory.GetFiles(output, "*合并打印.pdf").Single();
            using (var pdf = PdfReader.Open(combined, PdfDocumentOpenMode.Import))
            {
                Check(pdf.PageCount == 2, "Short invoice and half-page attachment pair; long invoice alone");
                Check(pdf.Outlines.Count == 2, "Category bookmarks"); Check(Math.Abs(pdf.Pages[0].Width.Point - 595.2756) < 0.1, "A4 paper width");
            }
            using (var pdf = UglyToad.PdfPig.PdfDocument.Open(combined))
            {
                var text = string.Join("\n", pdf.GetPages().Select(p => p.Text));
                File.WriteAllText(Path.Combine(root, "output-text.txt"), text);
                Check(text.Contains(invoice.Number) && text.Contains(longTicket.Number), "PDF source text remains vector and identifiers intact. Actual: " + text);
                Check(text.Contains("1 / 2") && text.Contains("2 / 2"), "Continuous combined page numbering");
            }
            using (var zip = ZipFile.OpenRead(Directory.GetFiles(output, "*.xlsx").Single()))
            using (var stream = zip.GetEntry("xl/worksheets/sheet1.xml").Open())
            {
                XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"; var xml = XDocument.Load(stream);
                Check(!xml.Descendants(ns + "f").Any() && xml.Descendants(ns + "t").Any(x => x.Value == "=SUM(A1:A2)"), "Spreadsheet formula injection stays literal text");
                Check(xml.Descendants(ns + "t").Any(x => x.Value == "00123456789012345678"), "Spreadsheet leading-zero identifier preserved");
                Check(xml.Descendants(ns + "v").Any(x => x.Value == "133.55"), "Exact workbook total, attachment excluded");
            }
            Check(before.SequenceEqual(form.Current.Rows.Select(r => Rules.Hash(r.Source))), "Original bytes unchanged after all processing");
            using (var screenshot = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(screenshot, new Rectangle(Point.Empty, form.Size)); screenshot.Save(Path.Combine(root, "desktop.png"), ImageFormat.Png); }
            var sessionPath = Path.Combine(root, "session-test.json");
            using (var stream = File.Create(sessionPath)) new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Session)).WriteObject(stream, form.Current);
            using (var stream = File.OpenRead(sessionPath)) { var recovered = (Session)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Session)).ReadObject(stream); Check(recovered.Rows.Count == 3 && recovered.Rows[0].Number == invoice.Number, "Session recovery retains invoice identifiers and manual fields"); }

            // No finalized folder survives a cancellation or source mutation.
            var cancellationRoot = Path.Combine(root, "cancel-exports");
            using (var canceled = new CancellationTokenSource()) { canceled.Cancel(); try { Exporter.Write(form.Current.Rows, "测试", "2026-10", cancellationRoot, canceled.Token); throw new Exception("Export cancellation failed"); } catch (OperationCanceledException) { checks.Add("Export cancellation without partial output"); } }
            File.AppendAllText(invoice.Source, "modified");
            try { Exporter.Write(form.Current.Rows, "测试", "2026-10", Path.Combine(root, "changed-exports"), token); throw new Exception("Source mutation not detected"); } catch (IOException) { checks.Add("Changed original blocks export"); }
        }
    }
}
