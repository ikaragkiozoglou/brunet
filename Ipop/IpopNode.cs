/*
Copyright (C) 2009  David Wolinsky <davidiw@ufl.edu>, University of Florida

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using Brunet;
using Brunet.Applications;
using Brunet.Connections;
using Brunet.Messaging;
using Brunet.Security;
using Brunet.Symphony;
using Brunet.Transport;
using Brunet.Util;
using NetworkPackets;
using NetworkPackets.Dhcp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Threading;

#if NUNIT
using NUnit.Framework;
#endif

/// \namespace Ipop
/// \brief Ipop provides the IP over P2P library that is to be used in
/// conjunction with Brunet.
/// Main features of this library include Virtual Ethernet (tap) handler,
/// Dhcp Server, Dns Server, and network packet parsing.
/// This is a filter between Brunet and the host system via the Virtual Ethernet
/// device.  Data is read from the ethernet device, parsed and sent to the
/// proper handler if one exists or a query to resolve the ip to a Brunet
/// address.  If a mapping exists, the packet will be sent to the proper end
/// point.  From Brunet, the data is sent to the Ipop handler and optional
/// translation services can occur before being written to the Ethernet device.
/// Both these enter through the HandleData, data read from the Ethernet goes
/// to HandleIPOut, where as Brunet incoming data goes to HandleIPIn. 
namespace Ipop {
  /// <summary> IpopRouter allows Ipop to provide L3 connectivity between
  /// multiple domains with only a single instance per site.</summary>
  /// <remarks> Specifically, a user can have 2 remote clusters set up and
  /// using only a single instance of Ipop per-site connect the two clusters.
  /// Unlike previous versions of Ipop, the advantage are that this does not
  /// require any configuration changes to the individual cluster machines,
  /// still provides dynamic IP addresses for all nodes in the combined
  /// cluster, and allows machines in the same cluster to talk directly with
  /// each other. </remarks>
  public abstract class IpopNode : BasicNode, IDataHandler, IRpcHandler {
    /// <summary>Stores the underlying ApplicationNode</summary>
    public readonly ApplicationNode AppNode;
    /// <summary>Stores the public overlays ApplicationNode.  It will be the
    /// same as AppNode, if there is no private overlay.</summary>
    public readonly ApplicationNode PublicNode;
    /// <summary>The IpopConfig for this IpopNode</summary>
    protected readonly IpopConfig _ipop_config;
    /// <summary>The Virtual Network handler</summary>
    public readonly Ethernet Ethernet;
    /// <summary>The Rpc handler for Information</summary>
    public readonly Information Info;
    /// <summary>The Rpc handler for the Public overlays Information.  It will
    /// be the same as AppNode's, if there is no private overlay.</summary>
    public readonly Information PublicInfo;
    /// <summary>OnDemand for Brunet.</summary>
    protected readonly OnDemandConnectionOverlord _ondemand;
    /// <summary>Resolves IP Addresses to Brunet.Addresses</summary>
    protected IAddressResolver _address_resolver;
    /// <summary>Resolves hostnames and IP Addresses</summary>
    protected Dns _dns;
    /// <summary>If necessary, acts as a DNAT / SNAT</summary>
    protected ITranslator _translator;
    /// <summary>Global lock</summary>
    protected object _sync;
    /// <summary>Because locks are reentrant, this is a non-reentrant lock.</summary>
    protected int _lock;
    /// <summary>Enables multicast.</summary>
    protected readonly bool _multicast;
    /// <summary>Enables broadcast.</summary>
    protected readonly bool _broadcast;
    /// <summary>Enables IP over secure senders.</summary>
    protected bool _secure_senders;

    /// <summary>Mapping of Ethernet address to IP Address.</summary>
    protected Dictionary<MemBlock, MemBlock> _ether_to_ip;
    /// <summary>Mapping of IP Address to Ethernet Address</summary>
    protected Dictionary<MemBlock, MemBlock> _ip_to_ether;

    /// <summary>Provides network information, used to get lease information.<summary>
    protected DhcpServer _dhcp_server;
    /// <summary>Port number to use for the Dhcp Server, typically 67.</summary>
    protected readonly int _dhcp_server_port;
    /// <summary>Port number to use for the Dhcp Client, typically 68.</summary>
    protected readonly int _dhcp_client_port;
    /// <summary>Mapping of Ethernet to the its Dhcp server.</summary>
    protected Dictionary<MemBlock, DhcpServer> _ether_to_dhcp_server;
    protected Dictionary<MemBlock, SimpleTimer> _static_mapping;
    /// <summary>Used to hold configuration information.</summary>
    protected DhcpServer _static_dhcp_server;
    /// <summary>A hashtable used to lock Dhcp Servers.</summary>
    protected Hashtable _checked_out;
    /// <summary>We use this to set our L3 network</summary>
    protected DHCPConfig _dhcp_config;

    /// <summary>We must check the node every so often to see if there
    /// are any static addresses that have gone away or new ones that
    /// have shown up.</summary>
    protected Brunet.DateTime _last_check_node;


#region Public
    /// <summary>Creates an IpopNode given a NodeConfig and an IpopConfig.
    /// Also sets up the Information, Ethernet device, and subscribes
    /// to Brunet for IP Packets</summary>
    /// <param name="node_config">The path to a NodeConfig xml file</param>
    /// <param name="ipop_config">The path to a IpopConfig xml file</param>
    public IpopNode(NodeConfig node_config, IpopConfig ipop_config,
        DHCPConfig dhcp_config) : base(node_config)
    {
      PublicNode = CreateNode(node_config);
      PublicNode.Node.DisconnectOnOverload = false;
      if(PublicNode.PrivateNode == null) {
        AppNode = PublicNode;
      } else {
        AppNode = PublicNode.PrivateNode;
        AppNode.Node.DisconnectOnOverload = false;
      }

      _ondemand = new OnDemandConnectionOverlord(AppNode.Node);
      AppNode.Node.AddConnectionOverlord(_ondemand);
      _ipop_config = ipop_config;

      Ethernet = new Ethernet(_ipop_config.VirtualNetworkDevice);
      Ethernet.Subscribe(this, null);

      Info = new Information(AppNode.Node, "IpopNode", AppNode.SecurityOverlord);
      Info.UserData["IpopNamespace"] = _ipop_config.IpopNamespace;
      if(PublicNode == AppNode) {
        PublicInfo = Info;
      } else {
        PublicInfo = new Information(PublicNode.Node, "PrivateIpopNode",
            PublicNode.SecurityOverlord);
        PublicInfo.UserData["IpopNamespace"] = _ipop_config.IpopNamespace;
      }

      if(_ipop_config.EndToEndSecurity && AppNode.SymphonySecurityOverlord != null) {
        _secure_senders = true;
      } else {
        _secure_senders = false;
      }
      AppNode.Node.GetTypeSource(PType.Protocol.IP).Subscribe(this, null);

      _sync = new object();
      _lock = 0;

      _ether_to_ip = new Dictionary<MemBlock, MemBlock>();
      _ip_to_ether = new Dictionary<MemBlock, MemBlock>();

      _dhcp_server_port = _ipop_config.DHCPPort != 0 ? _ipop_config.DHCPPort : 67;
      _dhcp_client_port = _dhcp_server_port + 1;
      ProtocolLog.WriteIf(IpopLog.DhcpLog, String.Format(
          "Setting Dhcp Ports to: {0},{1}", _dhcp_server_port, _dhcp_client_port));
      _ether_to_dhcp_server = new Dictionary<MemBlock, DhcpServer>();
      _static_mapping = new Dictionary<MemBlock, SimpleTimer>();
      _dhcp_config = dhcp_config;
      if(_dhcp_config != null) {
        SetDns();
        SetTAAuth();
        _dhcp_server = GetDhcpServer();
      }
      _checked_out = new Hashtable();

      AppNode.Node.HeartBeatEvent += CheckNode;
      _last_check_node = Brunet.DateTime.UtcNow;

      AppNode.Node.Rpc.AddHandler("Ipop", this);
    }

    /// <summary>Starts the execution of the IpopNode, this passes the caller 
    /// to execute the Brunet.Connect to eventually become Brunet.AnnounceThread.
    /// </summary>
    public override void Run() {
      if(_shutdown != null) {
        _shutdown.OnExit += Ethernet.Stop;
      }

      Thread thread = null;
      if(PublicNode != AppNode) {
        thread = new Thread(PublicNode.Node.Connect);
        thread.Start();
      }

      AppNode.Node.Connect();

      if(thread != null) {
        thread.Join();
      }
    }

#endregion
#region DataHandling
    /// <summary> This method handles all incoming packets into the IpopNode, both
    /// abroad and local.  This is done to reduce unnecessary extra classes and
    /// circular dependencies.  This method probably shouldn't be called
    /// directly.</summary>
    /// <param name="b"> The incoming packet</param>
    /// <param name="ret">An ISender to return data from the original sender.</param>
    /// <param name="state">always will be null</param>
    public void HandleData(MemBlock b, ISender ret, object state) {
      if(ret is Ethernet) {
        EthernetPacket ep = new EthernetPacket(b);

        switch (ep.Type) {
          case EthernetPacket.Types.Arp:
            HandleArp(ep.Payload);
            break;
          case EthernetPacket.Types.IP:
            HandleIPOut(ep, ret);
            break;
        }
      }
      else {
        HandleIPIn(b, ret);
      }
    }

    /// <summary>This method handles IPPackets that come from Brunet, i.e.,
    /// abroad.  </summary>
    /// <param name="packet"> The packet from Brunet.</param>
    /// <param name="ret">An ISender to send data to the Brunet node that sent
    /// the packet.</param>
    public virtual void HandleIPIn(MemBlock packet, ISender ret) {
      if(_secure_senders && !(ret is SecurityAssociation)) {
        return;
      }

      Address addr = null;
      if(ret is SecurityAssociation) {
        ret = ((SecurityAssociation) ret).Sender;
      }

      if(ret is AHSender) {
        addr = ((AHSender) ret).Destination;
      } else {
        ProtocolLog.Write(IpopLog.PacketLog, String.Format(
          "Incoming packet was not from an AHSender: {0}.", ret));
        return;
      }

      if(_translator != null) {
        try {
          packet = _translator.Translate(packet, addr);
        }
        catch (Exception e) {
          if(ProtocolLog.Exceptions.Enabled) {
            ProtocolLog.Write(ProtocolLog.Exceptions, e.ToString());
          }
          return;
        }
      }

      IPPacket ipp = new IPPacket(packet);

      try {
        if(!_address_resolver.Check(ipp.SourceIP, addr)) {
          return;
        }
      } catch (AddressResolutionException ex) {
        if(ex.Issue == AddressResolutionException.Issues.DoesNotExist) {
          ProtocolLog.WriteIf(IpopLog.ResolverLog, "Notifying remote node of " +
              " missing address: " + addr + ":" + ipp.SSourceIP);
          ISender sender = new AHExactSender(AppNode.Node, addr);
          AppNode.Node.Rpc.Invoke(sender, null, "Ipop.NoSuchMapping", ipp.SSourceIP);
          return;
        } else {
          throw;
        }
      }

      if(IpopLog.PacketLog.Enabled) {
        ProtocolLog.Write(IpopLog.PacketLog, String.Format(
                          "Incoming packet:: IP src: {0}, IP dst: {1}, p2p " +
                              "from: {2}, size: {3}", ipp.SSourceIP, ipp.SDestinationIP,
                              ret, packet.Length));
      }

      WriteIP(packet);
    }

    /// <summary>This method handles IPPackets that come from the TAP Device, i.e.,
    /// local system.</summary>
    /// <remarks>Currently this supports HandleMulticast (ip[0] >= 244 &&
    /// ip[0]<=239), HandleDns (dport = 53 and ip[3] == 1), dhcp (sport 68 and
    /// dport 67.</remarks>
    /// <param name="packet">The packet from the TAP device</param>
    /// <param name="from"> This should always be the tap device</param>
    protected virtual void HandleIPOut(EthernetPacket packet, ISender ret) {
      IPPacket ipp = new IPPacket(packet.Payload);

      if(IpopLog.PacketLog.Enabled) {
        ProtocolLog.Write(IpopLog.PacketLog, String.Format(
                          "Outgoing {0} packet::IP src: {1}, IP dst: {2}", 
                          ipp.Protocol, ipp.SSourceIP, ipp.SDestinationIP));
      }

      if(!IsLocalIP(ipp.SourceIP)) {
        HandleNewStaticIP(packet.SourceAddress, ipp.SourceIP);
        return;
      }

      UdpPacket udpp = null;
      switch(ipp.Protocol) {
        case IPPacket.Protocols.Udp:
          udpp = new UdpPacket(ipp.Payload);
          if(udpp.SourcePort == _dhcp_client_port && udpp.DestinationPort == _dhcp_server_port) {
            if(HandleDhcp(ipp)) {
              return;
            }
          } else if(udpp.DestinationPort == 53 && ipp.DestinationIP.Equals(_dhcp_server.ServerIP)) {
            if(HandleDns(ipp)) {
              return;
            }
          }
          break;
      }

      if(ipp.DestinationIP[0] >= 224 && ipp.DestinationIP[0] <= 239) {
        // We don't want to send Brunet multicast packets over IPOP!
        if(udpp != null && udpp.DestinationPort == IPHandler.mc_port) {
          return;
        } else if(HandleMulticast(ipp)) {
          return;
        }
      }

      if(ipp.DestinationIP.Equals(IPPacket.BroadcastAddress)) {
        if(HandleBroadcast(ipp)) {
          return;
        }
      }

      if(HandleOther(ipp)) {
        return;
      }

      if(_dhcp_server == null || ipp.DestinationIP.Equals(_dhcp_server.ServerIP)) {
        return;
      }

      Address target = null;
      try {
        target = _address_resolver.Resolve(ipp.DestinationIP) as AHAddress;
      } catch(AddressResolutionException ex) {
        if(ex.Issue != AddressResolutionException.Issues.DoesNotExist) {
          throw;
        }
        // Otherwise nothing to do, mapping doesn't exist...
      }

      if(target != null) {
        if(IpopLog.PacketLog.Enabled) {
          ProtocolLog.Write(IpopLog.PacketLog, String.Format(
                            "Brunet destination ID: {0}", target));
        }
        SendIP(target, packet.Payload);
        _ondemand.Set(target);
      }
    }

    /// <summary>Parses Arp Packets and writes to the Ethernet the translation.</summary>
    /// <remarks>IpopRouter makes nodes think they are in the same Layer 2 network
    /// so that two nodes in the same network can communicate directly with each
    /// other.  IpopRouter masquerades for those that are not local.</remarks>
    /// <param name="ep">The Ethernet packet to translate</param>
    protected virtual void HandleArp(MemBlock packet)
    {
      // Can't do anything until we have network connectivity!
      if(_dhcp_server == null) {
        return;
      }

      ArpPacket ap = new ArpPacket(packet);

      // Not in our range!
      if(!_dhcp_server.IPInRange((byte[]) ap.TargetProtoAddress) &&
          !_dhcp_server.IPInRange((byte[]) ap.SenderProtoAddress))
      {
        ProtocolLog.WriteIf(IpopLog.Arp, String.Format("Bad Arp request from {0} for {1}",
            Utils.MemBlockToString(ap.SenderProtoAddress, '.'),
            Utils.MemBlockToString(ap.TargetProtoAddress, '.')));
        return;
      }


      if(ap.Operation == ArpPacket.Operations.Reply) {
        // This would be a unsolicited Arp
        if(ap.TargetProtoAddress.Equals(IPPacket.BroadcastAddress) &&
            !ap.SenderHWAddress.Equals(EthernetPacket.BroadcastAddress))
        {
          HandleNewStaticIP(ap.SenderHWAddress, ap.SenderProtoAddress);
        }
        return;
      }

      // We only support request operation hereafter
      if(ap.Operation != ArpPacket.Operations.Request) {
        return;
      }

      // Must return nothing if the node is checking availability of IPs
      // Or he is looking himself up.
      if(_ip_to_ether.ContainsKey(ap.TargetProtoAddress) ||
          ap.SenderProtoAddress.Equals(IPPacket.BroadcastAddress) ||
          ap.SenderProtoAddress.Equals(IPPacket.ZeroAddress))
      {
        return;
      }

      // We shouldn't be returning these messages if no one exists at that end
      // point
     if(!ap.TargetProtoAddress.Equals(MemBlock.Reference(_dhcp_server.ServerIP))) {
       Address baddr = null;

       try {
         baddr = _address_resolver.Resolve(ap.TargetProtoAddress);
       } catch(AddressResolutionException ex) {
         if(ex.Issue != AddressResolutionException.Issues.DoesNotExist) {
           throw;
         }
         // Otherwise nothing to do, mapping doesn't exist...
       }

       if(AppNode.Node.Address.Equals(baddr) || baddr == null) {
         return;
       }
     }

     ProtocolLog.WriteIf(IpopLog.Arp, String.Format("Sending Arp response for: {0}",
         Utils.MemBlockToString(ap.TargetProtoAddress, '.')));

      ArpPacket response = ap.Respond(EthernetPacket.UnicastAddress);

      EthernetPacket res_ep = new EthernetPacket(ap.SenderHWAddress,
        EthernetPacket.UnicastAddress, EthernetPacket.Types.Arp,
        response.ICPacket);
      Ethernet.Send(res_ep.ICPacket);
    }

    /// <summary>This method is called by HandleIPOut if the packet does match any
    /// of the common mappings.</summary>
    /// <param name="ipp"> The miscellaneous IPPacket.</param>
    /// <returns>True if implemented, false otherwise.</returns>
    protected virtual bool HandleOther(IPPacket ipp) {
      return false;
    }

    /// <summary>This method is called by HandleIPOut if the destination address
    /// is within the multicast address range.  If you want Multicast, implement
    /// this method, output will most likely be sent via the SendIP() method in the
    /// IpopNode base class.</summary>
    /// <param name="ipp"> The IPPacket the contains the multicast message</param>
    /// <returns>True if implemented, false otherwise.</returns>
    protected virtual bool HandleMulticast(IPPacket ipp) {
      return false;
    }

    /// <summary>This method is called by HandleIPOut if the destination address
    /// is the broadcast address.  If you want Broadcast, implement
    /// this method, output will most likely be sent via the SendIP() method in the
    /// IpopNode base class.</summary>
    /// <param name="ipp"> The IPPacket the contains the broadcast message</param>
    /// <returns>True if implemented, false otherwise.</returns>
    protected virtual bool HandleBroadcast(IPPacket ipp) {
      return false;
    }

    /// <summary>If a request is sent to address a.b.c.255 with the dns port (53),
    /// this method will be called by HandleIPOut.  If you want Dns, implement this
    /// method, responses should be written directly to the tap interface using
    /// Ethernet.Send()</summary>
    /// <param name="ipp"> The IPPacket contain the Dns packet</param>
    /// <returns>True if implemented, false otherwise.</returns>
    protected virtual bool HandleDns(IPPacket ipp) {
      return false;
    }

    /// <summary>Sends the IP Packet to the specified target address.</summary>
    /// <param name="target"> the Brunet Address of the target</param>
    /// <param name="packet"> the data to send to the recepient</param>
    protected virtual void SendIP(Address target, MemBlock packet) {
      ISender s = null;
      if(_secure_senders) {
        try {
          s = AppNode.SymphonySecurityOverlord.GetSecureSender(target);
        }
        catch(Exception e) {
          Console.WriteLine(e);
          return;
        }
      }
      else {
        s = new AHExactSender(AppNode.Node, target);
      }
      s.Send(new CopyList(PType.Protocol.IP, packet));
    }

    /// <summary>Writes an IPPacket as is to the TAP device.</summary>
    /// <param name="packet">The IPPacket!</param>
    protected virtual void WriteIP(ICopyable packet)
    {
      MemBlock mp = packet as MemBlock;
      if(mp == null) {
        mp = MemBlock.Copy(packet);
      }

      IPPacket ipp = new IPPacket(mp);
      MemBlock dest = null;
  
      if(!_ip_to_ether.TryGetValue(ipp.DestinationIP, out dest)) {
        if(ipp.DestinationIP[0] >= 224 && ipp.DestinationIP[0] <= 239) {
          dest = EthernetPacket.GetMulticastEthernetAddress(ipp.DestinationIP);
        } else if(ipp.DestinationIP[3] == 255){
          dest = EthernetPacket.BroadcastAddress;
        } else {
          return;
        }
      }

      EthernetPacket res_ep = new EthernetPacket(dest,
            EthernetPacket.UnicastAddress, EthernetPacket.Types.IP, mp);
      Ethernet.Send(res_ep.ICPacket);
    }

    ///<summary>This sends an Icmp Request to the specified address, we want
    ///him to respond to us, so we can guarantee that by pretending to be the
    ///Server (i.e. x.y.z.1).  We'll get a response in our main thread.</summary>
    ///<param name="dest_ip">Destination IP of our request.</summary>
    protected virtual void SendIcmpRequest(MemBlock dest_ip) {
      if(_dhcp_server == null) {
        return;
      }
      MemBlock ether_addr = null;
      if(!_ip_to_ether.TryGetValue(dest_ip, out ether_addr)) {
        ether_addr = EthernetPacket.BroadcastAddress;
      }

      IcmpPacket icmp = new IcmpPacket(IcmpPacket.Types.EchoRequest);
      IPPacket ip = new IPPacket(IPPacket.Protocols.Icmp, _dhcp_server.ServerIP,
          dest_ip, icmp.Packet);
      EthernetPacket ether = new EthernetPacket(ether_addr,
          EthernetPacket.UnicastAddress, EthernetPacket.Types.IP, ip.ICPacket);
      Ethernet.Send(ether.ICPacket);
    }
#endregion
#region DhcpandStaticHandlers
    /// <summary>This is used to process a dhcp packet on the node side, that
    /// includes placing data such as the local Brunet Address, Ipop Namespace,
    /// and other optional parameters in our request to the dhcp server.  When
    /// receiving the results, if it is successful, the results are written to
    /// the TAP device.</summary>
    /// <param name="ipp"> The IPPacket that contains the Dhcp Request</param>
    /// <param name="dhcp_params"> an object containing any extra parameters for 
    /// the dhcp server</param>
    /// <returns> true on if dhcp is supported.</returns>
    protected virtual bool HandleDhcp(IPPacket ipp)
    {
      UdpPacket udpp = new UdpPacket(ipp.Payload);
      DhcpPacket dhcp_packet = new DhcpPacket(udpp.Payload);
      MemBlock ether_addr = dhcp_packet.chaddr;

      if(_dhcp_config == null) {
        return true;
      }

      DhcpServer dhcp_server = CheckOutDhcpServer(ether_addr);
      if(dhcp_server == null) {
        return true;
      }

      MemBlock last_ip = null;
      _ether_to_ip.TryGetValue(ether_addr, out last_ip);
      byte[] last_ipb = (last_ip == null) ? null : (byte[]) last_ip;

      WaitCallback wcb = delegate(object o) {
        ProtocolLog.WriteIf(IpopLog.DhcpLog, String.Format(
            "Attempting Dhcp for: {0}", Utils.MemBlockToString(ether_addr, '.')));

        DhcpPacket rpacket = null;
        try {
          rpacket = dhcp_server.ProcessPacket(dhcp_packet,
              AppNode.Node.Address.ToString(), last_ipb);
        } catch(Exception e) {
          ProtocolLog.WriteIf(IpopLog.DhcpLog, e.Message);
          CheckInDhcpServer(dhcp_server);
          return;
        }

        /* Check our allocation to see if we're getting a new address */
        MemBlock new_addr = rpacket.yiaddr;
        UpdateMapping(ether_addr, new_addr);

        MemBlock destination_ip = ipp.SourceIP;
        if(destination_ip.Equals(IPPacket.ZeroAddress)) {
          destination_ip = IPPacket.BroadcastAddress;
        }

        UdpPacket res_udpp = new UdpPacket(_dhcp_server_port, _dhcp_client_port, rpacket.Packet);
        IPPacket res_ipp = new IPPacket(IPPacket.Protocols.Udp, rpacket.siaddr,
            destination_ip, res_udpp.ICPacket);
        EthernetPacket res_ep = new EthernetPacket(ether_addr, EthernetPacket.UnicastAddress,
            EthernetPacket.Types.IP, res_ipp.ICPacket);
        Ethernet.Send(res_ep.ICPacket);
        CheckInDhcpServer(dhcp_server);
      };

      ThreadPool.QueueUserWorkItem(wcb);
      return true;
    }

    /// <summary>Let's see if we can route for an IP.  Default is do
    /// nothing!</summary>
    /// <param name="ip">The IP in question.</param>
    protected void HandleNewStaticIP(MemBlock ether_addr, MemBlock ip) {
      if(!_ipop_config.AllowStaticAddresses) {
        return;
      }

      lock(_sync) {
        if(_dhcp_config == null) {
          return;
        }
      }

      if(!_dhcp_server.IPInRange(ip)) {
        return;
      }

      DhcpServer dhcp_server = CheckOutDhcpServer(ether_addr);
      if(dhcp_server == null) {
        return;
      }

      ProtocolLog.WriteIf(IpopLog.DhcpLog, String.Format(
          "Static Address request for: {0}", Utils.MemBlockToString(ip, '.')));

      WaitCallback wcb = null;

      wcb = delegate(object o) {
        byte[] res_ip = null;

        try {
          res_ip = dhcp_server.RequestLease(ip, true,
              AppNode.Node.Address.ToString(),
              _ipop_config.AddressData.Hostname);
        } catch(Exception e) {
          ProtocolLog.WriteIf(IpopLog.DhcpLog, e.Message);
        }

        if(res_ip == null) {
          ProtocolLog.WriteIf(IpopLog.DhcpLog, String.Format(
                "Request for {0} failed!", Utils.MemBlockToString(ip, '.')));
        } else {
          lock(_sync) {
            bool new_entry = true;
            if(_ether_to_ip.ContainsKey(ether_addr)) {
              if(_ether_to_ip[ether_addr].Equals(ip)) {
                new_entry = false;
              }
            }
            if(new_entry) {
              if(_static_mapping.ContainsKey(ether_addr)) {
                _static_mapping[ether_addr].Stop();
              }
              _static_mapping[ether_addr] = new SimpleTimer(wcb, null,
                  _dhcp_config.LeaseTime * 1000 / 2,
                  _dhcp_config.LeaseTime * 1000 / 2);
              _static_mapping[ether_addr].Start();
            }
            UpdateMapping(ether_addr, MemBlock.Reference(res_ip));
          }
        }

        CheckInDhcpServer(dhcp_server);
      };

      ThreadPool.QueueUserWorkItem(wcb);
    }
