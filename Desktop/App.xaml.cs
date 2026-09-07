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
        if (e.Args.Length == 2 && e.Args[0] == "--verify-ui")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                Directory.CreateDirectory(e.Args[1]);
                int checks = VerifyBehavior();
                RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                var window = new MainWindow { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
                window.Show();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                for (int page = 0; page < window.Model.Navigation.Count; page++)
                {
                    window.Model.Page = page;
                    window.UpdateLayout();
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Capture(window, Path.Combine(e.Args[1], $"{page:00}-interface.png"));
                }
                window.Model.Page = 6;
                window.Model.Role = UserRole.Administrador;
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Capture(window, Path.Combine(e.Args[1], "07-admin.png"));
                window.Width = 1280; window.Height = 720; window.Model.Page = 0;
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Capture(window, Path.Combine(e.Args[1], "08-1280x720.png"));
                window.Close();
                File.WriteAllText(Path.Combine(e.Args[1], "verification.json"), JsonSerializer.Serialize(new { checksPassed = checks, renderedScreens = 9, framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, result = "PASS" }, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(0);
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(e.Args[1], "verification-error.txt"), exception.ToString());
                Shutdown(1);
            }
            return;
        }
        new MainWindow().Show();
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

    private static int VerifyBehavior()
    {
        int count = 0;
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException($"Falha: {name}"); count++; }
        var vm = new ShellViewModel();
        Check(vm.Role == UserRole.Operador, "Perfil inicial mínimo");
        Check(!vm.EditRecipe.CanExecute(null) && !vm.Configure.CanExecute(null), "Operador sem edição");
        vm.EditRecipe.Execute("Importar");
        Check(!vm.Notice.StartsWith("Importar"), "Comando protegido também na execução");
        vm.Role = UserRole.Engenharia;
        Check(vm.EditRecipe.CanExecute(null) && vm.EditDefect.CanExecute(null), "Engenharia edita receitas e catálogo");
        Check(!vm.Configure.CanExecute(null) && !vm.ManageUsers.CanExecute(null), "Engenharia sem administração");
        vm.Role = UserRole.Administrador;
        Check(vm.Configure.CanExecute(null) && vm.ManageUsers.CanExecute(null), "Administrador herda permissões");
        vm.Role = UserRole.Operador;
        Check(!vm.Finish.CanExecute(null) && !vm.CancelJudgment.CanExecute(null), "Pendências bloqueiam finalização");
        vm.Judge.Execute("Aceitável");
        Check(vm.Pending == 2 && vm.CancelJudgment.CanExecute(null), "Julgamento em memória");
        vm.CancelJudgment.Execute(null);
        Check(vm.Pending == 3 && vm.Selected.Decision == "Pendente", "Cancelar retorna para pendente");
        foreach (var c in vm.Components) { vm.Selected = c; vm.Judge.Execute("Defeito"); }
        Check(vm.Pending == 0 && vm.Finish.CanExecute(null), "Finalização exige todas as decisões");
        vm.Finish.Execute(null);
        Check(vm.Completed && !vm.Judge.CanExecute(null) && !vm.CancelJudgment.CanExecute(null), "Ciclo encerrado não é editável");
        vm.ResetDemo.Execute(null);
        Check(vm.Pending == 3 && !vm.Completed, "Reiniciar restaura demonstração");
        Check(vm.History.Count == 3, "Simulação não cria produção ou histórico real");
        for (int i = 0; i < 7; i++) { vm.Navigate.Execute(i); Check(vm.Page == i && vm.Title == vm.Navigation[i].Title, "Navegação"); }
        vm.Role = UserRole.Administrador; vm.Judge.Execute("Defeito");
        Check(!vm.EditRecipe.CanExecute(null), "Não alterar receita durante julgamento parcial");
        return count;
    }
}
