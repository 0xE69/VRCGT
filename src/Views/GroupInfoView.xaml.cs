using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace VRCGroupTools.Views;

public partial class GroupInfoView : UserControl
{
    public GroupInfoView()
    {
        InitializeComponent();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
