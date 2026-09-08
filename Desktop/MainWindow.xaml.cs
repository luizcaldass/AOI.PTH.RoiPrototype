using System.Windows;
using AOI.PTH.Desktop.ViewModels;
using AOI.PTH.Desktop.Recipes;
using Microsoft.Win32;
using System.IO;
using AOI.PTH.RoiPrototype.Vision;
using AOI.PTH.RoiPrototype.Options;
using OpenCvSharp;

namespace AOI.PTH.Desktop;

public partial class MainWindow : Window
{
    public ShellViewModel Model { get; } = new();
    private readonly RecipeStore recipeStore;
    private RecipeFiles? activeFiles;
    public MainWindow(RecipeStore? store = null)
    {
        InitializeComponent(); DataContext = Model; recipeStore = store ?? new(RecipeStore.DefaultRoot);
        Model.RecipeActionRequested += RecipeAction;
        ReloadRecipes();
        Closing += (_, args) => { if (Model.RecipeBusy) { args.Cancel = true; Model.Notice = "Aguarde a operação da receita terminar antes de fechar."; } };
    }
    private void ReloadRecipes(LocalRecipe? selected = null)
    {
        try
        {
            var items = recipeStore.List(out string warning);
            Model.Recipes.Clear(); foreach (var item in items) Model.Recipes.Add(item);
            Model.SelectedRecipe = Model.Recipes.FirstOrDefault(r => selected is not null && r.Document.Id == selected.Document.Id && r.Document.Version == selected.Document.Version) ?? Model.Recipes.FirstOrDefault();
            if (warning.Length > 0) Model.Notice = warning;
        }
        catch (Exception ex) { Model.Notice = "Não foi possível ler a biblioteca de receitas: " + ex.Message; }
    }
    private async void RecipeAction(string action)
    {
        // Recheck permissions at the entry point. Profile selection is still a demo identity.
        var command = action == "Ativar receita" ? Model.ActivateRecipe : action == "Carregar foto de teste" ? Model.LoadTestPhoto : Model.EditRecipe;
        if (!command.CanExecute(action)) return;
        try
        {
            if (action == "Importar receita")
            {
                var dialog = new OpenFileDialog { Title = "Importar receita por imagens", Filter = "Receita AOI (*.aoireceita)|*.aoireceita" };
                if (dialog.ShowDialog(this) != true) return;
                Model.RecipeBusy = true; Model.Notice = "Validando e importando receita…";
                var imported = await Task.Run(() => recipeStore.Import(dialog.FileName));
                ReloadRecipes(imported); Model.Page = 1;
                Model.Notice = $"{imported.Display} disponível na biblioteca local. Selecione Usar receita para carregar as referências.";
            }
            else if (action is "Criar receita" or "Editor de ROIs" or "Validar versão")
            {
                RecipeFiles? original = null; int nextVersion = 1;
                if (action != "Criar receita")
                {
                    var selected = Model.SelectedRecipe ?? throw new InvalidOperationException("Selecione uma receita.");
                    Model.RecipeBusy = true;
                    original = await Task.Run(() => recipeStore.Load(selected));
                    nextVersion = checked(Model.Recipes.Where(r => r.Document.Id == selected.Document.Id).Max(r => r.Document.Version) + 1);
                    Model.RecipeBusy = false;
                }
                var editor = new RecipeEditorWindow(recipeStore, () => Model.CanEdit, original, nextVersion) { Owner = this };
                if (editor.ShowDialog() == true && editor.Saved is LocalRecipe saved)
                { ReloadRecipes(saved); Model.Page = 1; Model.Notice = $"Receita {saved.Display} salva. Você pode usá-la nesta estação ou exportar o pacote .aoireceita."; }
            }
            else if (action == "Exportar receita")
            {
                var selected = Model.SelectedRecipe ?? throw new InvalidOperationException("Selecione uma receita.");
                var safeName = string.Concat(selected.Code.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                var dialog = new SaveFileDialog { Title = "Exportar receita", Filter = "Receita AOI (*.aoireceita)|*.aoireceita", DefaultExt = ".aoireceita", AddExtension = true, FileName = $"{safeName}_v{selected.Document.Version}.aoireceita" };
                if (dialog.ShowDialog(this) != true) return;
                Model.RecipeBusy = true;
                await Task.Run(() => recipeStore.Export(selected, dialog.FileName));
                Model.Notice = $"Pacote exportado: {dialog.FileName}";
            }
            else if (action == "Ativar receita")
            {
                var selected = Model.SelectedRecipe ?? throw new InvalidOperationException("Selecione uma receita.");
                Model.RecipeBusy = true; Model.Notice = "Verificando referências e integridade…";
                var files = await Task.Run(() => recipeStore.Load(selected));
                Model.SetActive(selected, files); activeFiles = files;
                Model.Notice = $"{selected.Display} ativa para testes com fotos. Julgamento automático e produção ainda não integrados.";
            }
            else if (action == "Carregar foto de teste" && activeFiles is not null)
            {
                var dialog = new OpenFileDialog { Filter = "Fotos (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", Title = "Carregar PCI em teste (mesmo modelo e lado)" };
                if (dialog.ShowDialog(this) != true) return;
                Model.RecipeBusy = true;
                Model.Notice = "Alinhando foto de teste à referência…";
                var reference = activeFiles.Assembled;
                var result = await Task.Run(() =>
                {
                    var bytes = RecipeImages.ReadPhoto(dialog.FileName);
                    using var a = Cv2.ImDecode(bytes, ImreadModes.Color);
                    using var r = Cv2.ImDecode(reference, ImreadModes.Color);
                    using var alignment = new BoardAlignmentService().Align(r, a, new PrototypeOptions("", "", ""));
                    double coverage = Cv2.CountNonZero(alignment.ValidAreaMask) / (double)(r.Width * r.Height);
                    if (coverage < .75) throw new InvalidDataException("A foto cobre menos de 75% da referência. Verifique o enquadramento.");
                    return (Image: RecipeImages.Bitmap(RecipeImages.Png(alignment.AlignedImage)), Error: alignment.MeanReprojectionErrorPixels);
                });
                Model.SetTestPhoto(result.Image, $"{Path.GetFileName(dialog.FileName)} • alinhada • erro médio {result.Error:F2} px. ROIs sobrepostas para conferência visual; sem decisão OK/NOK.");
                Model.Notice = "Foto alinhada às ROIs da receita. Esta etapa confere o posicionamento; não calcula defeitos.";
            }
        }
        catch (Exception ex) { Model.Notice = $"Operação não concluída: {ex.Message} A receita ativa anterior foi preservada."; }
        finally { Model.RecipeBusy = false; }
    }
    private void ExitClick(object sender, RoutedEventArgs e) => Close();
}
