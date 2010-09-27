/*
Copyright (C) 2009 Pierre St Juste <ptony82@ufl.edu>, University of Florida

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
using System.Xml;
using System.Collections.Generic;
using System.Threading;

using jabber;
using jabber.client;
using jabber.protocol;
using jabber.protocol.client;

using Brunet.Collections;
using Brunet.Xmpp;
using Brunet.Concurrent;

#if SVPN_NUNIT
using NUnit.Framework;
#endif

namespace Ipop.SocialVPN {

  public class SvpnMsg : Element
  {
    public const string NAMESPACE = "Ipop:SocialVPN:Message";
    public const string DATA = "data";

    public SvpnMsg(XmlDocument doc, string data) : 
      base("query", NAMESPACE, doc)
    {
      SetElem(DATA, data);
    }

    public SvpnMsg(string prefix, XmlQualifiedName qname, 
      XmlDocument doc) : base(prefix, qname, doc)
    {
    }

    public string Data { get { return GetElem(DATA); } }
  }

  public class SvpnFactory : jabber.protocol.IPacketTypes
  {
    private static QnameType[] _s_qnt = new QnameType[] {
      new QnameType("query", SvpnMsg.NAMESPACE, typeof(SvpnMsg)),
    };

    QnameType[] IPacketTypes.Types { get { return _s_qnt; } }

    public static void HandleStreamInit(object sender, ElementStream stream)
    {
      stream.AddFactory(new SvpnFactory());
    }
  }

  public class JabberNetwork : ISocialNetwork {

    public enum PacketTypes {
      Request,
      Reply
    }

    public const char DELIM = '_';

    public const int PUB_PERIOD = 60000;

    protected ImmutableDictionary<string, string> _addresses;

    protected ImmutableDictionary<string, string> _fingerprints;

    protected readonly XmlDocument _doc;

    protected readonly XmppService _xmpp;

    protected readonly int _port;

    protected readonly string _data;

    protected readonly string _host;

    protected readonly Timer _timer;

    protected string _message;

    public string Message { get { return _message; }}

    public IDictionary<string, string> Addresses {
      get { return _addresses; }
    }

    public IDictionary<string, string> Fingerprints {
      get { return _fingerprints; }
    }

    public JabberNetwork(string uid, string password, 
      string host, string port, string fingerprint, string address) {
      _addresses = ImmutableDictionary<string, string>.Empty;
      _fingerprints = ImmutableDictionary<string, string>.Empty;
      _doc = new XmlDocument();
      _port = Int32.Parse(port);
      _host = host;
      _data = fingerprint + DELIM + address;
      _message = "Offline";
      _timer = new Timer(TimerHandler, null, PUB_PERIOD, PUB_PERIOD);

      _xmpp = new XmppService(uid, password, _port);
      _xmpp.Register(typeof(SvpnMsg), HandleSvpnMsg);
      _xmpp.OnStreamInit += SvpnFactory.HandleStreamInit;
      _xmpp.OnAuthenticate += HandleAuthenticate;
      _xmpp.OnAuthError += HandleAuthError;
      _xmpp.OnPresence += HandlePresence;
      _xmpp.OnError += HandleError;
    }

    protected void HandleAuthenticate(object sender) {
      _message = "Online as " + _xmpp.JID.User + "@" + _xmpp.JID.Server;
    }

    protected void HandleAuthError(object sender, XmlElement rp) {
      _message = "Login Failed";
    }

    protected void HandleError(object sender, Exception e) {
      _message = "error occured";
    }

    protected void HandlePresence(object sender, Presence pres) {
      if(pres.From.Resource != null && 
        pres.From != _xmpp.JID && 
        pres.From.Resource.StartsWith(XmppService.RESOURCE_NS)) {
        string data = _data + DELIM + PacketTypes.Request.ToString();
        _xmpp.SendTo(new SvpnMsg(_doc, data), pres.From);
      }
    }

    public void HandleSvpnMsg(Element msg, JID from) {
      SvpnMsg request = msg as SvpnMsg;
      if(request == null) {
        return;
      }

      string[] parts = request.Data.Split(DELIM);
      string jid = from.User + "@" + from.Server;

      if(!_fingerprints.ContainsKey(parts[1])) {
        _fingerprints = _fingerprints.InsertIntoNew(parts[1], parts[0]);
      }

      if(!_addresses.ContainsKey(jid)) {
        _addresses = _addresses.InsertIntoNew(parts[1], jid);
      }

      if(parts[2] == PacketTypes.Request.ToString() && _xmpp.JID != from) {
        string data = _data + DELIM + PacketTypes.Reply.ToString();
        _xmpp.SendTo(new SvpnMsg(_doc, data), from);
      }
    }

    protected void Publish() {
      if(_xmpp != null && _xmpp.IsAuthenticated) {
        string data = _data + DELIM + PacketTypes.Request.ToString();
        _xmpp.SendBroadcast(new SvpnMsg(_doc, data));
      }
    }

    public void TimerHandler(object obj) {
      Publish();
    }

    public void Login(string uid, string password) {
      if(_message != "Online") {
        _xmpp.Connect(uid, password, _host, _port);
        _message = "...connecting...";
      }
    }

    public void Logout() {
      _xmpp.Logout();
      _message = "Offline";
    }

  }

#if SVPN_NUNIT
  [TestFixture]
  public class JabberNetworkTester {
    [Test]
    public void JabberNetworkTest() {
      string uid = "ptony82@ufl.edu";
      string password = "password";
      string host = "host";
      string port = "5222";
      string fpr = "fingerprint";
      string address = "adddress";
      JabberNetwork network = new JabberNetwork(uid, password, host, port,
        fpr, address);

      Random rand = new Random();
      XmlDocument doc = new XmlDocument();

      string fpr1 = rand.NextDouble().ToString();
      string addr1 = rand.NextDouble().ToString();
      string data1 = fpr1 + JabberNetwork.DELIM + addr1 +
        JabberNetwork.DELIM + JabberNetwork.PacketTypes.Request.ToString();

      string fpr2 = rand.NextDouble().ToString();
      string addr2 = rand.NextDouble().ToString();
      string data2 = fpr2 + JabberNetwork.DELIM + addr2 +
        JabberNetwork.DELIM + JabberNetwork.PacketTypes.Reply.ToString();

      JID from = new JID("ptony82@ufl.edu");
      SvpnMsg msg1 = new SvpnMsg(doc, data1);
      SvpnMsg msg2 = new SvpnMsg(doc, data2);
      string jid = from.User + "@" + from.Server;

      network.HandleSvpnMsg(msg1, from);

      Assert.AreEqual(network.Addresses[addr1], jid);
      Assert.AreEqual(network.Fingerprints[addr1], fpr1);
      
      network.HandleSvpnMsg(msg1, from);

      Assert.AreEqual(network.Addresses[addr1], jid);
      Assert.AreEqual(network.Fingerprints[addr1], fpr1);
      
      network.HandleSvpnMsg(msg2, from);

      Assert.AreEqual(network.Addresses[addr2], jid);
      Assert.AreEqual(network.Fingerprints[addr2], fpr2);

      network.HandleSvpnMsg(msg2, from);

      Assert.AreEqual(network.Addresses[addr2], jid);
      Assert.AreEqual(network.Fingerprints[addr2], fpr2);
      

    }
  } 
#endif
}
