using System.Windows;

namespace VRCGroupTools.Views;

public partial class HistoryInviteConfirmation : Window
{
    public HistoryInviteConfirmation() => InitializeComponent();

    public static bool Ask(string title, string message, string acceptLabel)
    {
        var dialog = new HistoryInviteConfirmation { Title = title };
        if (Application.Current?.MainWindow is { IsVisible: true } owner)
            dialog.Owner = owner;
        dialog.MaxHeight = SystemParameters.WorkArea.Height;
        dialog.MaxWidth = SystemParameters.WorkArea.Width;
        dialog.ReviewText.Text = message;
        dialog.ConfirmButton.Content = acceptLabel;
        return dialog.ShowDialog() == true;
    }

    private void Confirm_Click(object sender, RoutedEventArgs args) => DialogResult = true;
}
