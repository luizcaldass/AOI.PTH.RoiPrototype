using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using AOI.PTH.Desktop.ViewModels;

namespace AOI.PTH.Desktop.Recipes;

public sealed class RecipeEditorModel : Observable
{
    public string ModelName { get; set; } = "";
    public string Revision { get; set; } = "A";
    public string Threshold { get; set; } = "32";
    public string MinArea { get; set; } = "250";
    public string Padding { get; set; } = "10";
    public string MergeDistance { get; set; } = "16";
    public string VersionLabel { get; set; } = "NOVA RECEITA • v1";
    public ObservableCollection<RecipeRoi> Rois { get; } = [];
    private RecipeRoi? selected;
    public RecipeRoi? Selected { get => selected; set { selected = value; Changed(); } }
    private bool drawing;
    public bool Drawing { get => drawing; set { drawing = value; Changed(); } }
    private bool reviewed;
    public bool Reviewed { get => reviewed; set { reviewed = value; Changed(); } }
    private bool busy;
    public bool Busy { get => busy; set { busy = value; Changed(nameof(Ready)); } }
    public bool Ready => !Busy;
    public bool HasPrepared { get; set; }
    public BitmapSource? Preview { get; set; }
    public string CleanLabel { get; set; } = "Nenhuma imagem selecionada";
    public string AssembledLabel { get; set; } = "Nenhuma imagem selecionada";
    private string message = "Carregue duas fotos do mesmo modelo, lado e revisão. A montada será alinhada sobre a limpa.";
    public string Message { get => message; set { message = value; Changed(); } }
    public void Refresh() { foreach (var name in new[] { nameof(Selected), nameof(Preview), nameof(HasPrepared), nameof(CleanLabel), nameof(AssembledLabel), nameof(VersionLabel) }) Changed(name); }
    public GenerationSettings Settings()
    {
        if (!int.TryParse(Threshold, out int t) || !int.TryParse(MinArea, out int a) || !int.TryParse(Padding, out int p) || !int.TryParse(MergeDistance, out int m)) throw new InvalidDataException("Os parâmetros de geração devem ser números inteiros.");
        var settings = new GenerationSettings(t, a, p, m); RecipeImages.ValidateSettings(settings); return settings;
    }
}

