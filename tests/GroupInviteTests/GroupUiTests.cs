using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using VRCGroupTools.Services;
using VRCGroupTools.ViewModels;
using VRCGroupTools.Views;

static class GroupUiTests
{
    public static Task RunAsync(IVRChatApiService api, ISettingsService settings, GroupInviteLedger ledger)
    {
        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                // Never construct production App: its startup schedules network/storage work.
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new MaterialDesignThemes.Wpf.BundledTheme { BaseTheme = MaterialDesignThemes.Wpf.BaseTheme.Dark, PrimaryColor = MaterialDesignColors.PrimaryColor.DeepPurple, SecondaryColor = MaterialDesignColors.SecondaryColor.Teal });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml") });
                app.Resources["BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
                app.Resources["EmptyStringToVisibilityConverter"] = new VRCGroupTools.Converters.EmptyStringToVisibilityConverter();
                app.Resources["InverseBoolToVisConverter"] = new VRCGroupTools.Converters.InverseBoolToVisConverter();
                var vm = new GameLogViewModel(api, settings, ledger);
                var view = new GameLogView { DataContext = vm };
                view.Measure(new Size(900, 700)); view.Arrange(new Rect(0, 0, 900, 700));
                var frame = new System.Windows.Threading.DispatcherFrame();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                var buttons = Descendants(view).OfType<Button>().ToArray();
                Check(buttons.Any(b => b.Command == vm.InviteToGroupCommand), "Compiled group invite binding missing");
                Check(buttons.Any(b => b.Command == vm.BanLogUserCommand), "Compiled ban action binding missing");
                Check(buttons.Any(b => b.Command == vm.SyncMembersCommand), "Explicit bulk membership sync missing");
                Check(Descendants(view).OfType<DataGrid>().Any(g => ReferenceEquals(g.ItemsSource, vm.InviteHistory)), "Persistent history list not bound");
                var filter = Descendants(view).OfType<CheckBox>().Single(c => BindingOperations.GetBindingExpression(c, CheckBox.IsCheckedProperty)?.ParentBinding.Path.Path == "HideAlreadyInGroup");
                Check(filter.TranslatePoint(new Point(), view).X + filter.ActualWidth <= view.ActualWidth, "Nonmember filter is clipped at normal panel width");
                var scan = buttons.Single(b => b.Command == vm.ScanGameLogCommand);
                Check(scan.TranslatePoint(new Point(), view).X + scan.ActualWidth <= view.ActualWidth, "Scan action is clipped by nonmember filter");
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                app.Shutdown();
                done.SetResult();
            }
            catch (Exception ex) { done.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
}
