using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using VRCGroupTools.ViewModels;

namespace VRCGroupTools.Views;
public partial class OpenInstanceAnnouncementsView : UserControl
{
    public OpenInstanceAnnouncementsView() { InitializeComponent(); }
    private void SaveClicked(object sender, RoutedEventArgs e)
    { try { (DataContext as OpenInstanceAnnouncementsViewModel)?.SaveSecret(WebhookSecret.Password); } finally { WebhookSecret.Clear(); } }
    private void ContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged old) old.PropertyChanged -= ModelChanged;
        if (e.NewValue is INotifyPropertyChanged current) current.PropertyChanged += ModelChanged;
        WebhookSecret?.Clear();
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == "GroupId") WebhookSecret.Clear(); }
    private void ViewUnloaded(object sender, RoutedEventArgs e) { WebhookSecret.Clear(); }
}
