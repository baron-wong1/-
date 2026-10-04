using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace InvoiceAssistant
{
    public static class Exporter
    {
        const double Width = 595.2756, Height = 841.8898, Margin = 24, Footer = 25, Gap = 12;
        const double Half = (Height - Margin - Footer - Gap) / 2;
        public static string Write(IList<Ticket> records, string person, string month, string destination, CancellationToken token)
        {
            person = person.Trim();
            if (person == "" || person.Length > 80) throw new ArgumentException("请填写报销人（1–80 个字符）");
            if (!Regex.IsMatch(month, @"^20\d{2}-(0[1-9]|1[0-2])$")) throw new ArgumentException("请填写报销月份（yyyy-MM）");
            var rows = Rules.Prepare(records);
            if (Rules.Inside(destination, AppDomain.CurrentDomain.BaseDirectory) || Rules.Inside(destination, Session.Root)) throw new ArgumentException("请在程序及缓存目录之外选择输出文件夹");
            foreach (var r in rows.GroupBy(r => r.SourceHash).Select(g => g.First())) { token.ThrowIfCancellationRequested(); Rules.CheckSource(r); }
            Directory.CreateDirectory(destination);
            var stage = Path.Combine(destination, ".invoice-export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
            var output = Path.Combine(destination, "发票整理_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            var categories = Rules.Categories.Concat(rows.Select(r => r.Category).Except(Rules.Categories).OrderBy(c => c, StringComparer.Ordinal)).Where(c => rows.Any(r => r.Category == c)).ToArray();
            var prefix = month + "_" + Rules.SafeName(person);
            try
            {
                using (var combined = new PdfDocument())
                {
                    int index = 0;
                    foreach (var category in categories)
                    {
                        token.ThrowIfCancellationRequested();
                        using (var pdf = MakePdf(Rules.Sort(rows.Where(r => r.Category == category)), token))
                        {
                            var temp = Path.Combine(stage, ".category.pdf"); pdf.Save(temp);
                            int start = combined.PageCount;
                            using (var input = PdfReader.Open(temp, PdfDocumentOpenMode.Import)) foreach (var page in input.Pages) combined.AddPage(page);
                            combined.Outlines.Add(category, combined.Pages[start], true);
                            using (var numbered = PdfReader.Open(temp, PdfDocumentOpenMode.Modify))
                            {
                                AddNumbers(numbered); numbered.Save(Path.Combine(stage, prefix + "_" + (++index).ToString("D2") + "_" + Rules.SafeName(category) + ".pdf"));
                            }
                            File.Delete(temp);
                        }
                    }
                    AddNumbers(combined); combined.Save(Path.Combine(stage, prefix + "_合并打印.pdf"));
                }
                WriteExcel(Path.Combine(stage, prefix + "_统计表.xlsx"), rows, person, month, categories);
                token.ThrowIfCancellationRequested();
                foreach (var r in rows.GroupBy(r => r.SourceHash).Select(g => g.First())) Rules.CheckSource(r);
                Directory.Move(stage, output);
                return output;
            }
            finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        }
        internal static PdfDocument MakePdf(IEnumerable<Ticket> rows, CancellationToken token)
        {
            var output = new PdfDocument(); PdfPage current = null; int slots = 0;
            try
            {
                foreach (var r in rows)
                using (var form = XPdfForm.FromFile(r.Pdf))
                {
                    for (int p = r.Start; p <= r.End; p++)
                    {
                        token.ThrowIfCancellationRequested(); form.PageNumber = p;
                        var crop = r.Crop ?? new double[] { 0, 0, 1, 1 };
                        double w = form.PointWidth * (crop[2] - crop[0]), h = form.PointHeight * (crop[3] - crop[1]);
                        bool full = !r.Attachment && h * (Width - Margin * 2) / w > Half;
                        if (full || current == null || slots == 2) { current = output.AddPage(); current.Width = XUnit.FromPoint(Width); current.Height = XUnit.FromPoint(Height); slots = 0; }
                        var box = new XRect(Margin, Margin + slots * (Half + Gap), Width - Margin * 2, full ? Height - Margin - Footer : Half);
                        using (var graphics = XGraphics.FromPdfPage(current))
                        {
                            double scale = Math.Min(box.Width / w, box.Height / h);
                            double left = box.X + (box.Width - w * scale) / 2, top = box.Y + (box.Height - h * scale) / 2;
                            var state = graphics.Save(); graphics.IntersectClip(new XRect(left, top, w * scale, h * scale));
                            graphics.DrawImage(form, left - crop[0] * form.PointWidth * scale, top - crop[1] * form.PointHeight * scale, form.PointWidth * scale, form.PointHeight * scale);
                            graphics.Restore(state);
                        }
                        slots++; if (full) { current = null; slots = 0; }
                    }
                }
                return output;
            }
            catch { output.Dispose(); throw; }
        }
        static void AddNumbers(PdfDocument doc)
        {
            var font = new XFont("Arial", 8);
            for (int i = 0; i < doc.PageCount; i++) using (var graphics = XGraphics.FromPdfPage(doc.Pages[i]))
                graphics.DrawString((i + 1) + " / " + doc.PageCount, font, XBrushes.Gray, new XRect(Margin, Height - 17, Width - Margin * 2, 12), XStringFormats.TopRight);
        }
        static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        static XElement TextCell(string reference, string value, int style = 0) => new XElement(S + "c", new XAttribute("r", reference), new XAttribute("s", style), new XAttribute("t", "inlineStr"), new XElement(S + "is", new XElement(S + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), XmlText(value))));
        static string XmlText(string s) => Regex.Replace(s ?? "", @"[\x00-\x08\x0B\x0C\x0E-\x1F]", "");
        static XElement NumberCell(string reference, decimal value, int style = 2) => new XElement(S + "c", new XAttribute("r", reference), new XAttribute("s", style), new XElement(S + "v", value.ToString(CultureInfo.InvariantCulture)));
        internal static void WriteExcel(string path, List<Ticket> rows, string person, string month, string[] categories)
        {
            var invoices = categories.SelectMany(c => Rules.Sort(rows.Where(r => r.Category == c && !r.Attachment))).ToArray();
            var data = new XElement(S + "sheetData"); int index = 1;
            void Row(params XElement[] cells) { data.Add(new XElement(S + "row", new XAttribute("r", index), new XAttribute("ht", index == 1 ? 32 : 25), new XAttribute("customHeight", 1), cells)); index++; }
            Row(TextCell("A1", month.Substring(0, 4) + "年" + int.Parse(month.Substring(5)) + "月电子发票明细表", 1));
            var headers = new[] { "报销日期", "报销人", "发票内容", "发票金额", "开票日期", "发票号码" };
            Row(headers.Select((h, i) => TextCell(((char)('A' + i)).ToString() + "2", h, 1)).ToArray());
            var dateBase = new DateTime(1899, 12, 30);
            foreach (var r in invoices)
                Row(TextCell("A" + index, month), TextCell("B" + index, person), TextCell("C" + index, r.Category), NumberCell("D" + index, Rules.Money(r.Amount).Value), NumberCell("E" + index, (decimal)(DateTime.ParseExact(r.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture) - dateBase).TotalDays, 3), TextCell("F" + index, r.Number, 4));
            int detailEnd = index - 1, totalRow = index;
            Row(TextCell("A" + index, "合计", 1), NumberCell("D" + index, invoices.Sum(r => Rules.Money(r.Amount).Value)));
            Row();
            Row(TextCell("A" + index, "类别", 1), TextCell("B" + index, "发票张数", 1), TextCell("C" + index, "发票金额", 1));
            foreach (var c in categories)
            {
                var items = invoices.Where(r => r.Category == c).ToArray(); if (items.Length == 0) continue;
                Row(TextCell("A" + index, c), NumberCell("B" + index, items.Length, 0), NumberCell("C" + index, items.Sum(r => Rules.Money(r.Amount).Value)));
            }
            var sheet = new XElement(S + "worksheet",
                new XElement(S + "sheetViews", new XElement(S + "sheetView", new XAttribute("workbookViewId", 0), new XElement(S + "pane", new XAttribute("ySplit", 2), new XAttribute("topLeftCell", "A3"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
                new XElement(S + "cols", new[] { 18, 14, 22, 18, 18, 32 }.Select((w, i) => new XElement(S + "col", new XAttribute("min", i + 1), new XAttribute("max", i + 1), new XAttribute("width", w), new XAttribute("customWidth", 1)))), data,
                new XElement(S + "autoFilter", new XAttribute("ref", "A2:F" + detailEnd)),
                new XElement(S + "mergeCells", new XAttribute("count", 2), new XElement(S + "mergeCell", new XAttribute("ref", "A1:F1")), new XElement(S + "mergeCell", new XAttribute("ref", "A" + totalRow + ":C" + totalRow))),
                new XElement(S + "pageMargins", new XAttribute("left", 0.3), new XAttribute("right", 0.3), new XAttribute("top", 0.5), new XAttribute("bottom", 0.5), new XAttribute("header", 0.2), new XAttribute("footer", 0.2)),
                new XElement(S + "pageSetup", new XAttribute("paperSize", 9), new XAttribute("orientation", "landscape"), new XAttribute("fitToWidth", 1), new XAttribute("fitToHeight", 0)));
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
                Add(zip, "[Content_Types].xml", new XElement(ct + "Types", new XElement(ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")), new XElement(ct + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                    new[] { new[] { "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml" }, new[] { "/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml" }, new[] { "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml" } }.Select(a => new XElement(ct + "Override", new XAttribute("PartName", a[0]), new XAttribute("ContentType", a[1])))));
                Add(zip, "_rels/.rels", Relationships(new[] { "rId1", "officeDocument", "xl/workbook.xml" }));
                XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
                Add(zip, "xl/workbook.xml", new XElement(S + "workbook", new XAttribute(XNamespace.Xmlns + "r", rel), new XElement(S + "sheets", new XElement(S + "sheet", new XAttribute("name", "电子发票明细"), new XAttribute("sheetId", 1), new XAttribute(rel + "id", "rId1"))),
                    new XElement(S + "definedNames", new XElement(S + "definedName", new XAttribute("name", "_xlnm.Print_Titles"), new XAttribute("localSheetId", 0), "'电子发票明细'!$1:$2"), new XElement(S + "definedName", new XAttribute("name", "_xlnm.Print_Area"), new XAttribute("localSheetId", 0), "'电子发票明细'!$A$1:$F$" + (index - 1)))));
                Add(zip, "xl/_rels/workbook.xml.rels", Relationships(new[] { "rId1", "worksheet", "worksheets/sheet1.xml" }, new[] { "rId2", "styles", "styles.xml" }));
                Add(zip, "xl/worksheets/sheet1.xml", sheet);
                Add(zip, "xl/styles.xml", Styles());
            }
        }
        static XElement Relationships(params string[][] rows)
        {
            XNamespace ns = "http://schemas.openxmlformats.org/package/2006/relationships";
            return new XElement(ns + "Relationships", rows.Select(r => new XElement(ns + "Relationship", new XAttribute("Id", r[0]), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/" + r[1]), new XAttribute("Target", r[2]))));
        }
        static XElement Styles()
        {
            XElement Xf(int format, int font) => new XElement(S + "xf", new XAttribute("numFmtId", format), new XAttribute("fontId", font), new XAttribute("fillId", 0), new XAttribute("borderId", 0), new XAttribute("xfId", 0), new XAttribute("applyNumberFormat", 1), new XAttribute("applyAlignment", 1), new XElement(S + "alignment", new XAttribute("horizontal", format == 164 ? "right" : "center"), new XAttribute("vertical", "center")));
            return new XElement(S + "styleSheet",
                new XElement(S + "numFmts", new XAttribute("count", 2), new XElement(S + "numFmt", new XAttribute("numFmtId", 164), new XAttribute("formatCode", "#,##0.00")), new XElement(S + "numFmt", new XAttribute("numFmtId", 165), new XAttribute("formatCode", "yyyy-m-d"))),
                new XElement(S + "fonts", new XAttribute("count", 2), new XElement(S + "font", new XElement(S + "sz", new XAttribute("val", 11)), new XElement(S + "name", new XAttribute("val", "宋体"))), new XElement(S + "font", new XElement(S + "b"), new XElement(S + "sz", new XAttribute("val", 13)), new XElement(S + "name", new XAttribute("val", "宋体")))),
                new XElement(S + "fills", new XAttribute("count", 2), new XElement(S + "fill", new XElement(S + "patternFill", new XAttribute("patternType", "none"))), new XElement(S + "fill", new XElement(S + "patternFill", new XAttribute("patternType", "gray125")))),
                new XElement(S + "borders", new XAttribute("count", 1), new XElement(S + "border", new XElement(S + "left"), new XElement(S + "right"), new XElement(S + "top"), new XElement(S + "bottom"), new XElement(S + "diagonal"))),
                new XElement(S + "cellStyleXfs", new XAttribute("count", 1), new XElement(S + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0))),
                new XElement(S + "cellXfs", new XAttribute("count", 5), Xf(0, 0), Xf(0, 1), Xf(164, 0), Xf(165, 0), Xf(49, 0)),
                new XElement(S + "cellStyles", new XAttribute("count", 1), new XElement(S + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", 0), new XAttribute("builtinId", 0))));
        }
        static void Add(ZipArchive zip, string path, XElement xml)
        {
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            using (var stream = entry.Open()) using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) new XDocument(new XDeclaration("1.0", "utf-8", "yes"), xml).Save(writer);
        }
    }
}
