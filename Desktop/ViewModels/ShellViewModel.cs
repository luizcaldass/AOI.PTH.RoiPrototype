using AOI.PTH.Desktop.Security;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AOI.PTH.Desktop.Recipes;

namespace AOI.PTH.Desktop.ViewModels;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class RelayCommand(Action<object?> execute, Predicate<object?>? allowed = null) : ICommand
{
    public bool CanExecute(object? parameter) => allowed?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(parameter); }
    public event EventHandler? CanExecuteChanged;
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed record NavigationItem(string Glyph, string Title, string Subtitle, bool Allowed = false);
public sealed record RecipeRow(string Code, string Revision, string Components, string State);
public sealed record DefectRow(string Code, string Name, string Method, string State);
public sealed record HistoryRow(string Pci, string Model, string Time, string Result, string Operator);
public sealed record PermissionRow(string Action, string Operator, string Engineering, string Administrator);
public sealed class ComponentItem(string reference, string description, int present, int absent, int changedArea, int index) : Observable
{
    public string Reference { get; } = reference;
    public string Description { get; } = description;
    public int Present { get; } = present;
    public int Absent { get; } = absent;
    public int Difference { get; } = changedArea;
    public int Index { get; } = index;
    public string Margin => $"{Math.Abs(Present - Absent)} pontos";
    private string decision = "Pendente";
    public string Decision { get => decision; set { decision = value; Changed(); } }
}

