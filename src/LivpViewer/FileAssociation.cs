using Microsoft.Win32;

namespace LivpViewer;

internal static class FileAssociation
{
    public static void Register()
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定 LivpViewer.exe 路径。");

        using var fileType = Registry.CurrentUser.CreateSubKey(@"Software\Classes\LivpViewer.livp")
            ?? throw new InvalidOperationException("无法创建文件类型注册表项。");
        fileType.SetValue("", "LIVP Live Photo");

        using var command = Registry.CurrentUser.CreateSubKey(@"Software\Classes\LivpViewer.livp\shell\open\command")
            ?? throw new InvalidOperationException("无法创建打开命令注册表项。");
        command.SetValue("", $"\"{executable}\" \"%1\"");

        using var extension = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.livp")
            ?? throw new InvalidOperationException("无法创建 .livp 注册表项。");
        extension.SetValue("", "LivpViewer.livp");
    }
}
