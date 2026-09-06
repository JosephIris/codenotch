using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Codenotch;

internal static class AppBrand
{
    public static ImageSource Image { get; } = BitmapFrame.Create(new Uri("pack://application:,,,/Codenotch;component/Assets/AppIcon.png"));

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(string appId);

    public static void SetTaskbarIdentity(bool preview) => SetCurrentProcessExplicitAppUserModelID(preview ? "JosephIris.Codenotch.Preview" : "JosephIris.Codenotch.Windows");

    public static System.Drawing.Icon TrayIcon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Codenotch.Assets.Codenotch.ico")!;
        using var icon = new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
        return (System.Drawing.Icon)icon.Clone();
    }
}