public sealed class ShellViewModel : Observable
{
    public ObservableCollection<NavigationItem> Navigation { get; } = [
        new("\uE9D9", "Inspeção", "Analise cada divergência e revise o julgamento da PCI."),
        new("\uE8A7", "Receitas", "Referências, regiões de interesse e versões por modelo."),
        new("\uE7BA", "Defeitos", "Catálogo de ocorrências e critérios por componente."),
        new("\uE81C", "Histórico", "Rastreabilidade de placas, decisões e reinspeções."),
        new("\uE9D2", "Produção", "Acompanhe placas únicas e resultados da estação."),
        new("\uE713", "Configurações", "Estação, servidor, armazenamento e sincronização."),
        new("\uE716", "Perfis e acesso", "Responsabilidades de operação, engenharia e administração.")];
    public AccessService Access { get; }
    private int page;
    public int Page { get => page; set { if (value < 0 || value >= Navigation.Count || !Access.Can(AccessRules.View(AccessRules.Pages[value]))) return; page = value; Changed(); Changed(nameof(Title)); Changed(nameof(Subtitle)); Changed(nameof(ShowRecipeWorkspace)); } }
    public string Title => page >= 0 ? Navigation[page].Title : "Sem acesso";
    public string Subtitle => page >= 0 ? Navigation[page].Subtitle : "Solicite acesso ao administrador.";
    public bool CanEdit => Access.Can("recipe.create") || Access.Can("recipe.edit");
    public bool IsAdmin => Access.Can("settings.edit");
    public string RoleNotice => $"{Access.LoginName} • {Access.ProfileName}";
    public event Action? AccessManagementRequested;
    public void RefreshAccess()
    {
        for (int i = 0; i < Navigation.Count; i++) Navigation[i] = Navigation[i] with { Allowed = Access.Can(AccessRules.View(AccessRules.Pages[i])) };
        if (page < 0 || !Navigation[page].Allowed) { page = Navigation.ToList().FindIndex(n => n.Allowed); Changed(nameof(Page)); }
        foreach (var name in new[] { nameof(Title), nameof(Subtitle), nameof(CanEdit), nameof(IsAdmin), nameof(RoleNotice), nameof(ShowRecipeWorkspace) }) Changed(name);
        Refresh();
    }
    public ObservableCollection<ComponentItem> Components { get; } = [
        new("JP3", "Jumper • suspeita de ausência", 31, 89, 42, 0),
        new("R12", "Resistor • aparência divergente", 72, 48, 18, 1),
        new("C07", "Capacitor • verificar posição", 78, 39, 12, 2)];
    private ComponentItem? selected;
    public ComponentItem Selected { get => selected ?? Components[0]; set { selected = value; Changed(); Refresh(); } }
    public int Pending => Components.Count(x => x.Decision == "Pendente");
    public string PendingLabel => $"{Pending:00} pendência(s) de demonstração";
    private string notice = "Selecione uma ROI para explorar o julgamento. Todos os dados desta tela são demonstrativos.";
    public string Notice { get => notice; set { notice = value; Changed(); } }
    private bool completed;
    public bool Completed { get => completed; private set { completed = value; Changed(); Changed(nameof(CycleLabel)); Refresh(); } }
    public string CycleLabel => Completed ? "Demonstração encerrada" : "Aguardando revisão • simulação";
    public ObservableCollection<LocalRecipe> Recipes { get; } = [];
    public event Action<string>? RecipeActionRequested;
    private LocalRecipe? selectedRecipe;
    public LocalRecipe? SelectedRecipe { get => selectedRecipe; set { selectedRecipe = value; Changed(); Refresh(); } }
    private bool recipeBusy;
    public bool RecipeBusy { get => recipeBusy; set { recipeBusy = value; Changed(); Changed(nameof(RecipeReady)); Refresh(); } }
    public bool RecipeReady => !RecipeBusy;
    public LocalRecipe? ActiveRecipe { get; private set; }
    public bool HasActiveRecipe => ActiveRecipe is not null;
    public bool ShowDemo => !HasActiveRecipe;
    public bool ShowRecipeWorkspace => Page == 0 && HasActiveRecipe;
    public string ActiveRecipeLabel => ActiveRecipe?.Display ?? "Nenhuma receita real ativa • demonstração";
    public ObservableCollection<RecipeRoi> ActiveRois { get; } = [];
    private RecipeRoi? selectedActiveRoi;
    public RecipeRoi? SelectedActiveRoi { get => selectedActiveRoi; set { selectedActiveRoi = value; Changed(); } }
    public BitmapSource? RecipeImage { get; private set; }
    public BitmapSource? CleanReference { get; private set; }
    public BitmapSource? AssembledReference { get; private set; }
    public string PhotoStatus { get; private set; } = "Referência montada alinhada • carregue uma foto para conferir as ROIs.";
    public ObservableCollection<RoiAssessment> Assessments { get; } = [];
    public RelayCommand ActivateRecipe { get; }
    public RelayCommand LoadTestPhoto { get; }
    public void SetActive(LocalRecipe item, RecipeFiles files)
    {
        Assessments.Clear(); ActiveRecipe = item; ActiveRois.Clear(); foreach (var roi in files.Document.Rois) ActiveRois.Add(roi);
        SelectedActiveRoi = ActiveRois.FirstOrDefault();
        RecipeImage = AssembledReference = RecipeImages.Bitmap(files.Assembled); CleanReference = RecipeImages.Bitmap(files.Clean);
        PhotoStatus = "Referência montada alinhada • nenhum julgamento automático foi realizado.";
        foreach (var name in new[] { nameof(HasActiveRecipe), nameof(ShowDemo), nameof(ShowRecipeWorkspace), nameof(ActiveRecipeLabel), nameof(RecipeImage), nameof(CleanReference), nameof(AssembledReference), nameof(PhotoStatus) }) Changed(name);
        Page = 0; Refresh();
    }
    public void SetTestPhoto(BitmapSource image, string status) { RecipeImage = image; PhotoStatus = status; Changed(nameof(RecipeImage)); Changed(nameof(PhotoStatus)); }
    public ObservableCollection<DefectRow> Defects { get; } = [new("D001", "Componente ausente", "Presença / ausência", "Planejado"),new("D002", "Jumper ausente", "Geometria da região", "Planejado"),new("D003", "Componente deslocado", "Posição e contorno", "Planejado"),new("D004", "Componente invertido", "Orientação / marcação", "Planejado")];
    public ObservableCollection<HistoryRow> History { get; } = [new("DEMO-0003", "FONTE-24V", "10:42", "Revisão pendente", "Operador demo"),new("DEMO-0002", "FONTE-24V", "10:41", "Reprovada • exemplo", "Operador demo"),new("DEMO-0001", "FONTE-24V", "10:40", "Aprovada • exemplo", "Operador demo")];
    public ObservableCollection<PermissionRow> Permissions { get; } = [new("Inspecionar e julgar", "Permitido", "Permitido", "Permitido"),new("Consultar histórico e produção", "Permitido", "Permitido", "Permitido"),new("Importar / editar receitas", "Consulta", "Permitido", "Permitido"),new("Editar catálogo de defeitos", "Consulta", "Permitido", "Permitido"),new("Configurar servidor e estação", "Consulta", "Consulta", "Permitido"),new("Gerenciar usuários e perfis", "Consulta", "Consulta", "Permitido")];
    public RelayCommand Navigate { get; }
    public RelayCommand Placeholder { get; }
    public RelayCommand EditRecipe { get; }
    public RelayCommand EditDefect { get; }
    public RelayCommand Configure { get; }
    public RelayCommand ManageUsers { get; }
    public RelayCommand Judge { get; }
    public RelayCommand CancelJudgment { get; }
    public RelayCommand Finish { get; }
    public RelayCommand ResetDemo { get; }