#endregion
#region DhcpHelpers
    /// <summary>We need to get the DHCPConfig as soon as possible so that we
    /// can allocate static addresses, this method helps us do that.</summary>
    protected virtual void GetDhcpConfig() {
      if(_dhcp_config != null) {
        SetDns();
        SetTAAuth();
      }
    }

    protected void SetTAAuth()
    {
      // Setup a TAAuth for the given range so we don't try to form Edges over IPOP
      IPAddress ip = IPAddress.Parse(_dhcp_config.IPBase);
      int netmask = CalculateNetmaskCidr(_dhcp_config.Netmask);
      TAAuthorizer ip_auth = new NetmaskTAAuthorizer(ip, netmask,
          TAAuthorizer.Decision.Deny, TAAuthorizer.Decision.None);

      foreach(EdgeListener el in AppNode.Node.EdgeListenerList) {
        TAAuthorizer current_auth = el.TAAuth;
        TAAuthorizer[] series_auth = new TAAuthorizer[] {ip_auth, current_auth};
        el.TAAuth = new SeriesTAAuthorizer(series_auth);
      }
    }

    /// <summary>Converts from a.b.c.d to the number of 1s in a.b.c.d (CIDR notation).</summary>
    public static int CalculateNetmaskCidr(string netmask) {
      byte[] nmb = Utils.StringToBytes(netmask, '.');
      int nm = (nmb[0] << 24) | (nmb[1] << 16) | (nmb[2] << 8) | nmb[3];
      int cidr = 0;
      while(nm != 0) {
        cidr++;
        nm *= 2;
      }
      return cidr;
    }

    protected virtual bool SupportedDns(string dns) {
      if("StaticDns".Equals(dns)) {
        return true;
      }
      return false;
    }

    protected virtual void SetDns() {
      if(_dns != null) {
        return;
      }

      if(!SupportedDns(_ipop_config.Dns.Type) || _ipop_config.Dns.Type == "StaticDns") {
        _dns = new StaticDns(
            MemBlock.Reference(Utils.StringToBytes(_dhcp_config.IPBase, '.')),
            MemBlock.Reference(Utils.StringToBytes(_dhcp_config.Netmask, '.')),
            _ipop_config.Dns.NameServer, _ipop_config.Dns.ForwardQueries);
      }
    }

    /// <summary>Used to retrieve an instance of the DhcpServer associated with
    /// this type of node.</summary>
    protected abstract DhcpServer GetDhcpServer();

    /// <summary>Static addresses are handled nearly identically to dynamic, so
    /// we use one shared method to pull a dhcp server from the list of dhcp
    /// servers.  We only want one request per Ethernet / IP at a time.</summary>
    protected DhcpServer CheckOutDhcpServer(MemBlock ether_addr) {
      DhcpServer dhcp_server = null;

      lock(_sync) {
        if(!_ether_to_dhcp_server.TryGetValue(ether_addr, out dhcp_server)) {
          dhcp_server = GetDhcpServer();
          _ether_to_dhcp_server.Add(ether_addr, dhcp_server);
        }
      }

      lock(_checked_out.SyncRoot) {
        if(_checked_out.Contains(dhcp_server)) {
          return null;
        }
        _checked_out.Add(dhcp_server, true);
      }

      return dhcp_server;
    }

    /// <summary>The request on the IP allocation space (DHT) has returned.  So
    /// we're done with the server.</summary>
    protected void CheckInDhcpServer(DhcpServer dhcp_server) {
      lock(_checked_out.SyncRoot) {
        _checked_out.Remove(dhcp_server);
      }
    }

    /// <summary>Is this our IP?  Are we routing for it?</summary>
    /// <param name="ip">The IP in question.</param>
    protected virtual bool IsLocalIP(MemBlock ip) {
      return _ip_to_ether.ContainsKey(ip) || ip.Equals(IPPacket.ZeroAddress);
    }
