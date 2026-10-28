using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Utils;
using Skua.Shared.Avalonia.ViewModels;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.ViewModels.Packets;

public partial class PacketSpammerViewModel : BotControlViewModelBase
{
    public PacketSpammerViewModel(IScriptSend send, IFileDialogService fileDialog)
        : base("Packet Spammer")
    {
        _send = send;
        _fileDialog = fileDialog;
    }

    private readonly IScriptSend _send;
    private readonly IFileDialogService _fileDialog;

    [ObservableProperty]
    private RangedObservableCollection<string> _packets = new();

    [ObservableProperty]
    private bool _sendToClient;

    [ObservableProperty]
    private int _spamDelay = 1000;

    [ObservableProperty]
    private string _packetText = string.Empty;

    [ObservableProperty]
    private int _sendTimes = 1;

    [ObservableProperty]
    private int _selectedIndex = -1;

    // "Sending n of m" while the spammer runs
    [ObservableProperty]
    private string _runStatus = string.Empty;

    [RelayCommand]
    private void ClearPackets()
    {
        Packets.Clear();
    }

    [RelayCommand]
    private void ToggleSpammer()
    {
        if (SpammerCommand.IsRunning)
            SpammerCommand.Cancel();
        else if (Packets.Count > 0)
            SpammerCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task Spammer(CancellationToken token)
    {
        // Stops once the queue is emptied, or the loop would spin without awaiting
        while (!token.IsCancellationRequested && Packets.Count > 0)
        {
            for (int i = 0; i < Packets.Count; i++)
            {
                SelectedIndex = i;
                RunStatus = $"Sending {i + 1} of {Packets.Count}";
                Send(Packets[i]);
                await Task.Delay(SpamDelay, token);
            }
        }
    }

    [RelayCommand]
    private void RemovePacket()
    {
        if (SelectedIndex < 0)
            return;

        Packets.RemoveAt(SelectedIndex);
    }

    public void RemovePacketAt(int index)
    {
        if (index >= 0 && index < Packets.Count)
            Packets.RemoveAt(index);
    }

    [RelayCommand]
    private void AddPacket()
    {
        if (string.IsNullOrWhiteSpace(PacketText))
            return;

        Packets.Add(PacketText);
    }

    [RelayCommand]
    private void SaveSpammer()
    {
        _fileDialog.SaveText(Packets);
    }

    [RelayCommand]
    private void LoadSpammer()
    {
        IEnumerable<string>? packets = _fileDialog.OpenText();
        if (packets is null)
            return;
        Packets.Clear();
        Packets.AddRange(packets);
    }

    [RelayCommand]
    private void SendPacket()
    {
        if (string.IsNullOrWhiteSpace(PacketText))
            return;

        Send(PacketText);
    }

    private void Send(string packet)
    {
        bool json = packet.StartsWith("{");
        if (SendToClient)
        {
            _send.ClientPacket(packet, json ? "json" : "str");
            return;
        }

        _send.Packet(packet, json ? "Json" : "String");
    }
}