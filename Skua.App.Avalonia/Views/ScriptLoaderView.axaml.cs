using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Skua.App.Avalonia.Views;

public partial class ScriptLoaderView : UserControl
{
    public ScriptLoaderView()
    {
        InitializeComponent();
    }

    private async void CopyLog_Click(object? sender, RoutedEventArgs e)
    {
        await ScriptLog.CopySelectedOrAllAsync();
    }
}
