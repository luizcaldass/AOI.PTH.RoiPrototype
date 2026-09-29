using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AOI.PTH.Desktop.Security;

public sealed class AccessWindow : Window
{
    private readonly AccessService access;
    private readonly ListBox users = new() { DisplayMemberPath = "Login", MinHeight = 160 };
    private readonly TextBox login = new() { MaxLength = 80 };
    private readonly PasswordBox password = new() { MaxLength = 128, Padding = new Thickness(8) };
    private readonly PasswordBox confirm = new() { MaxLength = 128, Padding = new Thickness(8) };
    private readonly CheckBox enabled = new() { Content = "Conta ativa", IsChecked = true, Margin = new Thickness(0, 12, 0, 12) };
    private readonly TextBlock notice = new() { Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock heading = new() { FontSize = 22, FontWeight = FontWeights.SemiBold };
    private readonly Dictionary<string, CheckBox> checks = [];
    private Guid? userId;
    private bool loading;

    public AccessWindow(AccessService service)
    {
        access = service; access.Demand("access.manage");
        Title = "Usuários e permissões • AOI PTH"; Width = 1080; Height = 850; MinWidth = 840; MinHeight = 620;
        Background = Brush("#F2F5F9"); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var shell = new DockPanel { Margin = new Thickness(22) }; Content = shell;
        DockPanel.SetDock(notice, Dock.Bottom); shell.Children.Add(notice);
        var title = new TextBlock { Text = "Gerenciar usuários", FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) };
        DockPanel.SetDock(title, Dock.Top); shell.Children.Add(title);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); shell.Children.Add(grid);
        var left = new DockPanel { Margin = new Thickness(0, 0, 18, 0) };
        var actions = new StackPanel();
        actions.Children.Add(new TextBlock { Text = "CONTAS CADASTRADAS", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        AddButton(actions, "＋ Novo usuário", NewUser);
        var edit = AddButton(actions, "Editar usuário selecionado", EditSelected); edit.IsEnabled = false;
        users.SelectionChanged += (_, _) => edit.IsEnabled = users.SelectedItem is AccountSummary;
        actions.Children.Add(new TextBlock { Text = "Selecione uma conta e clique em Editar usuário selecionado.", Margin = new Thickness(0, 8, 0, 14) });
        DockPanel.SetDock(actions, Dock.Top); left.Children.Add(actions); left.Children.Add(users); grid.Children.Add(left);
        var form = new StackPanel { Margin = new Thickness(18) }; form.Children.Add(heading);
        form.Children.Add(new TextBlock { Text = "Dados da conta e permissões no mesmo cadastro.", Margin = new Thickness(0, 5, 0, 14) });
        LoginWindow.Field(form, "Login único / matrícula", login); form.Children.Add(enabled);
        var passwords = new Grid(); passwords.ColumnDefinitions.Add(new ColumnDefinition()); passwords.ColumnDefinitions.Add(new ColumnDefinition());
        var first = new StackPanel { Margin = new Thickness(0, 0, 8, 0) }; LoginWindow.Field(first, "Senha numérica • mínimo 5 dígitos", password); passwords.Children.Add(first);
        var second = new StackPanel { Margin = new Thickness(8, 0, 0, 0) }; LoginWindow.Field(second, "Confirmar senha", confirm); Grid.SetColumn(second, 1); passwords.Children.Add(second); form.Children.Add(passwords);
        form.Children.Add(new TextBlock { Text = "Ao editar, deixe as duas senhas vazias para manter a atual.", Margin = new Thickness(0, 8, 0, 18) });
        form.Children.Add(new TextBlock { Text = "Abas e ações permitidas", FontSize = 19, FontWeight = FontWeights.SemiBold });
        form.Children.Add(new TextBlock { Text = "Marque a aba para permitir a consulta e escolha as ações dentro dela. Julgamento está disponível para todas as contas ativas.", Margin = new Thickness(0, 6, 0, 12) });
        for (int i = 0; i < AccessRules.Pages.Length; i++)
        {
            string page = AccessRules.Pages[i];
            var cardContent = new StackPanel();
            var card = new Border { Child = cardContent, CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(1) };
            var pageCheck = AddCheck(cardContent, AccessRules.View(page), AccessRules.PageNames[i]); pageCheck.FontSize = 16; pageCheck.FontWeight = FontWeights.SemiBold;
            void Paint() { card.Background = Brush(pageCheck.IsChecked == true ? "#EAF3FF" : "#FFFFFF"); card.BorderBrush = Brush(pageCheck.IsChecked == true ? "#639DDD" : "#DFE6EF"); }
            pageCheck.Checked += (_, _) => Paint(); pageCheck.Unchecked += (_, _) => Paint();
            if (page == "inspection") cardContent.Children.Add(new TextBlock { Text = "Sempre disponível: julgar, cancelar, finalizar e reiniciar a demonstração.", Margin = new Thickness(24, 5, 0, 8) });
            foreach (var rule in AccessRules.Actions.Where(a => a.Page == page && !AccessRules.Common.Contains(a.Key)))
            {
                var actionCheck = AddCheck(cardContent, rule.Key, rule.Label); actionCheck.Margin = new Thickness(24, 7, 0, 7);
                actionCheck.Checked += (_, _) => { if (!loading) pageCheck.IsChecked = true; };
            }
            pageCheck.Unchecked += (_, _) => { if (!loading) foreach (var rule in AccessRules.Actions.Where(a => a.Page == page && checks.ContainsKey(a.Key))) checks[rule.Key].IsChecked = false; };
            Paint(); form.Children.Add(card);
        }
        var save = AddButton(form, "Salvar usuário e permissões", Save);
        save.Background = Brush("#003B83"); save.Foreground = Brushes.White;
        var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.White }; Grid.SetColumn(scroll, 1); grid.Children.Add(scroll);
        Reload(); NewUser();
    }
    private void Reload() { if (!access.Can("access.manage")) { Close(); return; } users.ItemsSource = access.Users(); }
    private void NewUser()
    {
        userId = null; users.SelectedItem = null; heading.Text = "Novo usuário"; login.Clear(); enabled.IsChecked = true; password.Clear(); confirm.Clear(); SetChecks(AccessRules.Common); notice.Text = "Preencha os dados e selecione os acessos antes de salvar.";
    }
    private void EditSelected()
    {
        if (users.SelectedItem is not AccountSummary user) throw new InvalidOperationException("Selecione um usuário para editar.");
        var profile = access.Profiles().Single(p => p.Id == user.ProfileId);
        userId = user.Id; heading.Text = "Editar usuário: " + user.Login; login.Text = user.Login; enabled.IsChecked = user.Enabled;
        password.Clear(); confirm.Clear(); SetChecks(profile.Grants.Concat(AccessRules.Common)); notice.Text = "As alterações serão aplicadas somente a este usuário ao salvar.";
    }
    private void SetChecks(IEnumerable<string> grants)
    {
        var selected = grants.ToHashSet(); loading = true;
        try { foreach (var item in checks) { item.Value.IsChecked = selected.Contains(item.Key); item.Value.IsEnabled = !AccessRules.Common.Contains(item.Key); } }
        finally { loading = false; }
    }
    private void Save()
    {
        if (password.Password != confirm.Password) throw new InvalidOperationException("As senhas não conferem.");
        access.SaveUserWithPermissions(userId, login.Text, enabled.IsChecked == true, password.Password, checks.Where(x => x.Value.IsChecked == true).Select(x => x.Key));
        password.Clear(); confirm.Clear(); Reload(); NewUser(); notice.Text = "Usuário e permissões salvos. Os acessos passam a valer nas próximas ações.";
    }
    private CheckBox AddCheck(Panel panel, string key, string label)
    {
        var box = new CheckBox { Content = new TextBlock { Text = label }, Padding = new Thickness(4), HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, Cursor = System.Windows.Input.Cursors.Hand };
        checks[key] = box; panel.Children.Add(box); return box;
    }
    private Button AddButton(Panel panel, string label, Action action)
    {
        var button = new Button { Content = new TextBlock { Text = label, TextAlignment = TextAlignment.Center }, Margin = new Thickness(0, 5, 0, 5) };
        button.Click += (_, _) => { try { access.Demand("access.manage"); action(); } catch (Exception ex) { notice.Text = ex.Message; } }; panel.Children.Add(button); return button;
    }
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
}
