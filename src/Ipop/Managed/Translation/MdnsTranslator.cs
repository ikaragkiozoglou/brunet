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
   * <summary>A simple mDns translator</summary>
   */
  public class MDnsTranslator : ProtocolTranslator<UdpPacket> {
    public MDnsTranslator(MemBlock local_ip) : base(local_ip) { }
    
    public override bool MatchesProtocol(UdpPacket packet) {
      // TODO: check more than just the port number
      return packet.DestinationPort == 5353;
    }
    
    public override UdpPacket Translate(UdpPacket packet,
                                        MemBlock source_ip,
                                        MemBlock old_source_ip,
                                        MemBlock old_dest_ip) {
      DnsPacket dnsp = new DnsPacket(packet.Payload);
      String ss_ip = DnsPacket.IPMemBlockToString(source_ip);
      bool change = mDnsTranslate(dnsp.Answers, ss_ip);
      change |= mDnsTranslate(dnsp.Authority, ss_ip);
      change |= mDnsTranslate(dnsp.Additional, ss_ip);
      // If we make a change let's make a new packet!
      if(change) {
        dnsp = new DnsPacket(dnsp.ID, dnsp.Query, dnsp.Opcode, dnsp.AA,
                             dnsp.RA, dnsp.RD, dnsp.Questions, dnsp.Answers,
                             dnsp.Authority, dnsp.Additional);
        return new UdpPacket(packet.SourcePort, packet.DestinationPort,
                             dnsp.ICPacket);
      }
      //not a mDns packet after all?
      return packet;
    }
    
    /**
    <summary>Translates mDns RRs, used on Answer and Additional RRs.</summary>
    <param name="responses">An array containing RRs to translate.</param>
    <param name="ss_ip">The defined source ip from the remote end point.</param>
    <returns>True if there was a translation, false otherwise.</returns>
    */
    public static bool mDnsTranslate(Response[] responses, String ss_ip) {
      bool change = false;
      for(int i = 0; i < responses.Length; i++) {
        if(responses[i].Type == DnsPacket.Types.A) {
          change = true;
          Response old = responses[i];
          responses[i] = new Response(old.Name, old.Type, old.Class,
                                         old.CacheFlush, old.Ttl, ss_ip);
        }
        else if(responses[i].Type == DnsPacket.Types.Ptr) {
          Response old = responses[i];
          if(DnsPacket.StringIsIP(old.Name)) {
            responses[i] = new Response(ss_ip, old.Type,  old.Class,
                                        old.CacheFlush, old.Ttl, old.RData);
            change = true;
          }
        }
      }
      return change;
    }
  }
  
  #if ManagedIpopNodeTranslatorsNUNIT
  [TestFixture]
  public class ManagedTester {
    [Test]
    public void Test() { 
      MemBlock mdnsm = MemBlock.Reference(new byte[] {0x00, 0x00, 0x00, 0x00,
        0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x0E, 0x64, 0x61, 0x76,
        0x69, 0x64, 0x69, 0x77, 0x2D, 0x6C, 0x61, 0x70, 0x74, 0x6F, 0x70, 0x05,
        0x6C, 0x6F, 0x63, 0x61, 0x6C, 0x00, 0x00, 0xFF, 0x00, 0x01, 0xC0, 0x0C,
        0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x78, 0x00, 0x04, 0x0A, 0xFE,
        0x00, 0x01});
      DnsPacket mdns = new DnsPacket(mdnsm);
      String ss_ip = "10.254.112.232";
      bool change = MDnsTranslator.mDnsTranslate(mdns.Answers, ss_ip);
      change |= MDnsTranslator.mDnsTranslate(mdns.Authority, ss_ip);
      change |= MDnsTranslator.mDnsTranslate(mdns.Additional, ss_ip);
      // If we make a change let's make a new packet!
      if(change) {
          mdns = new DnsPacket(mdns.ID, mdns.Query, mdns.Opcode, mdns.AA,
                               mdns.RA, mdns.RD, mdns.Questions, mdns.Answers,
                               mdns.Authority, mdns.Additional);
      }
      Assert.AreEqual(mdns.Authority[0].Name, "davidiw-laptop.local", "Name");
      Assert.AreEqual(mdns.Authority[0].Type, DnsPacket.Types.A, "Type");
      Assert.AreEqual(mdns.Authority[0].Class, DnsPacket.Classes.IN, "Class");
      Assert.AreEqual(mdns.Authority[0].CacheFlush, false, "CacheFlush");
      Assert.AreEqual(mdns.Authority[0].Ttl, 120, "Ttl");
      Assert.AreEqual(mdns.Authority[0].RData, "10.254.112.232", "RData");
    }
  }
  #endif
}
