using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Material.Icons;
using Material.Icons.Avalonia;
using Skua.App.Avalonia.Services;
using Skua.App.Avalonia.ViewModels;
using Skua.App.Avalonia.ViewModels.MainMenu;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.ViewModels;
using Skua.Core.Models;
using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Skua.App.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly IFlashUtil _flash;
    private readonly IScriptOption _options;
    private readonly SkuaStartupHandler _startup;
    private readonly MainMenuViewModel _mainMenuVm;
    private readonly MainViewModel _mainViewModel;
    private DispatcherTimer? _tabTitleSyncTimer;
    private bool _isFlashLoaded;

    public MainWindow()
    {
        InitializeComponent();
        _mainViewModel = Ioc.Default.GetRequiredService<MainViewModel>();
        _mainMenuVm = Ioc.Default.GetRequiredService<MainMenuViewModel>();
        _flash = Ioc.Default.GetRequiredService<IFlashUtil>();
        _options = Ioc.Default.GetRequiredService<IScriptOption>();
        _startup = Ioc.Default.GetRequiredService<SkuaStartupHandler>();

        DataContext = _mainViewModel;
        Skua.Shared.Avalonia.Controls.Shell.WindowTitleBar.SetArtworkPath(Ioc.Default.GetRequiredService<ISettingsService>().GetShared().TitleBarArtworkPath);
        MainMenuBar.DataContext = _mainMenuVm;
        ConfigureMainMenu();
        RegisterLifecycleHandlers();
    }

    private void ConfigureMainMenu()
    {
        BuildMainMenu();
        _mainMenuVm.MainMenuItems.CollectionChanged += MainMenuItemsChanged;
        if (_mainMenuVm.Plugins is INotifyCollectionChanged notifyPlugins)
        {
            notifyPlugins.CollectionChanged += (s, e) => Dispatcher.UIThread.Post(BuildMainMenu);
        }
    }

    private void MainMenuItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(BuildMainMenu);
    }

    private void BuildMainMenu()
    {
        MainMenuControl.Items.Clear();
        foreach (MainMenuItemViewModel item in _mainMenuVm.MainMenuItems)
            MainMenuControl.Items.Add(CreateMainMenuItem(item, isTopLevel: true));
    }

    private MenuItem CreateMainMenuItem(IMainMenuItemViewModel viewModel, bool isTopLevel = false)
    {
        MenuItem menuItem = new()
        {
            Command = viewModel.Command
        };

        if (isTopLevel)
        {
            menuItem.Classes.Add("top-nav-item");
            MaterialIconKind? iconKind = viewModel.Header switch
            {
                "Scripts" => MaterialIconKind.ScriptTextOutline,
                "Options" => MaterialIconKind.CogOutline,
                "Tools & Helpers" => MaterialIconKind.WrenchOutline,
                "Combat" => MaterialIconKind.SwordCross,
                "Bank" => MaterialIconKind.BankOutline,
                "Diagnostics" => MaterialIconKind.BugOutline,
                "Plugins" => MaterialIconKind.PuzzleOutline,
                "About" => MaterialIconKind.InformationOutline,
                _ => null
            };

            if (iconKind.HasValue)
            {
                var icon = new MaterialIcon
                {
                    Kind = iconKind.Value,
                    Width = 14,
                    Height = 14,
                    VerticalAlignment = VerticalAlignment.Center
                };
                icon[!MaterialIcon.ForegroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaTextBrush");

                var text = new TextBlock
                {
                    Text = viewModel.Header,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 12
                };
                menuItem.Header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { icon, text }
                };
            }
            else
            {
                menuItem.Header = viewModel.Header;
            }
        }
        else
        {
            MaterialIconKind? subIconKind = viewModel.Header switch
            {
                "Script Loader" => MaterialIconKind.ScriptOutline,
                "Scheduler" => MaterialIconKind.CalendarClock,
                "Game" => MaterialIconKind.GamepadVariantOutline,
                "Application" => MaterialIconKind.ApplicationCogOutline,
                "CoreBots" => MaterialIconKind.RobotOutline,
                "Application Themes" => MaterialIconKind.PaletteOutline,
                "HotKeys" => MaterialIconKind.KeyboardOutline,
                "Loadouts Manager" => MaterialIconKind.AccountSwitchOutline,
                "Fast Travel" => MaterialIconKind.CompassOutline,
                "Loader" => MaterialIconKind.DownloadOutline,
                "Grabber" => MaterialIconKind.HandBackRightOutline,
                "Current Drops" => MaterialIconKind.GiftOutline,
                "Junk Items" => MaterialIconKind.DeleteSweepOutline,
                "Skills" => MaterialIconKind.LightningBoltOutline,
                "Stats" => MaterialIconKind.ChartLine,
                "Runtime" => MaterialIconKind.PlayNetworkOutline,
                "Logs" => MaterialIconKind.FileDocumentOutline,
                "Console" => MaterialIconKind.Console,
                "Spammer" => MaterialIconKind.SendClockOutline,
                "Logger" => MaterialIconKind.TextSearch,
                "Interceptor" => MaterialIconKind.ShieldBugOutline,
                _ => null
            };

            if (subIconKind.HasValue)
            {
                var subIcon = new MaterialIcon
                {
                    Kind = subIconKind.Value,
                    Width = 14,
                    Height = 14,
                    VerticalAlignment = VerticalAlignment.Center
                };
                subIcon[!MaterialIcon.ForegroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaMutedTextBrush");
                menuItem.Icon = subIcon;
            }
            menuItem.Header = viewModel.Header;
        }

        if (viewModel.Header == "Plugins")
        {
            foreach (IMainMenuItemViewModel subItem in _mainMenuVm.Plugins)
                menuItem.Items.Add(CreateMainMenuItem(subItem, isTopLevel: false));
        }
        else if (viewModel.SubItems != null && viewModel.SubItems.Any())
        {
            foreach (IMainMenuItemViewModel subItem in viewModel.SubItems)
                menuItem.Items.Add(CreateMainMenuItem(subItem, isTopLevel: false));
        }

        return menuItem;
    }

    private void RegisterLifecycleHandlers()
    {
        Opened += MainWindow_Opened;
        Deactivated += (s, e) => CloseDropdowns();
        Closing += (s, e) => App.DisposeTrayIcon();
        Closed += MainWindow_Closed;

        if (_options is System.ComponentModel.INotifyPropertyChanged notifyOptions)
        {
            notifyOptions.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(IScriptOption.HeadlessMode))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        bool isHeadless = _options.HeadlessMode;
                        if (HeadlessOverlay != null) HeadlessOverlay.IsVisible = isHeadless;
                        if (FlashHost != null)
                        {
                            FlashHost.IsVisible = !isHeadless && _isFlashLoaded;
                        }
                    });
                }
            };
        }
    }

    private void CloseDropdowns()
    {
        AutoToggle?.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
        JumpToggle?.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
        MainMenuControl?.Close();
    }

    private void MainWindow_Opened(object? sender, EventArgs e)
    {
        _startup.Execute();
        _flash.FlashCall += OnFlashCall;

        try
        {
            var themeService = Ioc.Default.GetService<IThemeService>();
            bool isDark = themeService?.IsDarkTheme ?? true;
            Skua.Shared.Avalonia.Services.ThemeResourceApplicator.ApplyTitleBarTheme(this, isDark);
        }
        catch { }

        try
        {
            if (_flash is Flash.FlashUtil player)
                player.ParentWindow = FlashHost.WindowHandle;
            _flash.InitializeFlash();
        }
        catch (Exception ex)
        {
            Ioc.Default.GetRequiredService<IDialogService>().ShowMessageBox(ex.Message, "Game failed to start");
        }

        _tabTitleSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        string lastWrittenUsername = "";
        _tabTitleSyncTimer.Tick += (s, ev) =>
        {
            try
            {
                var bot = Ioc.Default.GetRequiredService<IScriptInterface>();
                if (!bot.Player.LoggedIn || string.IsNullOrWhiteSpace(bot.Player.Username))
                    return;

                string username = bot.Player.Username;
                string dir = ClientFileSources.TabsDIR;

                if (username != lastWrittenUsername)
                {
                    lastWrittenUsername = username;
                    string usernameFile = Path.Combine(dir, $"{Environment.ProcessId}.txt");
                    File.WriteAllText(usernameFile, username);
                    ClientFileSources.RestrictFilePermissions(usernameFile);
                }

                IScriptMap map = Ioc.Default.GetRequiredService<IScriptMap>();
                IScriptPlayer player = Ioc.Default.GetRequiredService<IScriptPlayer>();
                if (!map.Loaded || string.IsNullOrWhiteSpace(map.Name) || string.IsNullOrWhiteSpace(player.Cell))
                    return;

                string room = map.RoomNumber > 0 ? map.RoomNumber.ToString() : string.Empty;
                string locationFile = Path.Combine(dir, $"{Environment.ProcessId}.location.json");
                string temporaryFile = locationFile + ".tmp";
                string locationJson = JsonSerializer.Serialize(new
                {
                    Map = map.Name,
                    Room = room,
                    Cell = player.Cell,
                    Pad = string.IsNullOrWhiteSpace(player.Pad) ? "Spawn" : player.Pad,
                    Username = username,
                    UpdatedUtc = DateTime.UtcNow
                });
                File.WriteAllText(temporaryFile, locationJson);
                ClientFileSources.RestrictFilePermissions(temporaryFile);
                File.Move(temporaryFile, locationFile, true);
                ClientFileSources.RestrictFilePermissions(locationFile);
            }
            catch { }
        };
        _tabTitleSyncTimer.Start();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        App.DisposeTrayIcon();
        _tabTitleSyncTimer?.Stop();
        try
        {
            string dir = ClientFileSources.TabsDIR;
            string file = Path.Combine(dir, $"{Environment.ProcessId}.txt");
            string locationFile = Path.Combine(dir, $"{Environment.ProcessId}.location.json");
            string temporaryFile = locationFile + ".tmp";
            if (File.Exists(file))
                File.Delete(file);
            if (File.Exists(locationFile))
                File.Delete(locationFile);
            if (File.Exists(temporaryFile))
                File.Delete(temporaryFile);
        }
        catch { }

        _flash.FlashCall -= OnFlashCall;
        _startup.Dispose();
        StrongReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    private void OnFlashCall(string function, params object[] args)
    {
        if (function == "loaded" && !_isFlashLoaded)
        {
            _isFlashLoaded = true;
            Dispatcher.UIThread.Post(() =>
            {
                LoadingBar.IsVisible = false;
                FlashHost.IsVisible = !_options.HeadlessMode;
                HeadlessOverlay.IsVisible = _options.HeadlessMode;
                try
                {
                    _flash.Call("setGameObject", "stage.scaleMode", "showAll");
                }
                catch { }
            });
        }
    }
}
