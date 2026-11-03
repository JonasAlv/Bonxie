using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Skua.App.Avalonia.Flash;
using Skua.App.Avalonia.AppStartup;
using Skua.App.Avalonia.Services;
using Skua.Shared.Avalonia.Services;
using Skua.Shared.Avalonia.Controls.Theming;
using Skua.Core.AppStartup;
using Skua.Core.Interfaces;
using Skua.Core.Utils;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Skua.App.Avalonia;

public partial class App : Application
{
    private static int _exceptionDialogShown;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        RegisterGlobalExceptionHandlers();

        ServiceCollection services = new();
        services.AddCommonServices();
        services.AddCompiler();
        services.AddScriptableObjects();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IDispatcherService, DispatcherService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IWindowService, WindowService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IThemeService, AvaloniaThemeService>();
        services.AddSingleton(HotKeys.CreateHotKeys);
        services.AddSingleton<IHotKeyService, HotKeyService>();
        services.AddSingleton<ISoundService, SoundService>();
        services.AddSingleton<IFlashUtil, FlashUtil>();
        services.AddSingleton<IScreenshotService, ScreenshotService>();
        services.AddSingleton<SkuaStartupHandler>();
        Skua.App.Avalonia.AppStartup.Services.AddSkuaMainAppViewModels(services);

        Ioc.Default.ConfigureServices(services.BuildServiceProvider());

        Task.Run(() =>
        {
            try
            {
                Ioc.Default.GetRequiredService<IDiagnosticsService>().Start();
                ISettingsService settings = Ioc.Default.GetRequiredService<ISettingsService>();
                settings.SetApplicationVersion();
            }
            catch { }
        });

        ColorSpectrumFix.Initialize();

        IThemeService themeService = Ioc.Default.GetRequiredService<IThemeService>();
        themeService.ThemeChanged += OnThemeChanged;
        themeService.SchemeChanged += OnSchemeChanged;
        ApplyThemeFromService(themeService);

        Task.Run(() =>
        {
            IClientFilesService clientFiles = Ioc.Default.GetRequiredService<IClientFilesService>();
            clientFiles.CreateDirectories();
            clientFiles.CreateFiles();
        });

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            SetupTrayIcon();
            Views.MainWindow mainWindow = new() { WindowStartupLocation = global::Avalonia.Controls.WindowStartupLocation.CenterScreen };
            desktop.MainWindow = mainWindow;
            if (themeService is AvaloniaThemeService activeTheme)
                ThemeResourceApplicator.ApplyTitleBarTheme(mainWindow, activeTheme.IsDarkTheme,
                    activeTheme.UseAccentOnMainTitleBar ? activeTheme.PrimaryColor : (Color?)null,
                    activeTheme.UseAccentOnMainTitleBar ? activeTheme.PrimaryForegroundColor : (Color?)null);
            else
                ThemeResourceApplicator.ApplyTitleBarTheme(mainWindow, themeService.IsDarkTheme);

