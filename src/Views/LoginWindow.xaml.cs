using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools.ViewModels;

namespace VRCGroupTools.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow()
    {
        InitializeComponent();
        _viewModel = App.Services.GetRequiredService<LoginViewModel>();
        DataContext = _viewModel;
        _viewModel.LoginSuccessful += OnLoginSuccessful;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        
        // Trigger auto-login when window loads
        Loaded += async (s, e) =>
        {
            await _viewModel.TryAutoLoginAsync();
            
            // If credentials were loaded but auto-login didn't happen, show the password
            if (!string.IsNullOrEmpty(_viewModel.Password))
            {
                PasswordBox.Password = _viewModel.Password;
            }
        };
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // When toggling password visibility, sync the password to PasswordBox
        if (e.PropertyName == nameof(LoginViewModel.ShowPassword))
        {
            if (!_viewModel.ShowPassword && !string.IsNullOrEmpty(_viewModel.Password))
            {
                // Switching to hidden mode - update PasswordBox
                PasswordBox.Password = _viewModel.Password;
            }
        }
    }

    private void OnLoginSuccessful()
    {
        Console.WriteLine("[DEBUG] OnLoginSuccessful called!");
        try
        {
            Console.WriteLine("[DEBUG] Creating MainWindow...");
            var mainWindow = new MainWindow();
            Console.WriteLine("[DEBUG] MainWindow created, showing...");
            mainWindow.Show();
            Console.WriteLine("[DEBUG] MainWindow shown, closing LoginWindow...");
            _viewModel.LoginSuccessful -= OnLoginSuccessful;
            Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to open MainWindow: {ex}");
            MessageBox.Show($"Error opening main window: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm)
        {
            vm.Password = PasswordBox.Password;
        }
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
