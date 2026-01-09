using System.Windows;

namespace VRCGroupTools.Views;

public partial class UpdatePromptWindow : Window
{
    public string CurrentVersion { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public bool ShouldUpdate { get; private set; } = false;

    public UpdatePromptWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        ShouldUpdate = true;
        DialogResult = true;
        Close();
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        ShouldUpdate = false;
        DialogResult = false;
        Close();
    }
}