            desktop.Exit += OnDesktopExit;
        }

        Task.Run(() => TryInitializeStartupServices());
        base.OnFrameworkInitializationCompleted();
    }

    private static void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Exception ex = e.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception.");
            ShowUnhandledException("AppDomain.CurrentDomain.UnhandledException", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            System.Diagnostics.Trace.WriteLine($"TaskScheduler.UnobservedTaskException: {e.Exception}");
            e.SetObserved();
        };
    }

    private static void ShowUnhandledException(string source, Exception ex)
    {
        if (Interlocked.Exchange(ref _exceptionDialogShown, 1) == 1)
            return;

        try
        {
            string details = $"Unhandled exception source: {source}\r\n\r\n{ex.GetType().FullName}: {ex.Message}\r\n\r\n{ex.StackTrace}";
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "crash.txt"), details); } catch { }
        }
        catch
        {
            // ignored
        }
        finally
        {
            Interlocked.Exchange(ref _exceptionDialogShown, 0);
        }
    }

    private static async Task TryInitializeStartupServices()
    {
        try
        {
            MainMenu.RegisterManagedWindows(Ioc.Default.GetRequiredService<IWindowService>());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Startup window service init failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            Ioc.Default.GetRequiredService<IPluginManager>().Initialize();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Startup plugin init failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            Ioc.Default.GetRequiredService<IHotKeyService>().Reload();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Startup hotkey init failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            IGetScriptsService? getScripts = Ioc.Default.GetService<IGetScriptsService>();
            if (getScripts is null)
                return;

            await getScripts.GetScriptsAsync(null, default);

            ISettingsService settings = Ioc.Default.GetRequiredService<ISettingsService>();
            IDialogService dialogService = Ioc.Default.GetRequiredService<IDialogService>();
            if (settings.Get<bool>("CheckBotScriptsUpdates") && (getScripts.Missing > 0 || getScripts.Outdated > 0)
                && (settings.Get<bool>("AutoUpdateBotScripts")
                    || dialogService.ShowMessageBox("Would you like to update your scripts?", "Script Update", true) == true))
            {
                await Skua.Core.Services.ScriptStartupUpdater.DownloadMissingAndOutdatedAsync(getScripts, dialogService);
            }
        }
        catch { }
    }

    private static void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        DisposeTrayIcon();
        try
        {
            if (Ioc.Default.GetService<IThemeService>() is AvaloniaThemeService ats)
                ats.FlushPendingSave();
        }
        catch { }

        Task.Run(async () =>
        {
            try
            {
                var cleanupTask = Task.Run(async () =>
                {
                    try { await Ioc.Default.GetRequiredService<IDiagnosticsService>().DisposeAsync(); } catch { }
                    try { Ioc.Default.GetRequiredService<ICaptureProxy>().Stop(); } catch { }
                    try
                    {
                        await ((IAsyncDisposable)Ioc.Default.GetRequiredService<IScriptBoost>()).DisposeAsync();
                        await ((IAsyncDisposable)Ioc.Default.GetRequiredService<IScriptBotStats>()).DisposeAsync();
                        await ((IAsyncDisposable)Ioc.Default.GetRequiredService<IScriptDrop>()).DisposeAsync();
                        await Ioc.Default.GetRequiredService<IScriptManager>().StopScript();
                        await ((IScriptInterfaceManager)Ioc.Default.GetRequiredService<IScriptInterface>()).StopTimerAsync();
                    } catch { }
                    try { Ioc.Default.GetRequiredService<IFlashUtil>().Dispose(); } catch { }
                    try
                    {
                        if (Ioc.Default.GetRequiredService<IHotKeyService>() is IDisposable disposableHotKeys)
                            disposableHotKeys.Dispose();
                    } catch { }

                    try
                    {
                        WeakReferenceMessenger.Default.Cleanup();
                        WeakReferenceMessenger.Default.Reset();
                        StrongReferenceMessenger.Default.Reset();
                    } catch { }
                });

                await Task.WhenAny(cleanupTask, Task.Delay(1500));
            }
            catch { }
            finally
            {
                Environment.Exit(0);
            }
        });
    }

    // Full theme changes can include base theme + multiple palette slots, so we always
    // republish the complete resource map here (see ThemeResourceApplicator).
    private void OnThemeChanged(object? theme)
    {
        IThemeService themeService = Ioc.Default.GetRequiredService<IThemeService>();
        ThemeVariant targetVariant = themeService.IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        if (RequestedThemeVariant != targetVariant)
            RequestedThemeVariant = targetVariant;

        if (themeService is AvaloniaThemeService ats)
        {
            ThemeResourceApplicator.ApplyTitleBarTheme(this, themeService.IsDarkTheme, ats.PrimaryColor, ats.PrimaryForegroundColor);
            ThemeResourceApplicator.ApplyAccentBrushes(
                this,
                ats.PrimaryColor,
                ats.PrimaryForegroundColor,
                ats.SecondaryColor,
                ats.SecondaryForegroundColor,
                isDarkTheme: themeService.IsDarkTheme);
            if (!ats.UseAccentOnMainTitleBar &&
                ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow is Window mainWindow)
            {
                ThemeResourceApplicator.ApplyTitleBarTheme(mainWindow, ats.IsDarkTheme);
            }
        }
        else
        {
            string? accentHex = theme?.ToString();
            ThemeResourceApplicator.ApplyAccentBrushes(this, accentHex, isDarkTheme: themeService.IsDarkTheme);
        }
    }

    // Single-scheme edits still republish all known colors when AvaloniaThemeService is active.
    // This keeps Material and Skua resources in lockstep and prevents partial updates.
    private void OnSchemeChanged(Core.Models.ColorScheme scheme, object? color)
    {
        IThemeService themeService = Ioc.Default.GetRequiredService<IThemeService>();
        bool isDark = themeService.IsDarkTheme;
        ThemeVariant targetVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (RequestedThemeVariant != targetVariant)
            RequestedThemeVariant = targetVariant;

        if (themeService is AvaloniaThemeService ats)
        {
            ThemeResourceApplicator.ApplyTitleBarTheme(this, isDark, ats.PrimaryColor, ats.PrimaryForegroundColor);
            ThemeResourceApplicator.ApplyAccentBrushes(
                this,
                ats.PrimaryColor,
                ats.PrimaryForegroundColor,
                ats.SecondaryColor,
                ats.SecondaryForegroundColor,
                isDarkTheme: isDark);
            if (!ats.UseAccentOnMainTitleBar &&
                ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow is Window mainWindow)
            {
                ThemeResourceApplicator.ApplyTitleBarTheme(mainWindow, isDark);
            }
            return;
        }

        // Non-AvaloniaThemeService fallback can only target one path at a time.
        if (scheme == Core.Models.ColorScheme.PrimaryForeground)
            ThemeResourceApplicator.ApplyAccentBrushes(this, accentForegroundHex: color?.ToString(), isDarkTheme: isDark);
        else
            ThemeResourceApplicator.ApplyAccentBrushes(this, accentHex: color?.ToString(), isDarkTheme: isDark);
    }

    // Apply theme resources before first window render so root styles and previews do not
    // flash default colors on startup.
    private void ApplyThemeFromService(IThemeService themeService)
    {
        bool isDark = themeService.IsDarkTheme;
        RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (themeService is AvaloniaThemeService ats)
        {
            ThemeResourceApplicator.ApplyTitleBarTheme(this, isDark, ats.PrimaryColor, ats.PrimaryForegroundColor);
            ThemeResourceApplicator.ApplyAccentBrushes(
                this,
                ats.PrimaryColor,
                ats.PrimaryForegroundColor,
                ats.SecondaryColor,
                ats.SecondaryForegroundColor,
                isDarkTheme: isDark);
            return;
        }

        string? accentHex = themeService.SelectedColor?.ToString();
        ThemeResourceApplicator.ApplyAccentBrushes(this, accentHex, isDarkTheme: isDark);
    }

    private void TrayIcon_Clicked(object? sender, EventArgs e)
    {
        ShowClientWindow();
    }

    private void TrayShowClient_Click(object? sender, EventArgs e)
    {
        ShowClientWindow();
    }

    private void TrayHideClient_Click(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow is null)
            return;
        desktop.MainWindow.WindowState = WindowState.Minimized;
    }

    private static TrayIcon? _trayIcon;

    public static void DisposeTrayIcon()
    {
        try
        {
            void Cleanup()
            {
                try
                {
                    if (_trayIcon != null)
                    {
                        _trayIcon.IsVisible = false;
                        if (_trayIcon is IDisposable d)
                            d.Dispose();
                        _trayIcon = null;
                    }

                    if (Current != null)
                    {
                        var icons = TrayIcon.GetIcons(Current);
                        if (icons != null)
                        {
                            foreach (var ic in icons)
                            {
                                try
                                {
                                    ic.IsVisible = false;
                                    if (ic is IDisposable d)
                                        d.Dispose();
                                }
                                catch { }
                            }
                            icons.Clear();
                        }
                        TrayIcon.SetIcons(Current, null);
                    }
                }
                catch { }
            }

            if (Dispatcher.UIThread.CheckAccess())
                Cleanup();
            else
                Dispatcher.UIThread.InvokeAsync(Cleanup).Wait(TimeSpan.FromMilliseconds(500));
        }
        catch { }
    }

    private void TrayExit_Click(object? sender, EventArgs e)
    {
        DisposeTrayIcon();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void ShowClientWindow()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow is null)
            return;

        if (!desktop.MainWindow.IsVisible)
            desktop.MainWindow.Show();
        if (desktop.MainWindow.WindowState == WindowState.Minimized)
            desktop.MainWindow.WindowState = WindowState.Normal;
        desktop.MainWindow.Activate();
    }

    private void SetupTrayIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Skua.Shared.Avalonia/Assets/SkuaIcon.png"));
            var icon = new WindowIcon(stream);

            _trayIcon = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "Skua",
                IsVisible = true
            };
            _trayIcon.Clicked += TrayIcon_Clicked;

            var menu = new NativeMenu();

            var showItem = new NativeMenuItem("Show Client");
            showItem.Click += TrayShowClient_Click;
            menu.Add(showItem);

            var hideItem = new NativeMenuItem("Hide Client");
            hideItem.Click += TrayHideClient_Click;
            menu.Add(hideItem);

            menu.Add(new NativeMenuItemSeparator());

            var exitItem = new NativeMenuItem("Exit");
            exitItem.Click += TrayExit_Click;
            menu.Add(exitItem);

            _trayIcon.Menu = menu;

            var trayIcons = new TrayIcons { _trayIcon };
            TrayIcon.SetIcons(this, trayIcons);
        }
        catch { }
    }
}