#endregion
#region StateHelpers
    /// <summary>Tasks that should be performed during startup until completion
    /// and repetitive tasks are added here.
    protected void CheckNode(object o, EventArgs ea) {
      lock(_sync) {
        if(_dhcp_config == null) {
          GetDhcpConfig();
          if(_dhcp_config == null) {
            return;
          }
        }

        // The rest doesn't quite work right yet...
        Brunet.DateTime now = Brunet.DateTime.UtcNow;
        if((now - _last_check_node).TotalSeconds < 30) {
          return;
        }
        _last_check_node = now;
      }
      ThreadPool.QueueUserWorkItem(CheckNetwork);
    }

    /// <summary>Called when an ethernet address has had its IP address changed
    /// or set for the first time.</summary>
    protected virtual void UpdateMapping(MemBlock ether_addr, MemBlock ip_addr)
    {
      ArrayList ips = null;
      lock(_sync) {
        if(_ether_to_ip.ContainsKey(ether_addr)) {
          if(_ether_to_ip[ether_addr].Equals(ip_addr)) {
            return;
          }

          MemBlock old_ip = _ether_to_ip[ether_addr];
          _ip_to_ether.Remove(old_ip);
        }

        _ether_to_ip[ether_addr] = ip_addr;
        _ip_to_ether[ip_addr] = ether_addr;

        ips = new ArrayList(_ip_to_ether.Keys.Count);
        foreach(MemBlock ip in _ip_to_ether.Keys) {
          ips.Add(Utils.MemBlockToString(ip, '.'));
        }
      }
      Info.UserData["VirtualIPs"] = ips;
      if(PublicInfo != Info) {
        PublicInfo.UserData["VirtualIPs"] = ips;
      }

      ProtocolLog.WriteIf(IpopLog.DhcpLog, String.Format(
        "IP Address for {0} changed to {1}.",
        BitConverter.ToString((byte[]) ether_addr).Replace("-", ":"),
        Utils.MemBlockToString(ip_addr, '.')));
    }

    ///<summary>This let's us discover all machines in our subnet if and
    ///only if they allow responding to broadcast Icmp Requests, which for
    ///some reason doesn't seem to be defaulted in my Linux machines!</summary>
    protected virtual void CheckNetwork(object o) {
      SendIcmpRequest(MemBlock.Reference(_dhcp_server.Broadcast));
    }

    public void HandleRpc(ISender caller, string method, IList args, object rs) {
      if(method.Equals("NoSuchMapping")) {
        string sip = args[0] as string;
        MemBlock ip = MemBlock.Reference(Utils.StringToBytes(sip, '.'));
        MappingMissing(ip);
      } else {
        throw new Exception("Invalid method!");
      }
    }

    protected virtual bool MappingMissing(MemBlock ip)
    {
      ProtocolLog.WriteIf(IpopLog.ResolverLog, "Notified of address missing.");
      // do we even own this ip?
      return _ip_to_ether.ContainsKey(ip);
    }
