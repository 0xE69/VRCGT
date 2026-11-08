using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Extensions.DependencyInjection;
using VRCGroupTools;
using VRCGroupTools.Models;
using VRCGroupTools.Services;
using VRCGroupTools.ViewModels;
using VRCGroupTools.Views;

static class UiTests
{
    public static Task Integrated()
    {
        var result = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
            try
            {
                using var temp = new Temp();
                var ledger = new GroupInviteLedger(temp.Db);
                const string group = "grp_11111111-1111-1111-1111-111111111111", user = "usr_22222222-2222-2222-2222-222222222222", account = "usr_33333333-3333-3333-3333-333333333333";
                const string member="usr_44444444-4444-4444-4444-444444444444", unknown="usr_55555555-5555-5555-5555-555555555555";
                await ledger.SaveMembershipAsync(new(group,new(){member},DateTime.UtcNow,true,unknown));
                var start=DateTime.UtcNow.AddMinutes(-1);
                System.IO.File.WriteAllText(temp.Log,$"[Behaviour] Joining wrld_11111111-1111-1111-1111-111111111111:123\n[Behaviour] OnJoinedRoom\n[Behaviour] OnPlayerJoined Exact ({user})\n");
                System.IO.File.AppendAllText(temp.Log,$"[Behaviour] OnPlayerJoined Self ({account})\n[Behaviour] OnPlayerJoined Member ({member})\n[Behaviour] OnPlayerJoined Unknown ({unknown})\n[Behaviour] OnPlayerJoined Unresolved Name\n");
                var instance=new InstanceInviterService(temp.Dir,()=>start,()=>DateTime.UtcNow);
                var handler=new Handler { Body=$"{{\"id\":\"{user}\",\"location\":\"{PresenceTests.Location}\",\"state\":\"online\"}}" }; var api=new VRChatApiService(new System.Net.Http.HttpClient(handler){BaseAddress=new Uri("https://offline.invalid/")},ledger);
                typeof(VRChatApiService).GetProperty("IsLoggedIn")!.SetValue(api,true);
                typeof(VRChatApiService).GetProperty("CurrentUserId")!.SetValue(api,account);
                var settings = new AppSettings { GroupId=group, InstanceInviterOnly18Plus=false };
                var settingsProxy=Proxy.Make<ISettingsService>((m,_)=>m.Name switch { "get_Settings"=>settings,"get_CurrentGroupId"=>group,_=>null });
                var app=new Application();
                app.Resources.MergedDictionaries.Add(new MaterialDesignThemes.Wpf.BundledTheme { BaseTheme=MaterialDesignThemes.Wpf.BaseTheme.Dark, PrimaryColor=MaterialDesignColors.PrimaryColor.DeepPurple,SecondaryColor=MaterialDesignColors.SecondaryColor.Teal });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml") });
                app.Resources["ZeroToVisibilityConverter"]=new VRCGroupTools.Converters.ZeroToVisibilityConverter();
                app.Resources["BooleanToVisibilityConverter"]=new BooleanToVisibilityConverter();
                var services=new ServiceCollection();
                typeof(App).GetMethod("ConfigureServices",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{services});
                services.AddSingleton<IVRChatApiService>(api); services.AddSingleton(settingsProxy); services.AddSingleton<IInstanceInviterService>(instance);services.AddSingleton(ledger);services.AddSingleton(Proxy.Make<ICacheService>((_,_)=>null));
                using var provider=services.BuildServiceProvider();var vm=provider.GetRequiredService<InstanceInviterViewModel>();
                var confirmations=0; vm.ConfirmAutoInvite=(context,filters)=>{confirmations++;Assert(context.GroupId==group&&context.AccountId==account&&context.InstanceId=="123"&&filters.Contains("18+="),"Confirmation not exact");return true;};
                var view=new InstanceInviterView { DataContext=vm }; view.Measure(new Size(1200,1000)); view.Arrange(new Rect(0,0,1200,1000));Pump();
                var check=(CheckBox)view.FindName("AutoInviteCheckBox");
                Assert(check!=null&&BindingOperations.GetBindingExpression(check,CheckBox.IsCheckedProperty)?.Status==BindingStatus.Active,"Compiled checkbox binding missing");
                Assert(check!.IsChecked==false&&handler.Calls==0,"Auto started on view load");
                check.SetCurrentValue(CheckBox.IsCheckedProperty,true);Pump();
                Assert(confirmations==1&&vm.AutoInviteEnabled,"Explicit checkbox did not start confirmed session");
                var session=(AutoGroupInviteSession)typeof(InstanceInviterViewModel).GetField("_autoSession",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(vm)!;
                await session.TickAsync();await session.PendingSend;Assert(handler.Calls==2&&await ledger.GetStateAsync(group,user)==GroupInviteState.Sent,"Integrated automatic dispatch not tracked");
                await session.TickAsync();Assert(handler.Calls==2,"Unchanged poll duplicated HTTP");
                Assert(((TextBlock)view.FindName("AutoInviteStatus")).Text.Contains("Sent"),"Visible status binding did not update");
                vm.SkipVisitors=true;Assert(!vm.AutoInviteEnabled&&!session.IsRunning,"Filter edit did not revoke consent");
                vm.SkipVisitors=false;check.SetCurrentValue(CheckBox.IsCheckedProperty,true);Pump();Assert(confirmations==2,"Re-enable must reconfirm");
                ((TextBox)view.FindName("MinInstanceTimeInput")).Text="invalid-number";Assert(!vm.AutoInviteEnabled,"Invalid text silently retained automatic consent");
                ((TextBox)view.FindName("MinInstanceTimeInput")).Text="60";check.SetCurrentValue(CheckBox.IsCheckedProperty,true);Pump();Assert(confirmations==3,"Text edits must require new consent");
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));Assert(!vm.AutoInviteEnabled,"Leaving view did not stop");
                var reloaded=new GroupInviteLedger(temp.Db);Assert(await reloaded.GetStateAsync(group,user)==GroupInviteState.Sent,"Restart forgot automatic invite");
                try { await api.SendGroupInviteAsync(group,user);throw new Exception("Manual path bypassed automatic ledger"); } catch(GroupInviteSuppressedException){}
                Assert(handler.Calls==2,"Cross-entrypoint duplicate reached HTTP");
                const string newcomer="usr_66666666-6666-6666-6666-666666666666";
                vm.Only18Plus=true;System.IO.File.AppendAllText(temp.Log,$"[Behaviour] OnPlayerJoined Newcomer ({newcomer})\n");
                handler.Body=$"{{\"id\":\"{newcomer}\",\"ageVerificationStatus\":\"18+\",\"ageVerified\":true,\"tags\":[\"system_trust_basic\"]}}";
                typeof(VRChatApiService).GetField("_lastRequestTime",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(api,DateTime.MinValue);
                var refresh=typeof(InstanceInviterViewModel).GetMethod("RefreshInstanceUsersAsync",BindingFlags.NonPublic|BindingFlags.Instance)!;
                await (Task)refresh.Invoke(vm,null)!;
                Assert(vm.FilteredInstanceUsers.Any(u=>u.User.UserId==newcomer&&u.CanInvite),"Manual refresh cannot review/select newly resolved 18+ users without enabling automation");
                var afterDetails=handler.Calls;await (Task)refresh.Invoke(vm,null)!;Assert(handler.Calls==afterDetails,"Manual refresh re-fetched unchanged metadata");
                check.SetCurrentValue(CheckBox.IsCheckedProperty,true);Pump();Assert(vm.AutoInviteEnabled,"Shutdown regression needs an active opted-in session");
                typeof(InstanceInviterView).GetMethod("OnApplicationExit",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(view,new object?[]{app,null});
                Assert(!vm.AutoInviteEnabled,"Application-exit handler did not revoke automatic consent");
                vm.AutoInviteEnabled=true;Assert(!vm.AutoInviteEnabled,"Disposed session restarted automatically");
                System.IO.File.Delete(temp.Log);
                typeof(InstanceInviterViewModel).GetMethod("UpdateCurrentInstance", BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(vm,null);Pump();
                var detectionText = view.FindName("InstanceDetectionStatus") as TextBlock;
                Assert(detectionText != null && detectionText.Text.Contains("LogMissing") && detectionText.TextWrapping == TextWrapping.Wrap,"Compiled detection diagnostic must be visible, bound and wrapped, not clipped in a horizontal StackPanel");
                Assert(handler.Calls==afterDetails,"Unverified detection caused network requests");
                vm.Dispose(); result.SetResult();
            }
            catch(Exception e){result.SetException(e);}
            finally { dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background); }
            }));
            System.Windows.Threading.Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();return result.Task;
    }
    static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    static void Pump(){var frame=new System.Windows.Threading.DispatcherFrame();System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle,new Action(()=>frame.Continue=false));System.Windows.Threading.Dispatcher.PushFrame(frame);}
}
public class Proxy:DispatchProxy
{
    public Func<MethodInfo,object?[]?,object?> Call=null!;
    public static T Make<T>(Func<MethodInfo,object?[]?,object?> call) where T:class {var proxy=Create<T,Proxy>();((Proxy)(object)proxy).Call=call;return proxy;}
    protected override object? Invoke(MethodInfo? method,object?[]? args)=>Call(method!,args);
}
