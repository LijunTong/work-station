using Avalonia.Controls;
using WorkStation.App.ViewModels;

namespace WorkStation.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    public MainWindow(MainWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
