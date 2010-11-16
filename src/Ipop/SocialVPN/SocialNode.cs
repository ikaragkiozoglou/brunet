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
using System.IO;
using System.Security.Cryptography;

using Brunet;
using Brunet.Security;
using Brunet.Applications;
using Brunet.Collections;
using Brunet.Concurrent;
using Brunet.Connections;
using Brunet.Symphony;
using Brunet.Security.PeerSec.Symphony;

using Ipop;
using Ipop.Managed;

#if SVPN_NUNIT
using NUnit.Framework;
#endif

namespace Ipop.SocialVPN {

  public class SocialNode : ManagedIpopNode {

    public const string CONFIGPATH = "social.config";

    protected readonly WriteOnce<SocialUser> _user;

    protected ImmutableDictionary<string, SocialUser> _friends;

    protected readonly RSACryptoServiceProvider _rsa;

    protected readonly ManagedConnectionOverlord _mco;

    protected readonly string _address;

    public StructuredNode Node {
      get { return AppNode.Node; }
    }

    public SymphonySecurityOverlord Bso {
      get { return AppNode.SymphonySecurityOverlord; }
    }

    public SocialUser LocalUser {
      get { return _user.Value; }
    }

    public IDictionary<string, SocialUser> Friends {
      get { return _friends; }
    }

    public string Address {
      get { return _address; }
    }

    public string IP {
      get { return _marad.LocalIP; }
    }

    public SocialNode(NodeConfig brunetConfig, IpopConfig ipopConfig,
      RSACryptoServiceProvider rsa) 
      : base(brunetConfig, ipopConfig) {
      _friends = ImmutableDictionary<string, SocialUser>.Empty;
      _rsa = rsa;
      _address = AppNode.Node.Address.ToString();
      _user = new WriteOnce<SocialUser>();
      _mco = new ManagedConnectionOverlord(AppNode.Node);
      AppNode.Node.AddConnectionOverlord(_mco);
    }

    public void SetUid(string uid) {
      SetUid(uid, null);
    }

    public void SetUid(string uid, string pcid) {

      if(_user.Value != null) {
        return;
      }

      string country = "US";
      string version = "0.4";
      string name = uid;

      if(pcid == null || pcid == String.Empty) {
        pcid = System.Net.Dns.GetHostName();
      }

      CertificateMaker cm = new CertificateMaker(country, version, pcid,
                                                 name, uid, _rsa, 
                                                 this.Address);
      Certificate cert = cm.Sign(cm, _rsa);
      string certificate = Convert.ToBase64String(cert.X509.RawData);
      SocialUser user = new SocialUser(certificate, this.IP, null);
      _user.Value = user;

      Bso.CertificateHandler.AddCACertificate(user.X509);
      Bso.CertificateHandler.AddSignedCertificate(user.X509);
    }

    public SocialUser AddFriend(string address, string cert, string uid, 
      string ip) {

      if(_friends.ContainsKey(address)) {
        throw new Exception("Address already exists");
      }

      Address addr = AddressParser.Parse(address);
      string new_ip = _marad.AddIPMapping(ip, addr);
      SocialUser user = new SocialUser(cert, new_ip, null);

      Bso.CertificateHandler.AddCACertificate(user.X509);
      _mco.Set(addr);
      _friends = _friends.InsertIntoNew(address, user);

      return user;
    }

    public void RemoveFriend(string address) {
      SocialUser user = _friends[address];
      Address addr = AddressParser.Parse(user.Address);

      _mco.Unset(addr);
      _marad.RemoveIPMapping(user.IP);

      ImmutableDictionary<string, SocialUser> old;
      _friends = _friends.RemoveFromNew(address, out old);
    }

    public void Block(string address) {
      SocialUser user = _friends[address];
      _marad.RemoveIPMapping(user.IP);
    }

    public void Unblock(string address) {
      SocialUser user = _friends[address];
      _marad.AddIPMapping(user.IP, AddressParser.Parse(address));
    }

    public bool IsAllowed(string address) {
      return _marad.mcast_addr.Contains(AddressParser.Parse(address));
    }

    public string GetStats(string address) {
      return _marad.GetStats(address);
    }

    public void Close() {
      System.Threading.ThreadPool.QueueUserWorkItem(HandleShutdown, this);
      System.Threading.Thread.Sleep(1000);
      Environment.Exit(0);
    }

    public void HandleShutdown(object state) {
      IpopNode node = state as IpopNode;
      node.Shutdown.Exit();
    }

    public static new SocialNode CreateNode() {

      SocialConfig social_config;
      NodeConfig node_config;
      IpopConfig ipop_config;
      RSACryptoServiceProvider rsa = new RSACryptoServiceProvider();

      if(File.Exists(CONFIGPATH)) {
        social_config = Utils.ReadConfig<SocialConfig>(CONFIGPATH);
      }
      else {
        social_config = SocialUtils.CreateConfig();
      }

      node_config = Utils.ReadConfig<NodeConfig>(social_config.BrunetConfig);
      ipop_config = Utils.ReadConfig<IpopConfig>(social_config.IpopConfig);

      if(!File.Exists(node_config.Security.KeyPath) || 
        node_config.NodeAddress == null) {
        node_config.NodeAddress = Utils.GenerateAHAddress().ToString();
        Utils.WriteConfig(social_config.BrunetConfig, node_config);

        SocialUtils.WriteToFile(rsa.ExportCspBlob(true), 
          node_config.Security.KeyPath);
      }
      else if(File.Exists(node_config.Security.KeyPath)) {
        rsa.ImportCspBlob(SocialUtils.ReadFileBytes(
          node_config.Security.KeyPath));
      }

      SocialNode node = new SocialNode(node_config, ipop_config, rsa);
#if !SVPN_NUNIT
      SocialDnsManager sdm = new SocialDnsManager(node);
      SocialStatsManager ssm = new SocialStatsManager(node);

      SocialConnectionManager manager = new SocialConnectionManager(node,
        node.AppNode.Node.Rpc, sdm, ssm, social_config);

      JabberNetwork jabber = new JabberNetwork(social_config.JabberID, 
        social_config.JabberPass, social_config.JabberHost, 
        social_config.JabberPort);

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
