using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using VRCGroupTools.Services;
namespace VRCGroupTools.Views;
public partial class PublicationApprovalWindow : Window
{
    public PublicationApprovalWindow(string exactPreview)
    {
        InitializeComponent();
        ExactPreview.Text = exactPreview;
    }
    public PublicationApprovalWindow(PublicationPreview preview) : this(preview.Summary)
    {
        var bytes = preview.ImageBytes;
        if (bytes.Length > 0)
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); Artwork.Source = bitmap;
        }
    }
    private void Approve_Click(object sender, RoutedEventArgs e) { if (Reviewed.IsChecked == true) DialogResult = true; }
    public static bool ConfirmAnnouncement(AnnouncementPreview preview)
    {
        var window = new PublicationApprovalWindow(preview.Summary) { Title = "Explicit approval — ONE Discord message" };
        window.Approve.Content = "Approve this ONE Discord message now";
        return window.ShowDialog() == true;
    }
    public static bool Confirm(PublicationPreview preview) => new PublicationApprovalWindow(preview).ShowDialog() == true;
}