#endregion
  }

  /// <summary>Provides exception handling cases for the Address resolver so
  /// that Ipop can perform actions to resolve the issue.</summary>
  public class AddressResolutionException : Exception
  {
    public enum Issues {
      /// <summary>Existing mapping doesn't match the current message.</summary>
      Mismatch,
      /// <summary>No mapping for the current message.</summary>
      DoesNotExist
    }

    public readonly Issues Issue;

    public AddressResolutionException(string message, Issues issue) :
      base(message)
    {
      Issue = issue;
    }
  }

  /// <summary>This interface is used for IP Address to Brunet address translations.
  /// It must implement the method GetAddress.  All IpopNode sub classes MUST
  /// have some IAddressResolver.</summary>
  public interface IAddressResolver {
    /// <summary>Takes an IP and returns the mapped Brunet.Address</summary>
    /// <param name="ip"> the MemBlock representation of the IP</param>
    /// <returns>translated Brunet.Address</returns>
    Address Resolve(MemBlock ip);
    /// <summary>Sometimes mappings change or we get packets from a place
    /// that doesn't map correctly, this checks the mapping, returns false
    /// if the mapping is currently invalid, but then causes a revalidation
    /// of the mapping</summary>
    bool Check(MemBlock ip, Address addr);
  }

  /// <summary>This interface is used for Nodes which would like to translate IP
  /// header data on arrival to a remote node.  This is similar to the DNAT/SNAT
  /// features provided in iptables</summary>
  public interface ITranslator {
    /// <summary>This takes in an IPPacket, translates it and returns the resulting
    /// IPPacket</summary>
    /// <param name="packet">The IP Packet to translate.</param>
    /// <param name="from">The Brunet address the packet was sent from.</param>
    /// <returns>The translated IP Packet.</returns>
    MemBlock Translate(MemBlock packet, Address from);
  }

#if NUNIT
  [TestFixture]
  public class IpopTest {
    [Test]
    public void CidrTest() {
      Assert.AreEqual(1, IpopNode.CalculateNetmaskCidr("128.0.0.0"));
      Assert.AreEqual(4, IpopNode.CalculateNetmaskCidr("240.0.0.0"));
      Assert.AreEqual(7, IpopNode.CalculateNetmaskCidr("254.0.0.0"));
      Assert.AreEqual(10, IpopNode.CalculateNetmaskCidr("255.192.0.0"));
      Assert.AreEqual(14, IpopNode.CalculateNetmaskCidr("255.252.0.0"));
      Assert.AreEqual(18, IpopNode.CalculateNetmaskCidr("255.255.192.0"));
      Assert.AreEqual(31, IpopNode.CalculateNetmaskCidr("255.255.255.254"));
      Assert.AreEqual(32, IpopNode.CalculateNetmaskCidr("255.255.255.255"));
    }
  }
#endif
}
