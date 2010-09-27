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
using System.Collections;
using System.Collections.Generic;

using Brunet;
using Brunet.Security;
using Brunet.Applications;
using Brunet.Collections;
using Brunet.Symphony;
using Brunet.Security.PeerSec.Symphony;

using Ipop;
using Ipop.Managed;

#if SVPN_NUNIT
using NUnit.Framework;
#endif

namespace Ipop.SocialVPN {

  public class SocialNode : ManagedIpopNode {

    public const string DNSSUFFIX = "sdns";

    protected ImmutableDictionary<string, SocialUser> _friends;

    protected ImmutableDictionary<string, string> _aliases;

    protected readonly SocialUser _local_user;

    public StructuredNode Node {
      get { return AppNode.Node; }
    }

    public SocialUser LocalUser {
      get { return _local_user; }
    }

    public IDictionary<string, SocialUser> Friends {
      get { return _friends; }
    }

    public SymphonySecurityOverlord Bso {
      get { return AppNode.SymphonySecurityOverlord; }
    }

    public SocialNode(NodeConfig brunetConfig, IpopConfig ipopConfig,
                      string certificate) : base(brunetConfig, ipopConfig) {

      _friends = ImmutableDictionary<string, SocialUser>.Empty;
      _aliases = ImmutableDictionary<string, string>.Empty;
      _local_user = new SocialUser(certificate);
      _local_user.IP = _marad.LocalIP;
      _marad.AddDnsMapping(_local_user.Alias, _local_user.IP, true);

      Bso.CertificateHandler.AddCACertificate(_local_user.GetCert().X509);
      Bso.CertificateHandler.AddSignedCertificate(_local_user.GetCert().X509);
    }

    private bool Verify(SocialUser user) {
      if(_friends.ContainsKey(user.Address)) {
        throw new Exception("Verify failure, address already exists");
      }

      if(_aliases.ContainsKey(user.Alias)) {
        RemoveFriend(_aliases[user.Alias]);
      }

      return true;
    }

    public SocialUser AddFriend(string cert, string uid, string ip) {
      SocialUser user = new SocialUser(cert);

      Verify(user);

      Address addr = AddressParser.Parse(user.Address);
      Bso.CertificateHandler.AddCACertificate(user.GetCert().X509);
      Node.ManagedCO.AddAddress(addr);
      user.IP = _marad.AddIPMapping(ip, addr);
      _marad.AddDnsMapping(user.Alias, user.IP, true);

      _friends = _friends.InsertIntoNew(user.Address, user);
      _aliases = _aliases.InsertIntoNew(user.Alias, user.Address);

      return user;
    }

    public void RemoveFriend(string address) {
      SocialUser user = _friends[address];
      Address addr = AddressParser.Parse(user.Address);
      Node.ManagedCO.RemoveAddress(addr);
      _marad.RemoveIPMapping(user.IP);
      _marad.RemoveDnsMapping(user.Alias, true);

      ImmutableDictionary<string, SocialUser> old;
      _friends = _friends.RemoveFromNew(address, out old);
      ImmutableDictionary<string, string> old2;
      _aliases = _aliases.RemoveFromNew(user.Alias, out old2);
    }

    public void Block(string address) {
      SocialUser user = _friends[address];
      _marad.RemoveIPMapping(user.IP);
    }

    public void Unblock(string address) {
      SocialUser user = _friends[address];
      _marad.AddIPMapping(user.IP, AddressParser.Parse(address));
    }

    public void AddDnsMapping(string alias, string ip) {
      _marad.AddDnsMapping(alias, ip, false);
    }

    public void RemoveDnsMapping(string alias) {
      _marad.RemoveDnsMapping(alias, false);
    }

    public bool IsAllowed(string address) {
      return _marad.mcast_addr.Contains(AddressParser.Parse(address));
    }

    public static new SocialNode CreateNode() {

      SocialConfig social_config;
      NodeConfig node_config;
      IpopConfig ipop_config;

      byte[] certData = SocialUtils.ReadFileBytes("local.cert");
      string certb64 = Convert.ToBase64String(certData);
      social_config = Utils.ReadConfig<SocialConfig>("social.config");
      node_config = Utils.ReadConfig<NodeConfig>(social_config.BrunetConfig);
      ipop_config = Utils.ReadConfig<IpopConfig>(social_config.IpopConfig);

      SocialNode node = new SocialNode(node_config, ipop_config, certb64);
#if !SVPN_NUNIT
      SocialDnsManager sdm = new SocialDnsManager(node);

      SocialConnectionManager manager = new SocialConnectionManager(node,
        node.AppNode.Node.Rpc, sdm);

      JabberNetwork jabber = new JabberNetwork(social_config.JabberID, 
        social_config.JabberPass, social_config.JabberHost, 
        social_config.JabberPort, node.LocalUser.Fingerprint,
        node.LocalUser.Address);

      manager.Register("jabber", jabber);

      HttpInterface http = new HttpInterface(social_config.HttpPort);
      http.ProcessEvent += manager.ProcessHandler;

      node._marad.Resolver = sdm;
      node.Shutdown.OnExit += jabber.Logout;
      node.Shutdown.OnExit += http.Stop;
      http.Start();
#endif

      return node;
    }

    public static void Main(string[] args) {
      
      SocialNode node = SocialNode.CreateNode();
      node.Run();
    }
  }

#if SVPN_NUNIT
  [TestFixture]
  public class SocialNodeTester {
    [Test]
    public void SocialNodeTest() {
      Assert.AreEqual(1,1);
    }
  }
#endif
}
