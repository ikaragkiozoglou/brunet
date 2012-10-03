/*
Copyright (C) 2008  David Wolinsky <davidiw@ufl.edu>, University of Florida
                    Pierre St Juste <ptony82@ufl.edu>, University of Florida
                    Benjamin Woodruff <odetopi.e@gmail.com>

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
using Brunet.Util;
using Brunet.Symphony;
using Brunet.Applications;
using Brunet.Concurrent;
using NetworkPackets;
using NetworkPackets.Dns;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Ipop.Managed.Translation;

#if ManagedIpopNodeNUNIT
using NUnit.Framework;
#endif

namespace Ipop.Managed {

  public interface IDnsResolver {
    string DnsResolve(string name);
  }

  /// <summary>
  /// This class implements Dns, IAddressResolver, IManagedHandler, and
  /// ITranslator. It provides most functionality needed by ManagedIpopNode.
  /// </summary>
  public class ManagedAddressResolverAndDns : Dns, IAddressResolver, ITranslator {

    /// <summary> Udp packet translators.</summary>
    protected IProtocolTranslator<UdpPacket>[] _udp_translators;

    /// <summary>The node to do ping checks on</summary>
    protected readonly StructuredNode _node;

    /// <summary>Contains ip:hostname mapping</summary>
    protected readonly Dictionary<string, string> _dns_a;

    /// <summary>Contains hostname:ip mapping</summary>
    protected readonly Dictionary<string, string> _dns_ptr;

    /// <summary>Maps MemBlock IP Addresses to Brunet Address as Address</summary>
    protected readonly Dictionary<MemBlock, Address> _ip_addr;

    /// <summary>Maps Brunet Address as Address to MemBlock IP Addresses</summary>
    protected readonly Dictionary<Address, MemBlock> _addr_ip;

    /// <summary>Keeps track of blocked addresses</summary>
    protected readonly Dictionary<Address, MemBlock> _blocked_addrs;

    /// <summary>MemBlock of the IP mapped to local node</summary>
    protected readonly MemBlock _local_ip;

    /// <summary>Helps assign remote end points</summary>
    protected readonly DhcpServer _dhcp;

    /// <summary>Array list of multicast addresses</summary>
    public readonly List<Address> mcast_addr;

    /// <summary>Keeps track of number of sent packets</summary>
    protected readonly Dictionary<Address, int> _tx_counters;

    /// <summary>Keeps track of number or received packets</summary>
    protected readonly Dictionary<Address, int> _rx_counters;

    /// <summary>Synchronization object</summary>
    protected readonly object _sync;

    /// <summary>Create a new address resolver</summary>
    protected readonly WriteOnce<IDnsResolver> _resolver;

    /// <summary>Setter for address resolver</summary>
    public IDnsResolver Resolver {
      set { _resolver.Value = value; }
    }

    /// <summary>
    /// Constructor for the class, it initializes various objects
    /// </summary>
    /// <param name="node">Takes in a structured node</param>
    public ManagedAddressResolverAndDns(StructuredNode node, DhcpServer dhcp,
        MemBlock local_ip, string name_server, bool forward_queries) :
      base(MemBlock.Reference(dhcp.BaseIP), MemBlock.Reference(dhcp.Netmask),
          name_server, forward_queries)
    {
      _node = node;
      _dns_a = new Dictionary<string, string>();
      _dns_ptr = new Dictionary<string, string>();
      _ip_addr = new Dictionary<MemBlock, Address>();
      _addr_ip = new Dictionary<Address, MemBlock>();
      _blocked_addrs = new Dictionary<Address, MemBlock>();
      mcast_addr = new List<Address>();
      _tx_counters = new Dictionary<Address, int>();
      _rx_counters = new Dictionary<Address, int>();

      _dhcp = dhcp;
      _local_ip = local_ip;
      _sync = new object();
      _udp_translators = new IProtocolTranslator<UdpPacket>[] {
        new MDnsTranslator(local_ip),
        new SipTranslator(local_ip),
        new SsdpTranslator(local_ip)
      };
      _resolver = new WriteOnce<IDnsResolver>();
    }

    protected void UpdateCounter(Dictionary<Address, int> counters, 
      Address addr) {

      if(addr == null) {
        return;
      }

      int counter;
      if(counters.TryGetValue(addr, out counter)) {
        counter++;
        counters[addr] = counter;
      }
      else {
        counters.Add(addr, 1);
      }
    }

    public string GetStats(string address) {
      Address addr = AddressParser.Parse(address);
      int rx_counter = 0;
      int tx_counter = 0;

      _tx_counters.TryGetValue(addr, out tx_counter);
      _rx_counters.TryGetValue(addr, out rx_counter);

      return rx_counter + " " + tx_counter;
    }

    // Return string of localIP
    public string LocalIP {
      get { return Utils.MemBlockToString(_local_ip, '.'); }
    }

    /// <summary>
    /// This method does an inverse lookup for the DNS
    /// </summary>
    /// <param name="ip">IP address of the name that's being looked up</param>
    /// <returns>Returns the name as string of the IP specified</returns>
    public override String NameLookUp(String ip) {
      return _dns_ptr[ip];
    }

    /// <summary>
    /// This method does an address lookup on the Dns
    /// </summary>
    /// <param name="name">Takes in name as string to lookup</param>
    /// <returns>The result as a String Ip address</returns>
    public override String AddressLookUp(String name) {
      string ip = null;
      if (_resolver.Value != null) {
        ip = _resolver.Value.DnsResolve(name);
      }
      if (ip == null) {
        ip = _dns_a[name];
      }
      return ip;
    }

    /**
    <summary>Implements the ITranslator portion for ManagedAddress..., takes an
    IP Packet, based upon who the originating Brunet Sender was, changes who
    the packet was sent from and then switches the destination address to the
    local nodes address. Takes incomming packets only.</summary>
    <param name="packet">The IP Packet to translate.</param>
    <param name="from">The Brunet address the packet was sent from.</param>
    <returns>The translated IP Packet.</returns>
    */
    public MemBlock Translate(MemBlock packet, Address from) {
      UpdateCounter(_rx_counters, from);
      MemBlock source_ip = _addr_ip[from];
      if(source_ip == null) {
        throw new Exception("Invalid mapping " + from + ".");
      }

      // Attempt to translate a packet
      IPPacket ipp = new IPPacket(packet);
      // hdr is everything in the packet up to the source IP, dest IP, "options"
      //   and data
      MemBlock hdr = packet.Slice(0,12);
      // Pull the "fragment" info from the header. If it is not fragmented, we
      // can deal with the packet. DNS packets should never be sent as fragments
      bool fragment = ((hdr[6] & 0x1F) | hdr[7]) != 0;
      //should there be a field in the IPPacket class for this?
      
      MemBlock dest_ip = ipp.DestinationIP;
      byte dest_ip_first_byte = dest_ip[0];
      bool is_multicast = dest_ip_first_byte > 223 && dest_ip_first_byte < 240;
      //multicast addresses are 224.0.0.0 through 239.255.255.255
      
      if(ipp.Protocol == IPPacket.Protocols.Udp && !fragment) { //simple UDP
        UdpPacket udpp = new UdpPacket(ipp.Payload);
        foreach(IProtocolTranslator<UdpPacket> i in _udp_translators) {
          if(i.MatchesProtocol(udpp)) {
            udpp = i.Translate(udpp, source_ip,
                               ipp.SourceIP, ipp.DestinationIP);
            return new IPPacket(ipp.Protocol, source_ip,
                                is_multicast?
                                  ipp.DestinationIP:
                                  _local_ip,
                                hdr, udpp.ICPacket).Packet;
          }
        }
      }
      
      //fallback
      return IPPacket.Translate(packet, source_ip,
                                is_multicast?
                                  ipp.DestinationIP:
                                  _local_ip
      );
    }
    
    
    
    /// <summary>
    /// Returns the Brunet address given an IP
    /// </summary>
    /// <param name="IP">A MemBlock of the IP</param>
    /// <returns>A brunet Address for the IP</returns>
    public Address Resolve(MemBlock IP) {
      Address to = _ip_addr[IP];
      UpdateCounter(_tx_counters, to);
      return to;
    }

    public bool Check(MemBlock ip, Address addr) {
      return _addr_ip[addr].Equals(ip) && _ip_addr[ip].Equals(addr);
    }

    /// <summary>
    /// Sets up DNS for localhost
    /// </summary>
    /// <param name="name">The DNS alias for the localhost</param>
    /// <returns>true if successful</returns>
    public bool MapLocalDns(string name) {
      _dns_a.Add(name, LocalIP);
      _dns_ptr.Add(LocalIP, name);
      return true;
    }
    
    /// <summary>
    /// Maps IP address to Brunet address.
    /// </summary>
    /// <param name="ip">IP address to map</param>
    /// <param name="addr">Brunet address to map</param>
    /// <returns>IP string of the allocated IP</returns>
    public string AddIPMapping(string ip, Address addr) {
      MemBlock ip_bytes;

      lock(_sync) {
        if(ip == null || ip == String.Empty) {
          do {
            ip_bytes = MemBlock.Reference(_dhcp.RandomIPAddress());
          } while (_ip_addr.ContainsKey(ip_bytes));
        }
        else {
          ip_bytes = MemBlock.Reference(Utils.StringToBytes(ip, '.'));
          if (!_dhcp.ValidIP(ip_bytes)) {
            throw new Exception("Invalid IP");
          }
        }

        if (_ip_addr.ContainsValue(addr) || _addr_ip.ContainsValue(ip_bytes)) {
          throw new Exception("IP/P2P address is already found");
        }

        _ip_addr.Add(ip_bytes, addr);
        _addr_ip.Add(addr, ip_bytes);
        mcast_addr.Add(addr);
      }
      return Utils.BytesToString(ip_bytes, '.');
    }
    
    /// <summary>
    /// Remove IP to Brunet address mapping.
    /// </summary>
    /// <param name="ip">IP address to remove</param>
    public void RemoveIPMapping(string ip) {
      MemBlock ip_bytes = MemBlock.Reference(Utils.StringToBytes(ip, '.'));
      Address addr = _ip_addr[ip_bytes];
      lock(_sync) {
        _ip_addr.Remove(ip_bytes);
        _addr_ip.Remove(addr);
        mcast_addr.Remove(addr);
      }
    }
    
    /// <summary>
    /// Maps DNS alias to IP address.
    /// </summary>
    /// <param name="alias">Dns alias to map</param>
    /// <param name="ip">IP address to map</param>
    /// <param name="reverse">If true, add reverve mapping</param>
    public void AddDnsMapping(string alias, string ip, bool reverse) {
      lock(_sync) {
        _dns_a.Add(alias, ip);
        if (reverse) {
          _dns_ptr.Add(ip, alias);
        }
      }
    }
    
    /// <summary>
    /// Remove Dns alias mapping.
    /// </summary>
    /// <param name="alias">Dns alias to remove</param>
    /// <param name="reverse">If true, remove reverse mapping</param>
    public void RemoveDnsMapping(string alias, bool reverse) {
      string ip = _dns_a[alias];
      lock (_sync) {
        _dns_a.Remove(alias);
        if (reverse) {
          _dns_ptr.Remove(ip);
        }
      }
    }
  }
  
}
