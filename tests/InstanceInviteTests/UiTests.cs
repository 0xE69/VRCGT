using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools;
using VRCGroupTools.Data;
using VRCGroupTools.Services;
using VRCGroupTools.ViewModels;
using VRCGroupTools.Views;

static class UiTests
{
    public static Task WiredViewAndDependencyInjection()
    {
        var completion = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                // Plain Application only: constructing App schedules its real startup on the Dispatcher.
                var app = new Application();
                app.Resources.MergedDictionaries.Add(new MaterialDesignThemes.Wpf.BundledTheme
                {
                    BaseTheme = MaterialDesignThemes.Wpf.BaseTheme.Dark,
                    PrimaryColor = MaterialDesignColors.PrimaryColor.DeepPurple,
                    SecondaryColor = MaterialDesignColors.SecondaryColor.Teal
                });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml")
                });
                app.Resources["BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
                var services = new ServiceCollection();
                typeof(App).GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { services });
                ApiTests.Assert(services.Any(d => d.ServiceType == typeof(CurrentInstanceInviteWorkflow)), "Current-instance workflow is not registered");
                services.AddSingleton(StubProxy.Create<IVRChatApiService>((m, a) => m.Name switch
                {
                    "get_IsLoggedIn" => false,
                    "GetFriendsAsync" => Task.FromResult(new List<FriendInfo>()),
                    _ => null
                }));
                services.AddSingleton(StubProxy.Create<ISettingsService>());
                services.AddSingleton(StubProxy.Create<IDatabaseService>());
                services.AddSingleton(StubProxy.Create<ICacheService>());
                using var provider = services.BuildServiceProvider();
                var vm = provider.GetRequiredService<FriendInviterViewModel>();
                var view = new FriendInviterView { DataContext = vm };
                view.Measure(new Size(1000, 700));
                view.Arrange(new Rect(0, 0, 1000, 700));
                var frame = new System.Windows.Threading.DispatcherFrame();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                var buttons = Descendants(view).OfType<Button>().ToArray();
                ApiTests.Assert(buttons.Any(b => b.Content?.ToString() == "Invite to Current Instance" && b.Command == vm.InviteToCurrentInstanceCommand), "Compiled Friend Manager lacks current-instance action binding");
                ApiTests.Assert(buttons.Any(b => b.Command == vm.InviteToCurrentInstanceCancelCommand), "Cancellation control is missing");
                ApiTests.Assert(buttons.Any(b => b.Command == vm.InviteToGroupCommand), "Existing group action must remain");
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
