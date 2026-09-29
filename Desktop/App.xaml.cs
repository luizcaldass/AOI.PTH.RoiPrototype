using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AOI.PTH.Desktop.ViewModels;

namespace AOI.PTH.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (e.Args.Length == 2 && e.Args[0] == "--verify-ui")
        {
            try { Verification.Run(e.Args[1]); Shutdown(0); }
            catch (Exception ex) { Directory.CreateDirectory(e.Args[1]); File.WriteAllText(Path.Combine(e.Args[1], "verification-error.txt"), ex.ToString()); Shutdown(1); }
            return;
        }
        SignIn(new Security.AccessService(Security.AccessService.DefaultRoot));
    }
    public void SignIn(Security.AccessService access)
    {
        try {
            if (new Security.LoginWindow(access).ShowDialog() != true) { Shutdown(); return; }
            var window = new MainWindow(access); MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose; window.Show();
        } catch (Exception ex) { MessageBox.Show("Não foi possível abrir a estação: " + ex.Message, "AOI PTH"); Shutdown(1); }
    }

    private static void Capture(MainWindow window, string path)
    {
        var surface = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

}
