/*
Copyright (C) 2008  David Wolinsky <davidiw@ufl.edu>, University of Florida

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
using Brunet.Services.Dht;
using Brunet.Symphony;
using Brunet.Util;
using Ipop;
using NetworkPackets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;

/**
\namespace Ipop::DhtNode
\brief Defines DhtIpopNode and the utilities necessary to use Ipop over Dht.
*/
namespace Ipop.Dht {
  /// <summary>This class provides an IpopNode that does address and name
  /// resolution using Brunet's Dht.  Multicast is supported.</summary>
  public class DhtIpopNode: IpopNode {
    protected bool _connected;

    ///<summary>Creates a DhtIpopNode.</summary>
    /// <param name="NodeConfig">NodeConfig object</param>
    /// <param name="IpopConfig">IpopConfig object</param>
    public DhtIpopNode(NodeConfig node_config, IpopConfig ipop_config,
        DHCPConfig dhcp_config) : base(node_config, ipop_config, dhcp_config)
    {
      DhtAddressResolver dar = new DhtAddressResolver(AppNode.Dht, _ipop_config.IpopNamespace);
      Shutdown.OnExit += dar.Stop;
      _address_resolver = dar;

      _connected = false;
      AppNode.Node.StateChangeEvent += StateChangeHandler;
      StateChangeHandler(AppNode.Node, AppNode.Node.ConState);
    }

    public DhtIpopNode(NodeConfig node_config, IpopConfig ipop_config) :
        this(node_config, ipop_config, null)
    {
    }

    /// <summary> Occassionally nodes will get a true return from a allocation
    /// attempt, in order to prevent this, we reissue all dhcp requests after
    /// getting "connected" to the overlay.</summary>
    protected void StateChangeHandler(Node n, Node.ConnectionState state) {
      List<MemBlock> ips = null;

      lock(_sync) {
        if(state == Node.ConnectionState.Connected) {
          if(_connected) {
            return;
          }
          AppNode.Node.StateChangeEvent -= StateChangeHandler;
          _connected = true;
        } else {
          return;
        }

        ips = new List<MemBlock>(_ip_to_ether.Keys.Count);
        foreach(MemBlock ip in _ip_to_ether.Keys) {
          ips.Add(ip);
        }
      }

      WaitCallback callback = delegate(object o) {
        // Get a new Dhcp server so we get new state!
        DhcpServer dhcp_server = GetDhcpServer();
        foreach(MemBlock ip in ips) {
          try {
            dhcp_server.RequestLease(ip, true, AppNode.Node.Address.ToString(),
                _ipop_config.AddressData.Hostname);
          } catch(Exception e) {
            ProtocolLog.WriteIf(IpopLog.DhcpLog, e.Message);
          }
        }
      };

      ThreadPool.QueueUserWorkItem(callback, null);
    }

    /// <summary>Someone told us we didn't have a mapping... let's fix that.</summary>
    protected override bool MappingMissing(MemBlock ip)
    {
      if(!base.MappingMissing(ip)) {
        return false;
      }

      // Easiest approach is to simply update the mapping...
      DhcpServer dhcp_server = GetDhcpServer();
      try {
        dhcp_server.RequestLease(ip, true, AppNode.Node.Address.ToString(),
            _ipop_config.AddressData.Hostname);
      } catch(Exception e) {
        ProtocolLog.WriteIf(IpopLog.DhcpLog, e.Message);
      }

      return true;
    }

    protected override bool SupportedDns(string dns) {
      if("DhtDns".Equals(dns)) {
        return true;
      }

      return base.SupportedDns(dns);
    }

    protected override void SetDns() {
      if(_dns != null) {
        return;
      }

      if("DhtDns".Equals(_ipop_config.Dns.Type)) {
        _dns = new DhtDns(
            MemBlock.Reference(Utils.StringToBytes(_dhcp_config.IPBase, '.')),
            MemBlock.Reference(Utils.StringToBytes(_dhcp_config.Netmask, '.')),
            _ipop_config.Dns.NameServer, _ipop_config.Dns.ForwardQueries,
            AppNode.Dht, _ipop_config.IpopNamespace);
      } else {
        base.SetDns();
      }
    }

    /// <summary>This calls a Dns Lookup using ThreadPool.</summary>
    /// <param name="ipp">The IP Packet containing the Dns query.</param>
    /// <returns>Returns true since this is implemented.</returns>
    protected override bool HandleDns(IPPacket ipp) {
      WaitCallback wcb = delegate(object o) {
        try {
          WriteIP(_dns.LookUp(ipp).ICPacket);
        } catch {
        }
      };
      ThreadPool.QueueUserWorkItem(wcb, ipp);
      return true;
    }

    /// <summary>Calls HandleMulticast.</summary>
    protected override bool HandleBroadcast(IPPacket ipp) {
      return HandleMulticast(ipp);
    }

    /// <summary>Called by HandleIPOut if the current packet has a Multicast 
    /// address in its destination field.  This sends the multicast packet
    /// to all members of the multicast group stored in the dht.</summary>
    /// <param name="ipp">The IP Packet destined for multicast.</param>
    /// <returns>This returns true since this is implemented.</returns>
    protected override bool HandleMulticast(IPPacket ipp) {
      if(!_ipop_config.EnableMulticast) {
        return true;
      }

      WaitCallback wcb = delegate(object o) {
        Hashtable[] results = null;
        try {
          results = AppNode.Dht.Get(Encoding.UTF8.GetBytes(_ipop_config.IpopNamespace + ".multicast.ipop"));
        } catch {
          return;
        }
        foreach(Hashtable result in results) {
          try {
            AHAddress target = (AHAddress) AddressParser.Parse(Encoding.UTF8.GetString((byte[]) result["value"]));
            if(IpopLog.PacketLog.Enabled) {
              ProtocolLog.Write(IpopLog.PacketLog, String.Format(
                                "Brunet destination ID: {0}", target));
            }
            SendIP(target, ipp.Packet);
          }
          catch {}
        }
      };

      ThreadPool.QueueUserWorkItem(wcb, ipp);
      return true;
    }

    /// <summary>We need to get the DHCPConfig as soon as possible so that we
    /// can allocate static addresses, this method helps us do that.</summary>
    protected override void GetDhcpConfig() {
      if(Interlocked.Exchange(ref _lock, 1) == 1) {
        return;
      }

      WaitCallback wcb = delegate(object o) {
        bool success = false;
        DHCPConfig dhcp_config = null;
        try {
          dhcp_config = DhtDhcpServer.GetDhcpConfig(AppNode.Dht, _ipop_config.IpopNamespace);
          success = true;
        } catch(Exception e) {
          ProtocolLog.WriteIf(IpopLog.DhcpLog, e.ToString());
        }

        if(success) {
          lock(_sync) {
            _dhcp_config = dhcp_config;
            _dhcp_server = new DhtDhcpServer(AppNode.Dht, _dhcp_config, _ipop_config.EnableMulticast);
          }
        }
        base.GetDhcpConfig();

        Interlocked.Exchange(ref _lock, 0);
      };

      ThreadPool.QueueUserWorkItem(wcb);
    }

    protected override DhcpServer GetDhcpServer() {
      return new DhtDhcpServer(AppNode.Dht, _dhcp_config, _ipop_config.EnableMulticast);
    }
  }
}
