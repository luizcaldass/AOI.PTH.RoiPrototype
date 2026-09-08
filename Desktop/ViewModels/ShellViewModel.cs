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

public enum UserRole { Operador, Engenharia, Administrador }
public enum Permission { Inspect, EditRecipes, EditDefects, Configure, ManageUsers }
public static class RolePolicy
{
    public static bool Allows(UserRole role, Permission permission) => permission switch
    {
        Permission.Inspect => true,
        Permission.EditRecipes or Permission.EditDefects => role is UserRole.Engenharia or UserRole.Administrador,
        Permission.Configure or Permission.ManageUsers => role is UserRole.Administrador,
        _ => false
    };
}
public sealed record NavigationItem(string Glyph, string Title, string Subtitle);
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
    public UserRole[] Roles { get; } = Enum.GetValues<UserRole>();
    private int page;
    public int Page { get => page; set { if (value < 0 || value >= Navigation.Count) return; page = value; Changed(); Changed(nameof(Title)); Changed(nameof(Subtitle)); Changed(nameof(ShowRecipeWorkspace)); } }
    public string Title => Navigation[Page].Title;
    public string Subtitle => Navigation[Page].Subtitle;
    private UserRole role;
    public UserRole Role
    {
        get => role;
        set { role = value; Changed(); Changed(nameof(CanEdit)); Changed(nameof(IsAdmin)); Changed(nameof(RoleNotice)); Refresh(); Notice = $"Perfil de demonstração: {role}. Autenticação real ainda não integrada."; }
    }
    public bool CanEdit => RolePolicy.Allows(Role, Permission.EditRecipes);
    public bool IsAdmin => RolePolicy.Allows(Role, Permission.Configure);
    public string RoleNotice => IsAdmin ? "Administrador • configuração e gestão de acessos" : CanEdit ? "Engenharia • edição de receitas e catálogo" : "Operador • inspeção e julgamento; cadastros em consulta";
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
    public RelayCommand ActivateRecipe { get; }
    public RelayCommand LoadTestPhoto { get; }
    public void SetActive(LocalRecipe item, RecipeFiles files)
    {
        ActiveRecipe = item; ActiveRois.Clear(); foreach (var roi in files.Document.Rois) ActiveRois.Add(roi);
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

    public ShellViewModel()
    {
        Navigate = new(p => Page = Convert.ToInt32(p));
        Placeholder = new(p => PendingIntegration(p?.ToString() ?? "Ação"));
        EditRecipe = new(p => RecipeActionRequested?.Invoke(p?.ToString() ?? "Criar receita"), p => CanEdit && !IsDraftInProgress && !RecipeBusy && (p is null || p?.ToString() is "Criar receita" or "Importar receita" || SelectedRecipe is not null));
        ActivateRecipe = new(_ => RecipeActionRequested?.Invoke("Ativar receita"), _ => !IsDraftInProgress && !RecipeBusy && SelectedRecipe is not null);
        LoadTestPhoto = new(_ => RecipeActionRequested?.Invoke("Carregar foto de teste"), _ => HasActiveRecipe && !RecipeBusy);
        EditDefect = new(_ => PendingIntegration("Editar catálogo"), _ => CanEdit);
        Configure = new(_ => PendingIntegration("Salvar configuração"), _ => IsAdmin);
        ManageUsers = new(_ => PendingIntegration("Cadastrar usuário"), _ => IsAdmin);
        Judge = new(p => SetDecision(p?.ToString() == "Aceitável" ? "Aceitável" : "Defeito"), _ => !Completed && !HasActiveRecipe);
        CancelJudgment = new(_ => SetDecision("Pendente"), _ => !Completed && !HasActiveRecipe && Selected.Decision != "Pendente");
        Finish = new(_ => { Completed = true; Notice = "Demonstração finalizada. Nenhum resultado foi gravado e nenhum contador de produção foi alterado."; }, _ => !Completed && !HasActiveRecipe && Pending == 0);
        ResetDemo = new(_ => { ActiveRecipe = null; RecipeImage = CleanReference = AssembledReference = null; ActiveRois.Clear(); foreach (var name in new[] { nameof(HasActiveRecipe), nameof(ShowDemo), nameof(ShowRecipeWorkspace), nameof(ActiveRecipeLabel) }) Changed(name); foreach (var c in Components) c.Decision = "Pendente"; Completed = false; Selected = Components[0]; UpdateCounts(); Notice = "Demonstração reiniciada. Selecione cada componente para simular seu julgamento."; }, _ => !RecipeBusy);
    }
    // UI prototype only: a real session/authorization service replaces the role selector later.
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
