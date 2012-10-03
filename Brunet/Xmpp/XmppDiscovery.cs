/*
Copyright (C) 2010 David Wolinsky <davidiw@ufl.edu>, University of Florida

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

using Brunet.Transport;
using jabber;
using jabber.protocol;
using System;
using System.Collections;
using System.Xml;

namespace Brunet.Xmpp {
  /// <summary>Xmpp provides a means for discovery and as a relay.</summary>
  public class XmppDiscovery : Discovery {
    protected static readonly IList EMPTY_LIST = new ArrayList(0);
    protected readonly XmppService _xmpp;
    protected readonly string _realm;
    protected readonly string _local_ta;
    protected int _ready;

    /// <summary>A rendezvous service for finding remote TAs and sharing
    /// our TA, so that peers can become connected.</summary>
    public XmppDiscovery(ITAHandler ta_handler, XmppService xmpp, string realm) :
      base(ta_handler)
    {
      _realm = realm;
      _xmpp = xmpp;
      _ready = 0;
      _xmpp.Register(typeof(XmppTARequest), HandleRequest);
      _xmpp.Register(typeof(XmppTAReply), HandleReply);
      _xmpp.OnStreamInit += XmppTAFactory.HandleStreamInit;

      // Operations aren't valid until Xmpp has authenticated with the servers
      xmpp.OnAuthenticate += HandleAuthenticate;
      if(xmpp.IsAuthenticated) {
        HandleAuthenticate(null);
      }
    }

    /// <summary>Called once Xmpp has authenticated with the servers.  This
    /// generates our Xmpp server now that we have our complete JID.  username,
    /// server, and resource.</summary>
    protected void HandleAuthenticate(object sender)
    {
      System.Threading.Interlocked.Exchange(ref _ready, 1);
    }

    /// <summary>Some remote entity inquired for our TA, but we only reply if
    /// we are in the same realm.</summary>
    protected void HandleRequest(Element msg, JID from)
    {
      XmppTARequest xt = msg as XmppTARequest;
      if(xt == null) {
        return;
      }

      IList tas = EMPTY_LIST;
      if(xt.Realm.Equals(_realm)) {
        tas = LocalTAsToString(20);
      }
      XmppTAReply xtr = new XmppTAReply(new XmlDocument(), _realm, tas);
      _xmpp.SendTo(xtr, from);
    }

    /// <summary>We got a reply to one of our requests.  Let's send the result
    /// back to the TA listener.</summary>
    protected void HandleReply(Element msg, JID from)
    {
      XmppTAReply xt = msg as XmppTAReply;
      if(xt == null) {
        return;
      }

      if(!xt.Realm.Equals(_realm)) {
        return;
      }

      UpdateRemoteTAs(xt.TransportAddresses);
    }

    /// <summary>We need some TAs, let's query our friends to see if any of them
    /// can supply us with some.<summary>
    protected override void SeekTAs(DateTime now)
    {
      if(_ready == 0) {
        return;
      }

      XmppTARequest xtr = new XmppTARequest(new XmlDocument(), _realm);
      _xmpp.SendRandomMulticast(xtr);
    }
  }
}
