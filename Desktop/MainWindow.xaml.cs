using System.Windows;
using AOI.PTH.Desktop.ViewModels;

namespace AOI.PTH.Desktop;

public partial class MainWindow : Window
{
    public ShellViewModel Model { get; } = new();
    public MainWindow() { InitializeComponent(); DataContext = Model; }
    private void ExitClick(object sender, RoutedEventArgs e) => Close();
}
