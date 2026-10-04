using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace InvoiceAssistant
{
    [DataContract]
    public class Ticket
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString("N");
        [DataMember] public string Source { get; set; } = "";
        [DataMember] public string SourceHash { get; set; } = "";
        [DataMember] public string Pdf { get; set; } = "";
        [DataMember] public string PdfHash { get; set; } = "";
        [DataMember] public string Number { get; set; } = "";
        [DataMember] public string Date { get; set; } = "";
        [DataMember] public string Amount { get; set; } = "";
        [DataMember] public string Category { get; set; } = "其他";
        [DataMember] public bool Attachment { get; set; }
        [DataMember] public int Start { get; set; } = 1;
        [DataMember] public int End { get; set; } = 1;
        [DataMember] public int Pages { get; set; } = 1;
        [DataMember] public double[] Crop { get; set; }
        [DataMember] public string Warning { get; set; } = "";
        [DataMember] public string Error { get; set; } = "";
        [DataMember] public bool Reviewed { get; set; }
        [DataMember] public bool RequiresSplit { get; set; }
        [DataMember] public bool KeepConflict { get; set; }
        [DataMember] public string Travel { get; set; } = "";
        public string Status { get; set; } = "待核对";
        public string Issues { get; set; } = "";
        public string Filename => Path.GetFileName(Source);
        public string PageLabel => Start == End ? Start.ToString() : Start + "–" + End;
        public string Kind => Attachment ? "附件" : "发票";
        public Ticket Copy() => (Ticket)MemberwiseClone();
    }

    public static class Rules
    {
        public static readonly string[] Categories = { "住宿", "车票", "市交", "退票费", "其他" };
        public static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx" };
        public static decimal? Money(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            decimal n;
            if (!decimal.TryParse(s.Replace(",", "").Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out n) || n < 0 || n > 999999999.99m || decimal.Round(n, 2) != n)
                throw new ArgumentException("金额须为 0 至 999999999.99，最多两位小数");
            return n;
        }
        public static bool ValidDate(string s) => DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        public static bool ValidCrop(double[] c) => c != null && c.Length == 4 && c.All(x => !double.IsNaN(x) && !double.IsInfinity(x)) && 0 <= c[0] && c[0] < c[2] && c[2] <= 1 && 0 <= c[1] && c[1] < c[3] && c[3] <= 1;
        public static IEnumerable<string> Validate(Ticket r)
        {
            if (!string.IsNullOrEmpty(r.Error)) { yield return r.Error; yield break; }
            if (string.IsNullOrWhiteSpace(r.Category) || r.Category.Length > 70) yield return "分类须为 1–70 个字符";
            if (!r.Attachment)
            {
                if (!ValidDate(r.Date)) yield return "请核对开票日期（yyyy-MM-dd）";
                if (!Regex.IsMatch(r.Number ?? "", @"^\d{8,30}$")) yield return "请核对发票号码（8–30 位数字）";
                string issue = "";
                try { if (!Money(r.Amount).HasValue) issue = "请核对金额"; } catch (ArgumentException ex) { issue = ex.Message; }
                if (issue != "") yield return issue;
            }
            if (r.Start < 1 || r.Start > r.End || r.End > r.Pages) yield return "页范围超出原件";
            if (r.Crop != null && !ValidCrop(r.Crop)) yield return "裁剪范围无效";
            if (r.RequiresSplit && r.Crop == null) yield return "本页包含多张票据，请框选后拆分并逐张核对";
            if (!r.Reviewed && !string.IsNullOrEmpty(r.Warning)) yield return "请查看原件并确认识别提示";
        }
        public static void Reconcile(IList<Ticket> rows)
        {
            foreach (var r in rows)
            {
                var issues = Validate(r).ToArray();
                r.Issues = string.Join("；", issues);
                r.Status = r.Error != "" ? "失败" : issues.Length == 0 ? "就绪" : "待核对";
            }
            foreach (var g in rows.Where(r => !r.Attachment && Regex.IsMatch(r.Number ?? "", @"^\d{8,30}$")).GroupBy(r => r.Number).Where(g => g.Count() > 1))
            {
                if (g.All(r => r.Status == "就绪") && g.Select(r => new { r.Date, Amount = Money(r.Amount), r.Category }).Distinct().Count() == 1)
                    foreach (var r in g.Skip(1)) { r.Status = "重复"; r.Issues = "同号且关键字段一致，导出自动排除"; }
                else if (g.Any(r => !r.KeepConflict))
                    foreach (var r in g) { r.Status = "冲突"; r.Issues += (r.Issues == "" ? "" : "；") + "同号票据字段不同，请修订、移除或逐项确认保留"; }
            }
        }
        public static List<Ticket> Prepare(IList<Ticket> rows)
        {
            Reconcile(rows);
            if (rows.Any(r => r.Status != "就绪" && r.Status != "重复")) throw new ArgumentException("请先核对或移除待处理项目");
            var active = rows.Where(r => r.Status == "就绪").ToList();
            if (!active.Any(r => !r.Attachment)) throw new ArgumentException("请至少确认一张发票");
            for (int i = 0; i < active.Count; i++)
                foreach (var b in active.Skip(i + 1))
                {
                    var a = active[i];
                    if (a.SourceHash != b.SourceHash || Math.Max(a.Start, b.Start) > Math.Min(a.End, b.End)) continue;
                    var ca = a.Crop ?? new double[] { 0, 0, 1, 1 }; var cb = b.Crop ?? new double[] { 0, 0, 1, 1 };
                    if (Math.Max(ca[0], cb[0]) < Math.Min(ca[2], cb[2]) - 0.001 && Math.Max(ca[1], cb[1]) < Math.Min(ca[3], cb[3]) - 0.001)
                        throw new ArgumentException(a.Filename + " 的页范围或裁剪区域重叠，请调整或移除多余记录");
                }
            return active;
        }
        public static IOrderedEnumerable<Ticket> Sort(IEnumerable<Ticket> rows) => rows.OrderBy(r => r.Date).ThenBy(r => r.Travel).ThenBy(r => r.Number, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal);
        public static string Hash(string path) { using (var f = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", ""); }
        public static void CheckSource(Ticket r)
        {
            if (!File.Exists(r.Source) || Hash(r.Source) != r.SourceHash) throw new IOException(r.Filename + " 原文件已变化或丢失，请重新导入");
            if (!File.Exists(r.Pdf) || Hash(r.Pdf) != r.PdfHash) throw new IOException(r.Filename + " 预览缓存已变化或丢失，请重新导入");
        }
        public static string SafeName(string s)
        {
            var n = Regex.Replace(s, "[<>:\"/\\\\|?*\\x00-\\x1f]", "_").Trim(' ', '.');
            if (n.Length > 70) n = n.Substring(0, 70);
            if (n == "") n = "其他";
            if (Regex.IsMatch(n.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase)) n = "_" + n;
            return n;
        }
        public static bool Inside(string path, string parent)
        {
            var p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return (Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).StartsWith(p, StringComparison.OrdinalIgnoreCase);
        }
    }
}
