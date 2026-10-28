using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Skua.App.Avalonia.ViewModels.Packets;

namespace Skua.App.Avalonia.Views;

public partial class PacketSpammerView : UserControl
{
    public PacketSpammerView()
    {
        InitializeComponent();
    }

    // Row numbers: each container carries its 1-based position in Tag, which the row template shows.
    private void PacketsListBox_ContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        e.Container.Tag = (e.Index + 1).ToString();
    }

    private void PacketsListBox_ContainerIndexChanged(object? sender, ContainerIndexChangedEventArgs e)
    {
        e.Container.Tag = (e.NewIndex + 1).ToString();
    }

    // By the row's index, not its text: the queue can hold the same packet twice.
    private void RemovePacket_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control button && button.FindAncestorOfType<ListBoxItem>() is { } item
            && DataContext is PacketSpammerViewModel vm)
            vm.RemovePacketAt(PacketsListBox.IndexFromContainer(item));
    }
}