public partial class RecipeEditorWindow : Window
{
    public RecipeEditorModel Model { get; } = new();
    private readonly RecipeStore store;
    private readonly Func<bool> allowed;
    private RecipeFiles? prepared;
    private byte[]? clean, assembled;
    private Guid id = Guid.NewGuid();
    private int version = 1;
    private readonly string author;
    public LocalRecipe? Saved { get; private set; }
    public RecipeEditorWindow(RecipeStore store, Func<bool> allowed, RecipeFiles? existing = null, int nextVersion = 1, string author = "")
    {
        InitializeComponent(); this.store = store; this.allowed = allowed; this.author = author;
        if (existing is not null)
        {
            prepared = existing; clean = existing.Clean; assembled = existing.OriginalAssembled; id = existing.Document.Id; version = nextVersion;
            Model.ModelName = existing.Document.Model; Model.Revision = existing.Document.BoardRevision;
            Model.Threshold = existing.Document.Generation.Threshold.ToString(); Model.MinArea = existing.Document.Generation.MinArea.ToString();
            Model.Padding = existing.Document.Generation.Padding.ToString(); Model.MergeDistance = existing.Document.Generation.MergeDistance.ToString();
            Model.VersionLabel = $"NOVA VERSÃO • v{version}"; Model.CleanLabel = "Referência da versão anterior"; Model.AssembledLabel = "Original da versão anterior";
            SetPrepared(existing); Model.Message = "Revise e salve uma nova versão. A receita anterior será preservada.";
        }
        DataContext = Model;
        Model.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(Model.Drawing)) ImageCanvas.AllowDrawing = Model.Drawing && Model.HasPrepared && Model.Ready; };
        ImageCanvas.RegionDrawn += rect =>
        {
            if (!allowed() || !Model.Ready || !Model.HasPrepared) return;
            int n = 1; while (Model.Rois.Any(r => r.Reference.Equals($"ROI{n}", StringComparison.OrdinalIgnoreCase))) n++;
            var roi = new RecipeRoi { Reference = $"ROI{n}", X = (int)Math.Floor(rect.X), Y = (int)Math.Floor(rect.Y), Width = Math.Max(2, (int)Math.Floor(rect.Width)), Height = Math.Max(2, (int)Math.Floor(rect.Height)) };
            Hook(roi); Model.Rois.Add(roi); Model.Selected = roi; Model.Reviewed = false; Model.Refresh();
        };
    }
    private void Hook(RecipeRoi roi) => roi.PropertyChanged += (_, _) => Model.Reviewed = false;
    private void SetPrepared(RecipeFiles files)
    {
        prepared = files; Model.Rois.Clear();
        foreach (var roi in files.Document.Rois) { Hook(roi); Model.Rois.Add(roi); }
        Model.Preview = RecipeImages.Bitmap(files.Assembled); Model.HasPrepared = true; Model.Selected = Model.Rois.FirstOrDefault(); Model.Reviewed = false; Model.Refresh();
    }
    private async void ChooseClean(object sender, RoutedEventArgs args) => await Choose(true);
    private async void ChooseAssembled(object sender, RoutedEventArgs args) => await Choose(false);
    private async Task Choose(bool isClean)
    {
        if (!allowed() || !Model.Ready) return;
        var dialog = new OpenFileDialog { Filter = "Fotos da placa (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", Title = isClean ? "Selecionar placa limpa" : "Selecionar placa montada" };
        if (dialog.ShowDialog(this) != true) return;
        if (prepared is not null && MessageBox.Show(this, "Trocar a foto descarta o alinhamento e as ROIs deste rascunho. Continuar?", "Trocar referência", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Model.Busy = true; ImageCanvas.AllowDrawing = false;
        try
        {
            var image = await Task.Run(() => RecipeImages.ReadPhoto(dialog.FileName));
            if (isClean) { clean = image; Model.CleanLabel = Path.GetFileName(dialog.FileName); }
            else { assembled = image; Model.AssembledLabel = Path.GetFileName(dialog.FileName); }
            prepared = null; Model.HasPrepared = false; Model.Preview = RecipeImages.Bitmap(image); Model.Rois.Clear(); Model.Reviewed = false;
            Model.Message = "Imagem carregada. Clique em Alinhar e gerar ROIs para preparar as referências."; Model.Refresh();
        }
        catch (Exception ex) { Model.Message = "Não foi possível carregar a foto: " + ex.Message; }
        finally { Model.Busy = false; ImageCanvas.AllowDrawing = Model.Drawing && Model.HasPrepared; }
    }
    private async void Generate(object sender, RoutedEventArgs args)
    {
        if (!allowed() || !Model.Ready) return;
        if (clean is null || assembled is null) { Model.Message = "Selecione a placa limpa e a placa montada."; return; }
        if (prepared is not null && MessageBox.Show(this, "Gerar novamente substitui as ROIs ajustadas neste rascunho. Continuar?", "Gerar ROIs", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var settings = Model.Settings();
            Model.Busy = true; ImageCanvas.AllowDrawing = false; Model.Message = "Alinhando imagens e gerando regiões…";
            var files = await Task.Run(() => RecipeImages.Generate(clean, assembled, settings));
            SetPrepared(files);
            Model.Message = $"{Model.Rois.Count} ROIs candidatas. Erro médio: {files.Document.Alignment!.ErrorPixels:F2} px; sobreposição: {files.Document.Alignment.Coverage:P0}. Revise os nomes e as caixas. Você também pode desenhar regiões.";
        }
        catch (Exception ex) { Model.Message = "Geração não concluída: " + ex.Message + " O rascunho anterior foi preservado."; }
        finally { Model.Busy = false; ImageCanvas.AllowDrawing = Model.Drawing && Model.HasPrepared; }
    }
    private void RoiEdited(object sender, DataGridCellEditEndingEventArgs args) => Model.Reviewed = false;
    private void RemoveRoi(object sender, RoutedEventArgs args)
    {
        if (!allowed() || !Model.Ready || Model.Selected is null) return;
        Model.Rois.Remove(Model.Selected); Model.Selected = Model.Rois.FirstOrDefault(); Model.Reviewed = false; Model.Refresh();
    }
    private async void Save(object sender, RoutedEventArgs args)
    {
        if (!allowed() || !Model.Ready || prepared is null) return;
        if (!RoiGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !RoiGrid.CommitEdit(DataGridEditingUnit.Row, true) || HasError(this)) { Model.Message = "Corrija os campos inválidos na tabela de ROIs."; return; }
        try
        {
            if (Model.Settings() != prepared.Document.Generation) throw new InvalidDataException("Os parâmetros foram alterados. Gere as ROIs novamente antes de salvar.");
            var document = new RecipeDocument { Id = id, Version = version, Author = author, Model = Model.ModelName.Trim(), BoardRevision = Model.Revision.Trim(), Width = prepared.Document.Width, Height = prepared.Document.Height, Alignment = prepared.Document.Alignment, Generation = prepared.Document.Generation, RoiReviewConfirmed = Model.Reviewed, Rois = Model.Rois.ToList() };
            var files = prepared with { Document = document };
            RecipeStore.ValidateFiles(files);
            Model.Busy = true; ImageCanvas.AllowDrawing = false;
            Saved = await Task.Run(() => { if (!allowed()) throw new UnauthorizedAccessException("Permissão de edição revogada."); return store.Save(files); });
            Model.Busy = false;
            DialogResult = true;
        }
        catch (Exception ex) { Model.Message = "Receita não salva: " + ex.Message; }
        finally { Model.Busy = false; ImageCanvas.AllowDrawing = Model.Drawing && Model.HasPrepared; }
    }
    private static bool HasError(DependencyObject obj)
    {
        if (Validation.GetHasError(obj)) return true;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++) if (HasError(VisualTreeHelper.GetChild(obj, i))) return true;
        return false;
    }
    private void Cancel(object sender, RoutedEventArgs args) => Close();
    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (!Model.Ready) { args.Cancel = true; return; }
        if (Saved is null && (clean is not null || assembled is not null) && MessageBox.Show(this, "Descartar este rascunho e fechar?", "Receita não salva", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) args.Cancel = true;
    }
}
