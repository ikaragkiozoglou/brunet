/*
Copyright (C) 2010  Benjamin Woodruff <odetopi.e@gmail.com>

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
   * <summary>Provides an interface for all packet translators to implement.
   * It handles identification and translation APIs.</summary>
   */
  public interface IProtocolTranslator<P> where P : DataPacket {
    /**
     * <summary>Used to determine if a specific packet is translatable</summary>
     * <param name="packet">A DataPacket; packet-type is decided by an
     * implementing class</param>
     * <returns>True if the packet can be translated by this translator, false
     * otherwise</returns>
     */
    bool MatchesProtocol(P packet);
    
    /**
     * <summary>Takes a packet, and translates it to use the new source_ip value
     * (and usually the local ip, as defined in the abstract ProtocolTranslator
     * class)
     * <param name="packet">A DataPacket to translate</param>
     * <param name="source_ip">
     *   The <b>new</b> source ip address for the packet
     * </param>
     * <returns>The new, translated packet</returns>
     */
    P Translate(P packet, MemBlock source_ip,
                          MemBlock old_source_ip,
                          MemBlock old_dest_ip);
  }
  
  
  public abstract class ProtocolTranslator<P> : IProtocolTranslator<P> where P : DataPacket {
    protected MemBlock _local_ip;
    public ProtocolTranslator(MemBlock local_ip) {
      _local_ip = local_ip;
    }
    
    public abstract bool MatchesProtocol(P packet);
    public abstract P Translate(P packet, MemBlock source_ip,
                                          MemBlock old_source_ip,
                                          MemBlock old_dest_ip);
  }
}
