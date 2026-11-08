using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VRCGroupTools.Services;
using VRCGroupTools.ViewModels;
using VRCGroupTools.Views;

static class SchedulerUiTests
{
 public static void Run(CalendarEventService calendar, IVRChatApiService api, IDiscordWebhookService discord,
     ISettingsService settings, OpenInstanceAnnouncementService service, string group)
 {
    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
    app.Resources.MergedDictionaries.Add(new MaterialDesignThemes.Wpf.BundledTheme { BaseTheme = MaterialDesignThemes.Wpf.BaseTheme.Dark, PrimaryColor = MaterialDesignColors.PrimaryColor.DeepPurple, SecondaryColor = MaterialDesignColors.SecondaryColor.Teal });
    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml") });
    app.Resources["BooleanToVisibilityConverter"] = new VRCGroupTools.Converters.BoolToVisibilityConverter();
    app.Resources["StringMatchToVisibility"] = new VRCGroupTools.Converters.StringMatchToVisibilityConverter();
    app.Resources["InverseBooleanConverter"] = new VRCGroupTools.Converters.InverseBooleanConverter();
    app.Resources["DaysOfWeekStringConverter"] = new VRCGroupTools.Converters.DaysOfWeekStringConverter();
    app.Resources["DayToggleConverter"] = new VRCGroupTools.Converters.DayToggleConverter();
    var trace = new BindingErrors(); PresentationTraceSources.DataBindingSource.Listeners.Add(trace);
    var vm = new CalendarEventViewModel(calendar, api, () => group, () => new() { group });
    vm.SelectedAutomationRule = new() { Name = "offline", FilterGroupId = group, PostTitle = "title", PostBody = "body" };
    vm.AutomationRules.Add(vm.SelectedAutomationRule);
    vm.PendingPosts.Add(new("offline", group, "title", "body", null, DateTime.UtcNow));
    vm.Events.Add(new CalendarEvent { Name = "Saved local draft", GroupId = group });
    var calendarView = new CalendarEventView { DataContext = vm };
    var discordVm = new DiscordSettingsViewModel(settings, discord, service);
    var discordView = new DiscordSettingsView { DataContext = discordVm };
    var tabs = new TabControl(); tabs.Items.Add(new TabItem { Header = "Calendar", Content = calendarView }); tabs.Items.Add(new TabItem { Header = "Discord", Content = discordView });
    var window = new Window { Content = tabs, Width = 1200, Height = 900, ShowInTaskbar = false };
    try
    {
        window.Show(); window.UpdateLayout(); Pump();
        foreach (var expander in All(calendarView).OfType<Expander>()) expander.IsExpanded = true;
        window.UpdateLayout(); Pump();
        var buttons = All(calendarView).OfType<Button>().ToArray();
        Require(buttons.Any(b => b.Command == vm.EditEventCommand), "saved draft can actually reopen in publishing editor");
        Require(All(calendarView).OfType<ComboBox>().Any(b => ReferenceEquals(b.ItemsSource, vm.AvailableTimeZones)), "visible timezone selection binding");
        Require(All(calendarView).OfType<TextBox>().Any(b => System.Windows.Data.BindingOperations.GetBinding(b, TextBox.TextProperty)?.Path.Path == "StartClock"), "independent HH:mm clock binding");
        Require(buttons.Any(b => b.Command == vm.SaveEventCommand), "calendar save binding");
        Require(buttons.Any(b => b.Command == vm.PublishEventCommand), "explicit event approval command binding");
        Require(buttons.Any(b => b.Command == vm.PublishNewsCommand), "explicit news approval command binding");
        Require(buttons.Any(b => b.Command == vm.SaveNewsDraftCommand), "local news save binding");
        Require(buttons.Any(b => b.Command == vm.RefreshPublicationHistoryCommand), "read-only effect history binding");
        Require(buttons.Any(b => b.Command == vm.ReviewScheduledPostCommand), "per-item scheduled review binding; " + string.Join(";", trace.Errors) + " buttons=" + string.Join(",", buttons.Select(b => b.Content?.ToString())));
        var preview = ApprovalPublisher.Preview(new("ui-fixture", "usr_11111111-1111-1111-1111-111111111111", group, "news", "Exact title", "Exact body", "group", false));
        var approval = new PublicationApprovalWindow(preview);
        approval.Show(); approval.UpdateLayout(); Pump();
        Require(((TextBox)approval.FindName("ExactPreview")).Text == preview.Summary, "exact read-only preview content");
        var approve = (Button)approval.FindName("Approve");
        Require(!approve.IsEnabled, "approval enabled without review acknowledgement");
        ((CheckBox)approval.FindName("Reviewed")).IsChecked = true; Pump();
        Require(approve.IsEnabled, "compiled review-to-approval binding");
        approval.Close();
        Require(buttons.Any(b => b.Command == vm.PrepareDuePostsCommand), "due draft command binding");
        Require(buttons.Any(b => b.Command == vm.StartDraftChecksCommand), "start draft command binding");
        Require(buttons.Any(b => b.Command == vm.PauseDraftChecksCommand), "pause draft command binding");
        Require(All(calendarView).OfType<ItemsControl>().Any(i => ReferenceEquals(i.ItemsSource, vm.PendingPosts)), "pending list binding");
        tabs.SelectedIndex = 1; window.UpdateLayout(); Pump();
        var card = All(discordView).OfType<OpenInstanceAnnouncementsView>().Single();
        Require(ReferenceEquals(card.DataContext, discordVm.OpenInstances), "nested webhook DataContext");
        Require(card.FindName("WebhookSecret") is PasswordBox, "secret not masked");
        var secret = (PasswordBox)card.FindName("WebhookSecret"); secret.Password = "synthetic-do-not-persist";
        ((TestSettings)(object)settings).Group = "grp_22222222-2222-2222-2222-222222222222";
        discordVm.OpenInstances.RefreshGroup(); Pump(); Require(secret.Password.Length == 0, "secret survives group change");
        Require(All(card).OfType<Button>().Any(b => b.Command == discordVm.OpenInstances.StartCommand), "webhook start binding");
        Require(All(card).OfType<Button>().Any(b => b.Command == discordVm.OpenInstances.ReviewReadyCommand), "Discord per-item approval UI binding");
        Require(!service.IsRunning, "rendering started live checks");
        Require(trace.Errors.Count == 0, "WPF binding errors: " + string.Join(";", trace.Errors));
    }
    finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(trace); window.Close(); app.Shutdown(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
 }
 private static void Pump() { var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.ContextIdle) { Interval = TimeSpan.FromMilliseconds(250) }; timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
 private static void Require(bool value, string label) { if (!value) throw new Exception(label); }
 private static IEnumerable<DependencyObject> All(DependencyObject parent)
 {
    var seen = new HashSet<DependencyObject>(); var queue = new Queue<DependencyObject>(); queue.Enqueue(parent);
    while (queue.Count > 0) { var item = queue.Dequeue(); if (!seen.Add(item)) continue; yield return item;
      foreach (var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>()) queue.Enqueue(child);
      if (item is Visual || item is System.Windows.Media.Media3D.Visual3D) for (var n = 0; n < VisualTreeHelper.GetChildrenCount(item); n++) queue.Enqueue(VisualTreeHelper.GetChild(item, n));
    }
 }
 private sealed class BindingErrors : TraceListener
 {
    public List<string> Errors { get; } = new();
    public override void Write(string? message) { if (!string.IsNullOrEmpty(message)) Errors.Add(message); }
    public override void WriteLine(string? message) => Write(message);
 }
}
