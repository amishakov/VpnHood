﻿using System.Net;
using System.Net.Sockets;
using PacketDotNet;
using VpnHood.Common.Net;

namespace VpnHood.Client.Device;

public class NullPacketCapture : IPacketCapture
{
    public event EventHandler<PacketReceivedEventArgs>? PacketReceivedFromInbound;
    public event EventHandler? Stopped;
    public virtual bool Started { get; set; }
    public virtual bool IsDnsServersSupported { get; set; }
    public virtual IPAddress[]? DnsServers { get; set; }
    public virtual bool CanExcludeApps { get; set; } = true;
    public virtual bool CanIncludeApps { get; set; } = true;
    public virtual string[]? ExcludeApps { get; set; }
    public virtual string[]? IncludeApps { get; set; }
    public virtual IpNetwork[]? IncludeNetworks { get; set; }
    public virtual bool IsMtuSupported { get; set; } = true;
    public virtual int Mtu { get; set; }
    public virtual bool IsAddIpV6AddressSupported { get; set; } = true;
    public virtual bool AddIpV6Address { get; set; } = true;
    public virtual bool CanProtectSocket { get; set; } = true;
    public virtual bool CanSendPacketToOutbound { get; set; } = false;
    public virtual void StartCapture()
    {
        Started = true;
        _ = PacketReceivedFromInbound; //prevent not used warning
    }

    public virtual void StopCapture()
    {
        Started = false;
        Stopped?.Invoke(this, EventArgs.Empty);
    }

    public virtual void ProtectSocket(Socket socket)
    {
        // nothing

    }

    public virtual void SendPacketToInbound(IPPacket ipPacket)
    {
        // nothing
    }

    public virtual void SendPacketToInbound(IList<IPPacket> packets)
    {
        // nothing
    }

    public virtual void SendPacketToOutbound(IPPacket ipPacket)
    {
        // nothing
    }

    public virtual void SendPacketToOutbound(IList<IPPacket> ipPackets)
    {
        // nothing
    }

    public virtual void Dispose()
    {
        StopCapture();
    }

}