using System.Windows.Controls;

namespace VRCGroupTools.Views;

public partial class GameLogView : UserControl
{
    public GameLogView()
    {
        InitializeComponent();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        bool refreshing = false;
        async void Refresh(object? sender, EventArgs args)
        {
            if (refreshing || DataContext is not VRCGroupTools.ViewModels.GameLogViewModel vm || vm.IsLoading) return;
            try { refreshing = true; await vm.RefreshGroupContextAsync(); }
            finally { refreshing = false; }
        }
        timer.Tick += Refresh;
        Loaded += (s, e) => { Refresh(s, e); timer.Start(); };
        Unloaded += (s, e) => timer.Stop();
    }
}
