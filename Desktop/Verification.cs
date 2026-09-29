using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AOI.PTH.Desktop.Recipes;
using AOI.PTH.Desktop.Security;
using AOI.PTH.Desktop.ViewModels;
using OpenCvSharp;
using Window = System.Windows.Window;

namespace AOI.PTH.Desktop;

internal static class Verification
{
    public static void Run(string output)
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        string isolated = Path.Combine(output, "test-data-" + Guid.NewGuid().ToString("N"));
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); passed.Add(name); }
        void Reject(Action action, string name) { bool rejected = false; try { action(); } catch (Exception ex) when (ex is InvalidDataException or UnauthorizedAccessException) { rejected = true; } Check(rejected, name); }
        var auth = new AccessService(Path.Combine(isolated, "access"));
        Check(auth.NeedsSetup, "Primeiro acesso requer administrador");
        Reject(() => auth.Bootstrap("admin", "1234"), "Senha curta recusada");
        Reject(() => auth.Bootstrap("admin", "1234a"), "Senha não numérica recusada");
        auth.Bootstrap("admin", "00123");
        Check(!auth.Authenticate("admin", "123"), "Zeros iniciais fazem parte da senha");
        Check(auth.Authenticate("ADMIN", "00123"), "Login sem diferença de caixa e senha com zeros");
        Check(!File.ReadAllText(Path.Combine(isolated, "access", "usuarios.json")).Contains("00123"), "Senha não persistida em texto");
        var admin = auth.Users().Single(); var adminProfile = auth.Profiles().Single();
        Reject(() => auth.SaveUser(admin.Id, admin.Login, admin.ProfileId, false, ""), "Último administrador não pode ser desativado");
        Reject(() => auth.SaveProfile(adminProfile.Id, "Administrador", ["view.inspection"]), "Último perfil administrador protegido");
        auth.SaveProfile(null, "Consulta", ["view.recipes"]);
        var readProfile = auth.Profiles().Single(p => p.Name == "Consulta");
        Reject(() => auth.SaveProfile(null, "Inválido", ["view.recipes", "inspection.photo"]), "Ação requer sua aba");
        auth.SaveUser(null, "operador", readProfile.Id, true, "54321");
        Reject(() => auth.SaveUser(null, "OPERADOR", readProfile.Id, true, "54321"), "Login duplicado recusado");
        var operatorId = auth.Users().Single(u => u.Login == "operador").Id;
        var second = new AccessService(Path.Combine(isolated, "access"));
        Check(second.Authenticate("operador", "54321"), "Conta persiste em nova instância");
        var vm = new ShellViewModel(second);
        Check(vm.Page == 0 && vm.Navigation.Count(n => n.Allowed) == 2, "Inspeção comum e aba autorizada visíveis");
        vm.Page = 6; Check(vm.Page == 0, "Navegação direta não contorna permissão");
        bool invoked = false; vm.RecipeActionRequested += _ => invoked = true;
        vm.EditRecipe.Execute("Criar receita"); Check(!invoked, "Execução direta de comando bloqueada");
        Reject(() => second.SaveProfile(null, "Escalada", AccessRules.All), "Serviço bloqueia escalada de privilégio");
        auth.SaveProfile(readProfile.Id, "Consulta", ["view.recipes", "recipe.import"]);
        Check(second.Can("recipe.import") && !second.Can("recipe.create"), "Ações concedidas individualmente");
        auth.SaveUser(operatorId, "operador", readProfile.Id, false, "");
        Check(!second.Can("recipe.import"), "Desativação revoga sessão existente");
        auth.SaveUser(operatorId, "operador", readProfile.Id, true, "65432");
        for (int i = 0; i < 5; i++) Check(!second.Authenticate("operador", "00000"), $"Tentativa incorreta {i+1}");
        Check(!second.Authenticate("operador", "65432"), "Bloqueio após cinco tentativas");
        auth.SaveUser(operatorId, "operador", readProfile.Id, true, "65432");
        Check(second.Authenticate("operador", "65432"), "Redefinição de senha desbloqueia conta");
        second.Logout(); Check(!second.Can("view.recipes"), "Logout encerra autorização");

        Check(!second.Can("inspection.judge"), "Julgamento exige sessão ativa");
        auth.SaveUserWithPermissions(null, "novo", true, "12345", []);
        var created = auth.Users().Single(u => u.Login == "novo");
        Check(second.Authenticate("novo", "12345") && second.Can("inspection.judge") && second.Can("view.inspection"), "Cadastro unificado concede julgamento");
        Check(!second.Can("view.recipes") && !second.Can("access.manage"), "Cadastro mínimo não concede acessos extras");
        auth.SaveUserWithPermissions(created.Id, "renomeado", true, "", ["recipe.create"]);
        Check(second.Can("recipe.create") && second.Can("view.recipes"), "Edição concede ação e aba correspondente");
        Check(second.Authenticate("renomeado", "12345"), "Edição preserva senha vazia");
        auth.SaveUserWithPermissions(operatorId, "operador", true, "", ["view.history"]);
        Check(auth.Profiles().Single(p => p.Id == readProfile.Id).Grants.Contains("recipe.import"), "Edição individual preserva perfil compartilhado");
        Reject(() => auth.SaveUserWithPermissions(admin.Id, "admin", true, "", []), "Cadastro unificado protege último administrador");
        Check(auth.Can("access.manage"), "Falha de edição mantém permissões anteriores");
        Reject(() => auth.SaveUserWithPermissions(null, "RENOMEADO", true, "12345", []), "Cadastro unificado rejeita login duplicado");
        auth.SaveUserWithPermissions(created.Id, "renomeado", false, "", []);
        Check(!second.Can("inspection.judge"), "Desativação revoga também julgamento comum");
        second.Logout();
        using var clean = new Mat(128, 128, MatType.CV_8UC3, new Scalar(30, 30, 30));
        using var assembled = clean.Clone(); Cv2.Rectangle(assembled, new OpenCvSharp.Rect(32, 32, 32, 32), Scalar.White, -1);
        using var mask = new Mat(128, 128, MatType.CV_8UC1, Scalar.White);
        var roi = new RecipeRoi { Reference = "R1", X = 28, Y = 28, Width = 40, Height = 40 };
        var document = new RecipeDocument { Model = "SYNTHETIC", BoardRevision = "A", Width = 128, Height = 128, RoiReviewConfirmed = true,
            Alignment = new(20, 20, 0, 1, [1,0,0,0,1,0,0,0,1]), Rois = [roi] };
        var files = new RecipeFiles(document, RecipeImages.Png(clean), RecipeImages.Png(assembled), RecipeImages.Png(assembled), RecipeImages.Png(mask));
        var recipes = new RecipeStore(Path.Combine(isolated, "recipes"), auth);
        Reject(() => new RecipeStore(Path.Combine(isolated, "unauthorized"), second).Save(files), "Serviço de receitas bloqueia gravação sem sessão");
        var saved = recipes.Save(files); var loaded = recipes.Load(saved);
        Check(loaded.Document.Rois.Single().Reference == "R1", "Receita salva e reaberta");
        string package = Path.Combine(isolated, "sample.aoireceita"); recipes.Export(saved, package);
        var importedStore = new RecipeStore(Path.Combine(isolated, "imported"), auth); var imported = importedStore.Import(package);
        Check(importedStore.Load(imported).Clean.SequenceEqual(files.Clean), "Exportação/importação preserva referência");
        Check(importedStore.Import(package).Document.Id == document.Id, "Importação repetida é idempotente");
        Check(RecipeInspection.Evaluate(clean, assembled, assembled, mask, roi).Result == "PRESENTE", "ROI sintética presente");
        Check(RecipeInspection.Evaluate(clean, assembled, clean, mask, roi).Result == "AUSENTE", "ROI sintética ausente");
        Check(RecipeInspection.Evaluate(clean, clean, clean, mask, roi).Result == "REVISÃO MANUAL", "Referências sem contraste são inconclusivas");
        using var partial = mask.Clone(); Cv2.Rectangle(partial, new OpenCvSharp.Rect(28, 28, 20, 40), Scalar.Black, -1);
        Check(RecipeInspection.Evaluate(clean, assembled, assembled, partial, roi).Result == "REVISÃO MANUAL", "Cobertura insuficiente não decide ausência");
        Reject(() => RecipeStore.ValidateFiles(files with { ValidMask = RecipeImages.Png(partial) }), "Máscara inconsistente recusada");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Update)) { var entry = zip.GetEntry("receita.json")!; entry.Delete(); using var writer = new StreamWriter(zip.CreateEntry("receita.json").Open()); writer.Write("{}"); }
        Reject(() => new RecipeStore(Path.Combine(isolated, "tampered"), auth).Import(package), "Pacote adulterado recusado");

        // Full alignment -> generation -> persistence -> inspection on a deterministic textured board.
        using var textured = new Mat(512,512,MatType.CV_8UC3, new Scalar(40,40,40));
        var random = new Random(42);
        for (int i=0;i<500;i++) { int x=random.Next(12,495), y=random.Next(12,495); Cv2.Circle(textured,new OpenCvSharp.Point(x,y),random.Next(2,5),new Scalar(random.Next(80,230),random.Next(80,230),random.Next(80,230)),-1); }
        using var populated = textured.Clone(); Cv2.Rectangle(populated,new OpenCvSharp.Rect(230,230,40,40),Scalar.White,-1);
        var generated = RecipeImages.Generate(RecipeImages.Png(textured),RecipeImages.Png(populated),new GenerationSettings());
        Check(generated.Document.Rois.Count > 0, "Geração completa encontra ROI sintética");
        generated.Document.Model="TEXTURED"; generated.Document.BoardRevision="A"; generated.Document.RoiReviewConfirmed=true;
        var texturedRecipe = recipes.Save(generated);
        var assessed = RecipeInspection.Inspect(recipes.Load(texturedRecipe),RecipeImages.Png(populated));
        Check(assessed.Regions.Any(r=>r.Result=="PRESENTE"), "Foto de teste alinhada e avaliada pelo fluxo completo");

        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var window = new MainWindow(auth, recipes) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        window.Show();
        for (int page = 0; page < 7; page++) { window.Model.Page = page; Capture(window, Path.Combine(output, $"{page:00}-interface.png")); }
        window.Model.SetActive(saved, loaded); window.Model.Assessments.Add(RecipeInspection.Evaluate(clean, assembled, assembled, mask, roi)); Capture(window, Path.Combine(output, "07-receita.png"));
        var editor = new RecipeEditorWindow(recipes, () => true, loaded, 2, "admin") { Owner = window, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        editor.Show(); Capture(editor, Path.Combine(output, "08-editor.png"));
        // Avoid asking to discard a verification-only draft during shutdown.
        editor.Hide();
        var accessWindow = new AccessWindow(auth) { Owner = window, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        accessWindow.Show(); Capture(accessWindow, Path.Combine(output, "09-acesso.png")); accessWindow.Close();
        var loginWindow = new LoginWindow(auth) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000 };
        loginWindow.Show(); Capture(loginWindow, Path.Combine(output, "10-login.png")); loginWindow.Close();
        File.WriteAllText(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new { result = "PASS", checksPassed = passed.Count, checks = passed, note = "Imagens sintéticas validam lógica, não precisão em placas reais." }, new JsonSerializerOptions { WriteIndented = true }));
        // Process exits immediately after verification; no real user library is used.
        Environment.Exit(0);
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var surface = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen()) { dc.DrawRectangle(window.Background ?? Brushes.White, null, new System.Windows.Rect(0,0,surface.ActualWidth,surface.ActualHeight)); dc.DrawRectangle(new VisualBrush(surface), null, new System.Windows.Rect(0,0,surface.ActualWidth,surface.ActualHeight)); }
        bitmap.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
