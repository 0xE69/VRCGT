using System.Windows.Controls;
using VRCGroupTools.ViewModels;

namespace VRCGroupTools.Views;

public partial class AuditLogView : UserControl
{
    public AuditLogView()
    {
        InitializeComponent();
        
        Loaded += async (s, e) =>
        {
            if (DataContext is AuditLogViewModel vm)
            {
                await vm.InitializeAsync();
            }
        };
    }
}
