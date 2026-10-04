using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace InvoiceAssistant
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 3 && args[0] == "--convert-word") return SystemServices.WordWorker(args[1], args[2]);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--self-test") return SelfTest.Run(args[1]);
            Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            try { Application.Run(new MainForm()); return 0; }
            catch (Exception ex) { MessageBox.Show("无法启动：" + ex.Message, "发票整理助手"); return 1; }
        }
    }
}