    public ShellViewModel(AccessService access)
    {
        Access = access;
        Navigate = new(p => Page = Convert.ToInt32(p));
        Placeholder = new(p => PendingIntegration(p?.ToString() ?? "Ação"));
        EditRecipe = new(p => RecipeActionRequested?.Invoke(p?.ToString() ?? "Criar receita"), p => Access.Can(AccessRules.RecipeAction(p?.ToString() ?? "Criar receita")) && !IsDraftInProgress && !RecipeBusy && (p is null || p?.ToString() is "Criar receita" or "Importar receita" || SelectedRecipe is not null));
        ActivateRecipe = new(_ => RecipeActionRequested?.Invoke("Ativar receita"), _ => Access.Can("recipe.activate") && !IsDraftInProgress && !RecipeBusy && SelectedRecipe is not null);
        LoadTestPhoto = new(_ => RecipeActionRequested?.Invoke("Carregar foto de teste"), _ => Access.Can("inspection.photo") && HasActiveRecipe && !RecipeBusy);
        EditDefect = new(_ => PendingIntegration("Editar catálogo"), _ => Access.Can("defects.edit"));
        Configure = new(_ => PendingIntegration("Salvar configuração"), _ => IsAdmin);
        ManageUsers = new(_ => AccessManagementRequested?.Invoke(), _ => Access.Can("access.manage"));
        Judge = new(p => SetDecision(p?.ToString() == "Aceitável" ? "Aceitável" : "Defeito"), _ => Access.Can("inspection.judge") && !Completed && !HasActiveRecipe);
        CancelJudgment = new(_ => SetDecision("Pendente"), _ => Access.Can("inspection.cancel") && !Completed && !HasActiveRecipe && Selected.Decision != "Pendente");
        Finish = new(_ => { Completed = true; Notice = "Demonstração finalizada. Nenhum resultado foi gravado e nenhum contador de produção foi alterado."; }, _ => Access.Can("inspection.finish") && !Completed && !HasActiveRecipe && Pending == 0);
        ResetDemo = new(_ => { Assessments.Clear(); ActiveRecipe = null; RecipeImage = CleanReference = AssembledReference = null; ActiveRois.Clear(); foreach (var name in new[] { nameof(HasActiveRecipe), nameof(ShowDemo), nameof(ShowRecipeWorkspace), nameof(ActiveRecipeLabel) }) Changed(name); foreach (var c in Components) c.Decision = "Pendente"; Completed = false; Selected = Components[0]; UpdateCounts(); Notice = "Demonstração reiniciada. Selecione cada componente para simular seu julgamento."; }, _ => Access.Can("inspection.reset") && !RecipeBusy);
        RefreshAccess();
    }
    // Every command checks the current account and profile before execution.
    private bool IsDraftInProgress => !HasActiveRecipe && !Completed && Components.Any(x => x.Decision != "Pendente");
    private void SetDecision(string value) { Selected.Decision = value; Changed(nameof(Selected)); UpdateCounts(); Notice = $"{Selected.Reference}: {value}. Rascunho de demonstração; nada foi salvo no banco."; }
    private void UpdateCounts() { Changed(nameof(Pending)); Changed(nameof(PendingLabel)); Refresh(); }
    private void PendingIntegration(string action) => Notice = $"{action}: integração prevista para a próxima etapa. Nenhum arquivo, banco ou equipamento foi alterado.";
    private void Refresh()
    {
        EditRecipe?.Refresh(); EditDefect?.Refresh(); Configure?.Refresh(); ManageUsers?.Refresh();
        Judge?.Refresh(); CancelJudgment?.Refresh(); Finish?.Refresh();
        ActivateRecipe?.Refresh(); LoadTestPhoto?.Refresh(); ResetDemo?.Refresh();
    }
}
