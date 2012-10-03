/*
Copyright (C) 2010  David Wolinsky <davidiw@ufl.edu>, University of Florida
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

using System;
using System.Text;
using Brunet.Util;
using Ipop;
using NetworkPackets;
using NetworkPackets.Dns;
using System.Collections;
using Brunet.Applications;

#if ManagedIpopNodeTranslatorsNUNIT
using NUnit.Framework;
#endif

namespace Ipop.Managed.Translation {
  /**
   * <summary>Does a cheap find-and-replace on SIP packets. This SIP translator
   * is not to be confused with SimpleIpTranslator.</summary>
   */
  public class SipTranslator : ProtocolTranslator<UdpPacket> {
    
    public SipTranslator(MemBlock local_ip) : base(local_ip) { }
    
    public override bool MatchesProtocol(UdpPacket packet) {
      return packet.DestinationPort >= 5060 && packet.DestinationPort < 5100;
    }
    
    ///The evil SIP translator requires that I change the entire API to fit it
    ///because it does find-replace, it needs the old values
    public override UdpPacket Translate(UdpPacket packet,
                                        MemBlock source_ip,
                                        MemBlock old_source_ip,
                                        MemBlock old_dest_ip) {
      packet = SimpleUDPTranslate(packet, source_ip, old_source_ip, _local_ip,
                                             old_dest_ip, "SIP/2.0");
      return packet;
    }
    
    /// <summary>
    /// Check to see if it contains packet_id and translates with an ascii find-
    /// replace system
    /// </summary>
    /// <param name="payload">UDP payload</param>
    /// <param name="new_source_ip">New source IP</param>
    /// <param name="old_source_ip">Old source IP</param>
    /// <param name="new_dest_ip">New destination IP</param>
    /// <param name="old_dest_ip">Old destination IP</param>
    /// <param name="packet_id">Eg. "SIP/2.0" or "HTTP/1.1"</param>
    /// <returns>Returns the translated UDP packet</returns>
    public UdpPacket SimpleUDPTranslate(UdpPacket udpp,
                                         MemBlock new_source_ip,
                                         MemBlock old_source_ip,
                                         MemBlock new_dest_ip,
                                         MemBlock old_dest_ip,
                                         string packet_id) {
      string s_new_source_ip = Utils.MemBlockToString(new_source_ip, '.');
      string s_old_source_ip = Utils.MemBlockToString(old_source_ip, '.');
      string s_new_dest_ip = Utils.MemBlockToString(new_dest_ip, '.');
      string s_old_dest_ip = Utils.MemBlockToString(old_dest_ip, '.');
      
      MemBlock payload = ManagedNodeHelper.TextTranslate(
                                             udpp.Payload, s_old_source_ip,
                                             s_old_dest_ip, s_new_source_ip,
                                             s_new_dest_ip, packet_id
                                           ); 
      return new UdpPacket(udpp.SourcePort, udpp.DestinationPort, payload);
    }
  }
}
