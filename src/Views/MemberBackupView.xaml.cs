using System.Windows.Controls;
using System.Windows.Input;

namespace VRCGroupTools.Views;

public partial class MemberBackupView : UserControl
{
    public MemberBackupView()
    {
        InitializeComponent();
        Loaded += MemberBackupView_Loaded;
    }

    private async void MemberBackupView_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MemberBackupViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }

    private async void BackupItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement element && 
            element.Tag is ViewModels.BackupViewModel backup &&
            DataContext is ViewModels.MemberBackupViewModel vm)
        {
            await vm.LoadBackupMembersCommand.ExecuteAsync(backup);
        }
    }
}
