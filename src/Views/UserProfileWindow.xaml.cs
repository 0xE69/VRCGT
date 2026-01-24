using System.Windows;
using VRCGroupTools.ViewModels;

namespace VRCGroupTools.Views;

public partial class UserProfileWindow : Window
{
    public UserProfileWindow(UserProfileViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
