using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PdfSharp.Pdf.IO;

namespace InvoiceAssistant
{
    public sealed class Importer
    {
        public string Cache { get; }
        public List<string> DiscoveryWarnings { get; } = new List<string>();
        public Importer(string cache) { Cache = cache; Directory.CreateDirectory(cache); }
        public IEnumerable<string> Discover(IEnumerable<string> inputs, IEnumerable<string> outputs, CancellationToken token)
        {
            DiscoveryWarnings.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>(inputs.Reverse());
            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested(); var p = Path.GetFullPath(stack.Pop());
                if (!seen.Add(p) || Rules.Inside(p, AppDomain.CurrentDomain.BaseDirectory) || Rules.Inside(p, Cache) || outputs.Any(o => Rules.Inside(p, o))) continue;
                if (Directory.Exists(p))
                {
                    try
                    {
                        if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0 || Path.GetFileName(p).StartsWith(".") || Path.GetFileName(p).StartsWith("发票整理_")) continue;
                        foreach (var child in Directory.EnumerateFileSystemEntries(p).OrderByDescending(x => x)) stack.Push(child);
                    }
                    catch (IOException ex) { DiscoveryWarnings.Add(p + "：" + ex.Message); }
                    catch (UnauthorizedAccessException ex) { DiscoveryWarnings.Add(p + "：" + ex.Message); }
                }
                else if (File.Exists(p) && Rules.Extensions.Contains(Path.GetExtension(p))) yield return p;
            }
        }
        public async Task<List<Ticket>> Read(string path, CancellationToken token)
        {
            var result = new List<Ticket>(); string hash = "";
            try
            {
                hash = Rules.Hash(path); token.ThrowIfCancellationRequested();
                var folder = Path.Combine(Cache, hash); Directory.CreateDirectory(folder);
                var source = Path.Combine(folder, "source" + Path.GetExtension(path).ToLowerInvariant());
                var pdf = Path.Combine(folder, "document.pdf");
                // Copy first. Conversion reads an immutable local copy, never writes the original.
                File.Copy(path, source, true);
                if (Rules.Hash(source) != hash) throw new IOException("读取期间原文件发生变化，请重新导入");
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".pdf") File.Copy(source, pdf, true);
                else if (ext == ".doc" || ext == ".docx") { if (File.Exists(pdf)) File.Delete(pdf); await SystemServices.ConvertWord(source, pdf, token); }
                else SystemServices.ImagePdf(source, pdf);
                if (Rules.Hash(path) != hash) throw new IOException("处理期间原文件发生变化，请重新导入");
                int count; using (var document = PdfReader.Open(pdf, PdfDocumentOpenMode.Import)) count = document.PageCount;
                var pdfHash = Rules.Hash(pdf);
                for (int page = 1; page <= count; page++)
                {
                    token.ThrowIfCancellationRequested();
                    var row = new Ticket { Source = path, SourceHash = hash, Pdf = pdf, PdfHash = pdfHash, Start = page, End = page, Pages = count };
                    await Recognize(row, token);
                    if (count > 1) Parser.Warn(row, "多页材料，请核对页范围；同一张票的续页可在核对窗口合并范围并移除多余行");
                    result.Add(row);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { result.Add(new Ticket { Source = path, SourceHash = hash, Error = "无法导入：" + ex.Message }); }
            return result;
        }
        public static async Task Recognize(Ticket row, CancellationToken token)
        {
            string text = ""; bool fallback = false;
            row.Number = ""; row.Date = ""; row.Amount = ""; row.Warning = ""; row.Reviewed = false;
            try { text = SystemServices.PdfText(row.Pdf, row.Start, row.Crop); }
            catch { fallback = true; }
            // A scanned invoice can coexist with a small PDF text layer. OCR unless essential fields are complete.
            Parser.Apply(row, text, Path.GetDirectoryName(row.Source));
            if (fallback || string.IsNullOrWhiteSpace(text) || !Rules.ValidDate(row.Date) || row.Number == "" || row.Amount == "")
            {
                try
                {
                    using (var bitmap = await SystemServices.Render(row.Pdf, row.Start, token, 2200))
                    using (var clip = SystemServices.Clip(bitmap, row.Crop))
                    {
                        var recognized = await SystemServices.Ocr(clip, token);
                        Parser.Apply(row, text + "\n" + recognized, Path.GetDirectoryName(row.Source));
                        Parser.Warn(row, "系统识别结果，请查看原件核对");
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Parser.Warn(row, ex.Message); }
            }
            // Retain the multi-invoice requirement even when the new crop needs manual entry.
            if (row.RequiresSplit) Parser.Warn(row, "框选只保留一张票后可填写字段，其他票请复制拆分行继续框选");
        }
    }
}
