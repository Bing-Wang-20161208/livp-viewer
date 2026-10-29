using LibVLCSharp.Shared;
using System.Windows.Forms;

namespace LivpViewer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            try { SelfTest.Run(); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        if (args.Contains("--register", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                FileAssociation.Register();
                MessageBox.Show("已注册当前用户的 LIVP 打开方式。若 Windows 保留已有默认应用，请右键 LIVP 文件，在“打开方式”中选择 LIVP Viewer 并勾选“始终使用”。",
                    "LIVP Viewer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show("注册文件关联失败：" + ex.Message, "LIVP Viewer",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
        try
        {
            Core.Initialize();
            Application.Run(new MainForm(args.FirstOrDefault()));
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法启动 LIVP Viewer：\n{ex.Message}", "LIVP Viewer",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
