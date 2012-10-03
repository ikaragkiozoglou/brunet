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
using Brunet.Collections;
using Brunet.Services.Dht;
using Brunet.Util;
using Ipop;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ipop.Dht {
  /**
  <summary>This class provides the ability to lookup names using the Dht.  To
  add a name into the Dht either add a Hostname node into your AddressData node
  inside the IpopConfig or use another method to publish to the Dht.  The format
  of acceptable hostnames is [a-zA-Z0-9-_\.]*.ipop (i.e. must end in
  .ipop)</summary>
  */
  public class DhtDns: Dns {
    /// <summary>Maps names to IP Addresses</summary>
    protected Cache dns_a = new Cache(100);
    /// <summary>Maps IP Addresses to names</summary>
    protected Cache dns_ptr = new Cache(100);
    /// <summary>Use this Dht to resolve names that aren't in cache</summary>
    protected IDht _dht;
    /// <summary>The namespace where the hostnames are being stored.</summary>
    protected String _ipop_namespace;
    protected object _sync;

    /**
    <summary>Create a DhtDns using the specified Dht object</summary>
    <param name="dht">A Dht object used to acquire name translations</param>
    */
    public DhtDns(MemBlock ip, MemBlock netmask, string name_server,
        bool forward_queries, IDht dht, String ipop_namespace) :
      base(ip, netmask, name_server, forward_queries)
    {
      _ipop_namespace = ipop_namespace;
      _dht = dht;
      _sync = new object();
    }

    /**
    <summary>Called during LookUp to perform translation from hostname to IP.
    If an entry isn't in cache, we can try to get it from the Dht.  Throws
    an exception if the name is invalid and returns null if no name is found.
    </summary>
    <param name="name">The name to lookup</param>
    <returns>The IP Address or null if none exists for the name.  If the name
    is invalid, it will throw an exception.</returns>
     */
    public override String AddressLookUp(String name) {
      if(!name.EndsWith(DomainName)) {
        throw new Exception("Invalid Dns name: " + name);
      }
      String ip = (String) dns_a[name];
      if(ip == null) {
        try {
          ip = Encoding.UTF8.GetString((byte[]) _dht.Get(Encoding.UTF8.GetBytes(_ipop_namespace + "." + name))[0]["value"]);
          if(ip != null) {
            lock(_sync) {
              dns_a[name]= ip;
              dns_ptr[ip] = name;
            }
          }
        }
        catch{}
      }
      return ip;
    }

    /**
    <summary>Called during LookUp to perfrom a translation from IP to hostname.
    Entries get here via the AddressLookUp as the Dht does not retain pointer
    lookup information.</summary>
    <param name="IP">The IP to look up.</param>
    <returns>The name or null if none exists for the IP.</returns>
    */
    public override String NameLookUp(String IP) {
      return (String) dns_ptr[IP];
    }
  }
}
