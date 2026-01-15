using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.ViewModels;

namespace VRCGroupTools.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        Console.WriteLine("[DEBUG] MainWindow constructor starting...");
        
        // Initialize ViewModel BEFORE InitializeComponent so bindings work
        _viewModel = App.Services.GetRequiredService<MainViewModel>();
        Console.WriteLine("[DEBUG] MainViewModel retrieved");
        _viewModel.Initialize(); // Initialize child ViewModels
        Console.WriteLine("[DEBUG] MainViewModel initialized");
        DataContext = _viewModel;
        Console.WriteLine("[DEBUG] DataContext set");
        
        InitializeComponent();
        Console.WriteLine("[DEBUG] MainWindow InitializeComponent done");
        
        _viewModel.LogoutRequested += OnLogoutRequested;
        Console.WriteLine("[DEBUG] MainWindow constructor completed");
    }

    private void OnLogoutRequested()
    {
        var loginWindow = new LoginWindow();
        loginWindow.Show();
        _viewModel.LogoutRequested -= OnLogoutRequested;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeButton_Click(sender, e);
        }
        else
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Close dialog when clicking the overlay background
        if (e.Source == sender)
        {
            _viewModel.CancelAddGroupCommand.Execute(null);
        }
    }
}
