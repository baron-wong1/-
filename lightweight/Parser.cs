using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace InvoiceAssistant
{
    public static class Parser
    {
        const string Amount = @"(\d{1,3}(?:,\d{3})+(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)(?![\d.,])";
        public static void Apply(Ticket r, string text, string folder = "")
        {
            text = (text ?? "").Normalize(NormalizationForm.FormKC).Replace('﹕', ':');
            var compact = Regex.Replace(text, @"[ \t]", "");
            var numbers = Regex.Matches(text, @"(?:发\s*票\s*(?:号\s*码|号)|票据号码)\s*[:：]?\s*((?:\d[ \t]*){8,30})(?!\d)").Cast<Match>().Select(m => Regex.Replace(m.Groups[1].Value, @"\s", "")).Distinct().ToArray();
            if (numbers.Length == 0) numbers = Regex.Matches(compact, @"(?<!\d)\d{20}(?!\d)").Cast<Match>().Select(m => m.Value).Distinct().ToArray();
            r.Number = numbers.Length == 1 ? numbers[0] : "";
            r.RequiresSplit = numbers.Length > 1;
            if (r.RequiresSplit) Warn(r, "本页出现多个发票号码，请框选拆分并逐张核对");
            var label = Regex.Match(compact, @"开票日期[:：]?([^\n]{0,40}(?:\n[^\n]{0,30})?)");
            foreach (Match m in Regex.Matches(label.Success ? label.Groups[1].Value : compact, @"(?<!\d)(20\d{2})[年/.-](\d{1,2})[月/.-](\d{1,2})日?"))
            {
                try { r.Date = new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)).ToString("yyyy-MM-dd"); if (!label.Success) Warn(r, "日期未带开票标签，请核对"); break; } catch (ArgumentOutOfRangeException) { }
            }
            string[] amounts = Array.Empty<string>();
            foreach (var p in new[] { @"小写[)）]?[:：]?\s*[¥￥]?\s*", @"价税合计[^\d¥￥\n]{0,15}[:：]?\s*[¥￥]?\s*", @"(?:票价|退票费|实收金额|合计金额|含税金额)[:：]?\s*[¥￥]?\s*" })
            {
                amounts = Regex.Matches(compact, p + Amount).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToArray();
                if (amounts.Length != 0) break;
            }
            if (amounts.Length == 0)
            {
                amounts = Regex.Matches(compact, @"[¥￥]\s*" + Amount).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToArray();
                if (amounts.Length == 1) Warn(r, "未找到金额标签，请核对价税合计或票价");
            }
            if (amounts.Length == 1) { try { r.Amount = Rules.Money(amounts[0])?.ToString("0.00", CultureInfo.InvariantCulture) ?? ""; } catch (ArgumentException) { } }
            else if (amounts.Length > 1) Warn(r, "本页存在多个候选金额，请核对");
            if (r.RequiresSplit) { r.Date = ""; r.Amount = ""; }
            r.Attachment = numbers.Length == 0 && Has(compact, "支付成功", "付款成功", "交易详情", "转账成功", "微信支付", "支付宝");
            if (r.Attachment) Warn(r, "疑似付款凭证，作为附件不计入金额，请确认");
            r.Category = Classify(compact, folder);
            var travel = Regex.Match(text, @"20\d{2}[-/]\d{1,2}[-/]\d{1,2}\s+\d{1,2}:\d{2}");
            if (travel.Success && DateTime.TryParse(travel.Value, out var dt)) r.Travel = dt.ToString("yyyy-MM-ddTHH:mm");
        }
        public static bool Has(string s, params string[] words) => words.Any(s.Contains);
        public static string Classify(string text, string folder)
        {
            if (Has(text, "退票", "退票费")) return "退票费";
            if (Has(text, "住宿", "酒店", "宾馆", "客房", "房费")) return "住宿";
            if (Has(text, "铁路", "高铁", "动车", "航空", "机票", "民航", "火车")) return "车票";
            if (Has(text, "出租", "网约车", "滴滴", "市内交通", "地铁", "公交")) return "市交";
            return Rules.Categories.FirstOrDefault(c => c != "其他" && folder.Contains(c)) ?? "其他";
        }
        public static void Warn(Ticket r, string s) { if (r.Warning != "") r.Warning += "；"; r.Warning += s; }
    }
}
