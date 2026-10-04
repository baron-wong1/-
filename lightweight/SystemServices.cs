using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;
using Pig = UglyToad.PdfPig.PdfDocument;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace InvoiceAssistant
{
    public static class SystemServices
    {
        public static async Task<Bitmap> Render(string path, int page, CancellationToken token, uint width = 1500)
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask(token);
            var pdf = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file).AsTask(token);
            using (var source = pdf.GetPage((uint)(page - 1)))
            using (var stream = new InMemoryRandomAccessStream())
            {
                await source.RenderToStreamAsync(stream, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = width }).AsTask(token);
                using (var input = stream.AsStreamForRead()) using (var bitmap = new Bitmap(input)) return new Bitmap(bitmap);
            }
        }
        public static Bitmap Clip(Bitmap bitmap, double[] crop)
        {
            if (crop == null) return new Bitmap(bitmap);
            if (!Rules.ValidCrop(crop)) throw new ArgumentException("裁剪范围无效");
            int x = (int)(crop[0] * bitmap.Width), y = (int)(crop[1] * bitmap.Height);
            var area = Rectangle.FromLTRB(x, y, Math.Min(bitmap.Width, (int)Math.Ceiling(crop[2] * bitmap.Width)), Math.Min(bitmap.Height, (int)Math.Ceiling(crop[3] * bitmap.Height)));
            return bitmap.Clone(area, PixelFormat.Format32bppArgb);
        }
        public static async Task<string> Ocr(Bitmap image, CancellationToken token)
        {
            // Use only installed language packs. Never download a model in the application.
            var language = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(l => l.LanguageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
            if (language == null) throw new InvalidOperationException("系统未安装中文文字识别，请人工填写；可在 Windows 语言设置安装中文基本输入后重试");
            return await OcrWithLanguage(image, language, token);
        }
        internal static async Task<string> OcrWithLanguage(Bitmap image, Language language, CancellationToken token)
        {
            var engine = OcrEngine.TryCreateFromLanguage(language);
            if (engine == null) throw new InvalidOperationException("系统文字识别不可用，请人工填写");
            double scale = Math.Min(1, (double)OcrEngine.MaxImageDimension / Math.Max(image.Width, image.Height));
            using (var resized = new Bitmap(image, Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale))))
            using (var memory = new MemoryStream())
            {
                resized.Save(memory, ImageFormat.Png); memory.Position = 0;
                using (var random = memory.AsRandomAccessStream())
                {
                    var decoder = await BitmapDecoder.CreateAsync(random).AsTask(token);
                    using (var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(token))
                    {
                        var result = await engine.RecognizeAsync(bitmap).AsTask(token);
                        return string.Join("\n", result.Lines.Select(l => l.Text));
                    }
                }
            }
        }
        public static string PdfText(string path, int page, double[] crop = null)
        {
            using (var pdf = Pig.Open(path))
            {
                var p = pdf.GetPage(page);
                if (crop == null) return ContentOrderTextExtractor.GetText(p);
                // PdfPig coordinates start at the bottom-left; the preview uses top-left.
                var words = p.GetWords().Where(w =>
                {
                    var b = w.BoundingBox; double x = (b.Left + b.Right) / 2 / p.Width, y = 1 - (b.Bottom + b.Top) / 2 / p.Height;
                    return x >= crop[0] && x <= crop[2] && y >= crop[1] && y <= crop[3];
                });
                return string.Join("\n", words.OrderByDescending(w => w.BoundingBox.Top).ThenBy(w => w.BoundingBox.Left).Select(w => w.Text));
            }
        }
        public static void NormalizePdf(string path, CancellationToken token)
        {
            // XPdfForm's dimensions use the unrotated MediaBox. Normalize the visible
            // page in the private cache so preview, crop and printing share coordinates.
            var target = path + ".normalized";
            using (var form = XPdfForm.FromFile(path))
            {
                bool needed = false;
                for (int index = 1; index <= form.PageCount; index++)
                {
                    form.PageNumber = index; var p = form.Page;
                    if (p.Rotate != 0 || p.MediaBoxReadOnly.X1 != 0 || p.MediaBoxReadOnly.Y1 != 0 || p.HasCropBox && p.CropBoxReadOnly != p.MediaBoxReadOnly) needed = true;
                }
                if (!needed) return;
                using (var output = new PdfDocument())
                {
                    for (int index = 1; index <= form.PageCount; index++)
                    {
                        token.ThrowIfCancellationRequested(); form.PageNumber = index;
                        var source = form.Page; var media = source.MediaBoxReadOnly; var crop = source.EffectiveCropBoxReadOnly;
                        int rotation = (source.Rotate % 360 + 360) % 360; source.Rotate = 0;
                        double w = crop.Width, h = crop.Height;
                        var page = output.AddPage(); page.Width = XUnit.FromPoint(rotation == 90 || rotation == 270 ? h : w); page.Height = XUnit.FromPoint(rotation == 90 || rotation == 270 ? w : h);
                        using (var graphics = XGraphics.FromPdfPage(page))
                        {
                            graphics.IntersectClip(new XRect(0, 0, page.Width.Point, page.Height.Point));
                            if (rotation == 90) { graphics.TranslateTransform(h, 0); graphics.RotateTransform(90); }
                            else if (rotation == 180) { graphics.TranslateTransform(w, h); graphics.RotateTransform(180); }
                            else if (rotation == 270) { graphics.TranslateTransform(0, w); graphics.RotateTransform(270); }
                            graphics.DrawImage(form, -(crop.X1 - media.X1), -(media.Y2 - crop.Y2), form.PointWidth, form.PointHeight);
                        }
                    }
                    output.Save(target);
                }
            }
            File.Copy(target, path, true); File.Delete(target);
        }
        public static void ImagePdf(string image, string target)
        {
            using (var bitmap = new Bitmap(image))
            {
                if (bitmap.PropertyIdList.Contains(0x112))
                {
                    var orientation = BitConverter.ToUInt16(bitmap.GetPropertyItem(0x112).Value, 0);
                    var rotations = new[] { RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipX, RotateFlipType.Rotate180FlipNone, RotateFlipType.Rotate180FlipX, RotateFlipType.Rotate90FlipX, RotateFlipType.Rotate90FlipNone, RotateFlipType.Rotate270FlipX, RotateFlipType.Rotate270FlipNone };
                    if (orientation < rotations.Length) bitmap.RotateFlip(rotations[orientation]);
                }
                using (var pdf = new PdfDocument()) using (var memory = new MemoryStream())
                {
                    bitmap.Save(memory, ImageFormat.Png); memory.Position = 0;
                    var page = pdf.AddPage();
                    double dpi = bitmap.HorizontalResolution; if (dpi < 72 || dpi > 600) dpi = 150;
                    page.Width = XUnit.FromPoint(bitmap.Width * 72 / dpi); page.Height = XUnit.FromPoint(bitmap.Height * 72 / dpi);
                    using (var img = XImage.FromStream(memory)) using (var graphics = XGraphics.FromPdfPage(page)) graphics.DrawImage(img, 0, 0, page.Width.Point, page.Height.Point);
                    pdf.Save(target);
                }
            }
        }
        public static async Task ConvertWord(string source, string target, CancellationToken token)
        {
            var executable = Process.GetCurrentProcess().MainModule.FileName;
            var info = new ProcessStartInfo(executable, "--convert-word " + Quote(source) + " " + Quote(target)) { UseShellExecute = false, CreateNoWindow = true };
            using (var process = Process.Start(info))
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(120));
                try
                {
                    while (!process.HasExited) await Task.Delay(100, timeout.Token);
                    if (process.ExitCode != 0 || !File.Exists(target))
                    {
                        string message = File.Exists(target + ".error") ? File.ReadAllText(target + ".error") : "Word 转换失败，请使用本机 Office 另存为 PDF 后导入";
                        throw new InvalidOperationException(message);
                    }
                }
                catch (OperationCanceledException)
                {
                    KillTree(process);
                    KillOwnWord(target + ".pid", process.StartTime);
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException("Word 转换超过 120 秒，请先在 Office 中另存为 PDF");
                }
            }
        }
        static string Quote(string p) => "\"" + p.Replace("\"", "") + "\"";
        static void KillTree(Process p)
        {
            if (p.HasExited) return;
            using (var kill = Process.Start(new ProcessStartInfo("taskkill.exe", "/PID " + p.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true })) kill.WaitForExit(5000);
        }
        static void KillOwnWord(string marker, DateTime workerStart)
        {
            if (!File.Exists(marker) || !int.TryParse(File.ReadAllText(marker), out var id)) return;
            try
            {
                using (var word = Process.GetProcessById(id))
                    if (word.ProcessName.Equals("WINWORD", StringComparison.OrdinalIgnoreCase) && word.StartTime >= workerStart.AddSeconds(-1) && !word.HasExited) word.Kill();
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        public static int WordWorker(string source, string target)
        {
            object appObject = null, docObject = null;
            try
            {
                var type = Type.GetTypeFromProgID("Word.Application");
                if (type == null) throw new InvalidOperationException("本机未安装 Microsoft Word，请先将 Word 文件转成 PDF 再导入");
                var existing = Process.GetProcessesByName("WINWORD").Select(p => { using (p) return p.Id; }).ToHashSet();
                appObject = Activator.CreateInstance(type); dynamic app = appObject;
                GetWindowThreadProcessId(new IntPtr((int)app.Hwnd), out var wordPid);
                if (wordPid != 0 && !existing.Contains((int)wordPid)) File.WriteAllText(target + ".pid", wordPid.ToString());
                app.Visible = false; app.DisplayAlerts = 0; app.AutomationSecurity = 3;
                docObject = app.Documents.Open(FileName: source, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, Visible: false, OpenAndRepair: false);
                dynamic doc = docObject; doc.ExportAsFixedFormat(target, 17, OpenAfterExport: false);
                return 0;
            }
            catch (Exception ex) { File.WriteAllText(target + ".error", "Word 转换失败：" + ex.Message + "。可在 Office 中另存为 PDF 后导入"); return 1; }
            finally
            {
                if (docObject != null) { try { ((dynamic)docObject).Close(0); } catch { } Marshal.FinalReleaseComObject(docObject); }
                if (appObject != null) { try { ((dynamic)appObject).Quit(0); } catch { } Marshal.FinalReleaseComObject(appObject); }
                if (File.Exists(target + ".pid")) File.Delete(target + ".pid");
            }
        }
    }
}
