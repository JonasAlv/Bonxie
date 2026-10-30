namespace Skua.App.Avalonia.ViewModels.Packets;

public record InterceptedPacketViewModel(string Packet, bool? Outbound)
{
    // Outbound is null when the packet was not sent on (blocked)
    public bool IsBlocked => Outbound is null;

    public string Direction => Outbound switch { true => "OUT", false => "IN", null => "BLOCK" };

    public override string ToString()
    {
        return Packet;
    }
}