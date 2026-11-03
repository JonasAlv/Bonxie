using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Controls.ApplicationLifetimes;
using global::Avalonia.Platform.Storage;
using global::Avalonia.Threading;

namespace Skua.App.Avalonia.Services;

// Avalonia's StorageProvider shows the system dialog (the desktop portal or GTK).
// Its pickers are async, so the synchronous methods wait for them in a nested dispatcher frame,
// which keeps the UI responsive while the dialog is open.
public class FileDialogService : IFileDialogService
{
    private const string DefaultFilter = "Text Files (*.txt)|*.txt";

    public string? OpenFile() => OpenFile(ClientFileSources.SkuaDIR, "All Files (*.*)|*.*");

    public string? OpenFile(string filters) => OpenFile(ClientFileSources.SkuaDIR, filters);

    public string? OpenFile(string initialDirectory, string filters)
    {
        return RunOnUIThread(async provider =>
        {
            IReadOnlyList<IStorageFile> files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                SuggestedStartLocation = await GetStartLocation(provider, initialDirectory),
                FileTypeFilter = ParseFilters(string.IsNullOrWhiteSpace(filters) ? "All Files (*.*)|*.*" : filters)
            });
            return files.FirstOrDefault()?.TryGetLocalPath();
        });
    }

    public string? OpenFolder() => OpenFolder(ClientFileSources.SkuaDIR);

    public string? OpenFolder(string initialDirectory) => RunOnUIThread(provider => PickFolder(provider, initialDirectory));

    public Task<string?> OpenFolderAsync() => OpenFolderAsync(ClientFileSources.SkuaDIR);

    public Task<string?> OpenFolderAsync(string initialDirectory)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
            GetStorageProvider() is { } provider ? PickFolder(provider, initialDirectory) : Task.FromResult<string?>(null));
    }

    public IEnumerable<string>? OpenText()
    {
        string? path = OpenFile(ClientFileSources.SkuaDIR, DefaultFilter);
        return string.IsNullOrWhiteSpace(path) ? null : File.ReadAllLines(path);
    }

    public string? Save() => Save(ClientFileSources.SkuaDIR, DefaultFilter);

    public string? Save(string filters) => Save(ClientFileSources.SkuaDIR, filters);

    public string? Save(string initialDirectory, string filters)
    {
        List<FilePickerFileType> types = ParseFilters(string.IsNullOrWhiteSpace(filters) ? DefaultFilter : filters);
        // "*.json" -> "json", so a name typed without an extension still gets one.
        string? extension = types.FirstOrDefault()?.Patterns?.FirstOrDefault() is { } pattern && pattern.StartsWith("*.")
            ? pattern[2..]
            : null;

        return RunOnUIThread(async provider =>
        {
            IStorageFile? file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedStartLocation = await GetStartLocation(provider, initialDirectory),
                FileTypeChoices = types,
                DefaultExtension = extension,
                ShowOverwritePrompt = true
            });
            return file?.TryGetLocalPath();
        });
    }

    public void SaveText(string contents)
    {
        string? path = Save();
        if (!string.IsNullOrWhiteSpace(path))
            WriteFile(path, tmp => File.WriteAllText(tmp, contents));
    }

    public void SaveText(IEnumerable<string> contents)
    {
        string? path = Save();
        if (!string.IsNullOrWhiteSpace(path))
            WriteFile(path, tmp => File.WriteAllLines(tmp, contents));
    }

    // Writes next to the file first and replaces it only once that worked, so a full disk or a
    // folder without write access leaves the old file as it was; the error is shown, not thrown.
    private static void WriteFile(string path, Action<string> write)
    {
        string tmp = path + ".tmp";
        try
        {
            write(tmp);
            File.Move(tmp, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(tmp); } catch (Exception) { }
            Ioc.Default.GetService<IDialogService>()?.ShowMessageBox($"Could not save {path}: {ex.Message}", "Save Error");
        }
    }

    private static async Task<string?> PickFolder(IStorageProvider provider, string initialDirectory)
    {
        IReadOnlyList<IStorageFolder> folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            SuggestedStartLocation = await GetStartLocation(provider, initialDirectory)
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private static string? RunOnUIThread(Func<IStorageProvider, Task<string?>> pick)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.InvokeAsync(() => RunOnUIThread(pick)).GetAwaiter().GetResult();

        if (GetStorageProvider() is not { } provider)
            return null;

        Task<string?> task = pick(provider);
        if (!task.IsCompleted)
        {
            DispatcherFrame frame = new();
            task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.UIThread.PushFrame(frame);
        }
        return task.GetAwaiter().GetResult();
    }

    private static IStorageProvider? GetStorageProvider()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        Window? owner = desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
        return owner?.StorageProvider;
    }

    private static async Task<IStorageFolder?> GetStartLocation(IStorageProvider provider, string initialDirectory)
    {
        return !string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory)
            ? await provider.TryGetFolderFromPathAsync(initialDirectory)
            : null;
    }

    // Filter strings ("JSON Files (*.json)|*.json|All files (*.*)|*.*") as Avalonia file types.
    private static List<FilePickerFileType> ParseFilters(string filters)
    {
        string[] parts = filters.Split('|');
        List<FilePickerFileType> types = new();
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            types.Add(new FilePickerFileType(parts[i])
            {
                // "*.*" only matches names with a dot on Linux; "*" is every file.
                Patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim() == "*.*" ? "*" : p.Trim())
                    .ToArray()
            });
        }
        return types;
    }
}
