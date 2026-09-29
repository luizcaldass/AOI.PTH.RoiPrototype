using System.Windows;
using System.Windows.Controls;

namespace AOI.PTH.Desktop.Security;

public sealed class LoginWindow : Window
{
    private readonly AccessService access;
    private readonly bool setup;
    private readonly TextBox login = new() { MaxLength = 80 };
    private readonly PasswordBox password = new() { MaxLength = 128, Padding = new Thickness(10), MinHeight = 36 };
    private readonly PasswordBox confirmation = new() { MaxLength = 128, Padding = new Thickness(10), MinHeight = 36 };
    private readonly TextBlock message = new() { Margin = new Thickness(0, 12, 0, 12), TextWrapping = TextWrapping.Wrap };
    public LoginWindow(AccessService service)
    {
        access = service; setup = access.NeedsSetup;
        Title = setup ? "AOI PTH • Primeiro administrador" : "AOI PTH • Entrar";
        Background = System.Windows.Media.Brushes.White; Foreground = System.Windows.Media.Brushes.Black;
        Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(28), Background = System.Windows.Media.Brushes.White }; Content = panel;
        panel.Children.Add(new TextBlock { Text = setup ? "Cadastrar administrador inicial" : "Entrar na estação", FontSize = 23, FontWeight = FontWeights.SemiBold });
        message.Text = setup ? "Defina o primeiro acesso. Depois, cadastre os perfis e colaboradores em Perfis e acesso." : "Use o login e a senha fornecidos pelo administrador.";
        panel.Children.Add(message);
        Field(panel, "Login", login); Field(panel, "Senha numérica • mínimo 5 dígitos", password);
        if (setup) Field(panel, "Confirmar senha", confirmation);
        var submit = new Button { Content = setup ? "Criar administrador e entrar" : "Entrar", IsDefault = true, Margin = new Thickness(0, 16, 0, 0) };
        panel.Children.Add(submit);
        submit.Click += async (_, _) => {
            submit.IsEnabled = false;
            try {
                var name = login.Text; var secret = password.Password;
                if (setup && secret != confirmation.Password) throw new InvalidOperationException("As senhas não conferem.");
                bool accepted = await Task.Run(() => { if (setup) access.Bootstrap(name, secret); return access.Authenticate(name, secret); });
                password.Clear(); confirmation.Clear();
                if (accepted) DialogResult = true;
                else message.Text = "Acesso não autorizado. Confira os dados e se a conta está ativa. Após 5 erros, aguarde 1 minuto.";
            } catch (Exception ex) { message.Text = ex.Message; }
            finally { submit.IsEnabled = true; }
        };
        Loaded += (_, _) => login.Focus();
    }
    internal static void Field(Panel panel, string label, UIElement input)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 12, 0, 5) }); panel.Children.Add(input);
    }
}
