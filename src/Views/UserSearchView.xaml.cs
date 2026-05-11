using System.Windows.Controls;

namespace VRCGroupTools.Views;

public partial class UserSearchView : UserControl
{
    public UserSearchView()
    {
        InitializeComponent();
    }

    private void SearchButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Console.WriteLine("[DEBUG] Search button clicked!");
        Console.WriteLine($"[DEBUG] DataContext type: {DataContext?.GetType().Name ?? "null"}");
        Console.WriteLine($"[DEBUG] DataContext: {DataContext}");
        
        if (DataContext is ViewModels.UserSearchViewModel vm)
        {
            Console.WriteLine($"[DEBUG] ViewModel found! SearchQuery: '{vm.SearchQuery}'");
            Console.WriteLine($"[DEBUG] SearchCommand CanExecute: {vm.SearchCommand?.CanExecute(null)}");
        }
    }
}
