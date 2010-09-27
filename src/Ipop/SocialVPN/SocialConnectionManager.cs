/*
Copyright (C) 2010 Pierre St Juste <ptony82@ufl.edu>, University of Florida

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
using System.Threading;

using Brunet;
using Brunet.Util;
using Brunet.Concurrent;
using Brunet.Collections;
using Brunet.Messaging;
using Brunet.Symphony;
using Brunet.Applications;

#if SVPN_NUNIT
using System.Xml;
using jabber;

using NUnit.Framework;
using NMock2;
#endif

namespace Ipop.SocialVPN {

  public interface IRpcSender {
    void SendRpcMessage(string address, string method, string msg);
  }

  public class SocialConnectionManager : IRpcHandler, IRpcSender {

    public class NetworkState {

      public string Name;

      public string Message;
    }

    public class SocialState {

      public SocialUser LocalUser;

      public SocialUser[] Friends;

      public NetworkState[] Networks;

      public List<string> Pending;
    }

    public enum StatusTypes {
      Online,
      Offline,
      Blocked,
      Pending
    }
 
    public const string STATEPATH = "state.xml";

    public const string RPCID = "SocialRPC";

    public const double TIMEOUT = 60.0;

    public const int PERIOD = 10000;

#if SVPN_NUNIT
    protected readonly ISocialNode _node;
#else
    protected readonly SocialNode _node;
#endif
    protected readonly RpcManager _rpc;

    protected readonly SocialDnsManager _sdm;

    protected ImmutableDictionary<string, DateTime> _times;

    protected ImmutableDictionary<string, ISocialNetwork> _networks;

    protected ImmutableDictionary<string, string> _fprs;

    protected ImmutableList<string> _pending;

    protected ImmutableList<string> _blocked;

    protected readonly Timer _timer;

    private int _beat_counter;

    private bool _auto_allow;

    public ImmutableList<string> Pending { get { return _pending; }}

    public ImmutableList<string> Blocked { get { return _blocked; }}

#if SVPN_NUNIT
    public SocialConnectionManager(ISocialNode node) {
#else
    public SocialConnectionManager(SocialNode node, RpcManager rpc,
      SocialDnsManager sdm) {
      _rpc = rpc;
      _rpc.AddHandler(RPCID, this);
      _sdm = sdm;
#endif
      _node = node;
      _networks = ImmutableDictionary<string, ISocialNetwork>.Empty;
      _fprs = ImmutableDictionary<string, string>.Empty;
      _times = ImmutableDictionary<string, DateTime>.Empty;
      _pending = ImmutableList<string>.Empty;
      _blocked = ImmutableList<string>.Empty;
      _beat_counter = 0;
      _auto_allow = false;
      _sdm.Sender = this;
#if !SVPN_NUNIT
      _timer = new Timer(TimerHandler, _beat_counter, PERIOD, PERIOD);
      LoadState();
#endif
    }

    public void Register(string name, ISocialNetwork network) {
      _networks = _networks.InsertIntoNew(name, network);
    }

    public void TimerHandler(object obj) {
      if(_beat_counter % 6 == 0) {
        PingFriends();
        GetPending();
        GetState(true);
      }
      VerifyFriends();
      _beat_counter++;
    }

    public void ProcessHandler(Object obj, EventArgs eargs) {
      Dictionary <string, string> request = (Dictionary<string, string>)obj;
      string method = String.Empty;
      if (request.ContainsKey("m")) {
        method = request["m"];
      }

      if(request.ContainsKey("a") && !request["a"].StartsWith("brunet")) {
        request["a"] = "brunet:node:" + request["a"];
      }
      switch(method) {
        case "add":
          SendCertRequest(request["a"]);
          if(request.ContainsKey("f")) {
            _fprs = _fprs.InsertIntoNew(request["a"], request["f"]);
          }
          break;

        case "block":
          Block(request["a"]);
          break;

        case "unblock":
          Unblock(request["a"]);
          break;

        case "login":
          Login(request["n"], request["u"], request["p"]);
          break;

        case "logout":
          Logout(request["n"]);
          break;

        case "sdns.search":
          _sdm.SearchFriends(request["q"], this);
          request["response"] = _sdm.GetState(request["q"]);
          return;

        case "sdns.add":
          _sdm.AddDnsMapping(request["n"], request["i"]);
          request["response"] = _sdm.GetState(request["q"]);
          return;

        case "sdns.del":
          _sdm.DeleteDnsMapping(request["n"]);
          request["response"] = _sdm.GetState(request["q"]);
          return;

        case "sdns.state":
          request["response"] = _sdm.GetState(request["q"]);
          return;

        default:
          break;
      }
      request["response"] = GetState(true);
    }

    public void HandleRpc(ISender caller, string method, IList args,
                          object req_state) {
      object result = null;
      try {
        switch(method) {
          case "Ping":
            result = HandlePing((string)args[0], (string)args[1]);
            break;

          case "AddCertRequest":
            result = AddCertRequest((string)args[0], (string)args[1]);
            break;

          case "AddCertReply":
            result = AddCertReply((string)args[0], (string)args[1]);
            break;

          case "SearchMapping":
            result = _sdm.SearchMapping((string)args[0], (string)args[1], 
              this);
            break;

          case "AddTmpMapping":
            result = _sdm.AddTmpMapping((string)args[0], (string)args[1]);
            break;

          default:
            result = new InvalidOperationException("Invalid Method");
            break;
        }
      } catch (Exception e) {
        result = e.Message;
      }
      _rpc.SendResult(req_state, result);
    }

    public void SendRpcMessage(string address, string method, string msg) {
      SendRpcMessage(address, method, msg, true);
    }

    protected void SendRpcMessage(string address, string method,
      string query, bool secure) {
      Address addr = AddressParser.Parse(address);
      SendRpcMessage(addr, method, query, secure);
    }

    protected void SendRpcMessage(Address addr, string method, 
      string query, bool secure) {
#if !SVPN_NUNIT
      string meth_call = RPCID + "." + method;
      Channel q = new Channel();
      q.CloseAfterEnqueue();
      q.CloseEvent += delegate(object obj, EventArgs eargs) {
        RpcResult res = (RpcResult) q.Dequeue();
        string result = (string) res.Result;
        if(method == "Ping") {
          _times = _times.InsertIntoNew(result, DateTime.Now);
        }
      };

      ISender sender;
      if(!secure) {
        sender = new AHExactSender(_node.Node, addr);
      }
      else {
        sender = _node.Bso.GetSecureSender(addr);
      }
      _rpc.Invoke(sender, q, meth_call, _node.LocalUser.Address, query);
#endif
    }

    protected void Login(string name, string uid, string password) {
      _networks[name].Login(uid, password);
    }

    protected void Logout(string name) {
      _networks[name].Logout();
    }

    public void AddFriend(string cert) {
      SocialUser user = _node.AddFriend(cert, null, null);

      if(_pending.Contains(user.Address)) {
        _pending = _pending.RemoveFromNew(user.Address);
      }

      if(!_auto_allow && !IsVerified(user)) {
        _node.Block(user.Address);
      }
    }

    protected bool IsVerified(SocialUser user) {
      string fpr;
      if(_fprs.TryGetValue(user.Address, out fpr)) {
        if(user.Fingerprint == fpr) {
          return true;
        }
      }
      return false;
    }

    protected bool IsOffline(string address) {
      if(!_times.ContainsKey(address)) {
        return true;
      }
      else {
        TimeSpan span = DateTime.Now - _times[address];
        if(span.TotalSeconds > TIMEOUT) {
          return true;
        }
      }
      return false;
    }

    public void Block(string address) {
      _node.Block(address);
      _blocked = _blocked.PushIntoNew(address);
    }

    public void Unblock(string address) {
      _node.Unblock(address);
      _blocked = _blocked.RemoveFromNew(address);
    }

    protected void PingFriends() {
      foreach(SocialUser user in _node.Friends.Values) {
        if(_node.IsAllowed(user.Address)) {
          SendRpcMessage(user.Address, "Ping", user.Address, true);
        }
      }
    }

    public void VerifyFriends() {
      foreach(SocialUser user in _node.Friends.Values) {
        if(!_node.IsAllowed(user.Address) && 
          !_blocked.Contains(user.Address)) {
          foreach(ISocialNetwork network in _networks.Values) {
            IDictionary<string, string> fprs = network.Fingerprints;
            IDictionary<string, string> addrs = network.Addresses;
            string fpr;
            string uid;
            if(fprs.TryGetValue(user.Address, out fpr) && 
               fpr == user.Fingerprint &&
               addrs.TryGetValue(user.Address, out uid) &&
               uid == user.Uid) {
              _node.Unblock(user.Address);
              break;
            }
          }
        }
      }
    }

    public void GetPending() {
      foreach (ISocialNetwork network in _networks.Values) {
        foreach(string address in network.Addresses.Keys) {
          SendCertRequest(address);
        }
      }
      foreach(string address in _pending) {
        SendCertRequest(address);
      }
    }

    protected string HandlePing(string address, string msg) {
      _times = _times.InsertIntoNew(address, DateTime.Now);

      if(_node.IsAllowed(address)) {
        return _node.LocalUser.Address;
      }
      else {
        return String.Empty;
      }
    }

    protected void SendCertRequest(string address) {
      if(!_node.Friends.ContainsKey(address) && 
         address != _node.LocalUser.Address) {
        SendRpcMessage(address, "AddCertRequest", _node.LocalUser.Certificate, 
          false);
        if(!_pending.Contains(address)) {
          _pending = _pending.PushIntoNew(address);
        }
      }
    }

    protected string AddCertRequest(string address, string msg) {
      SendRpcMessage(address, "AddCertReply", _node.LocalUser.Certificate, 
        false);
      AddFriend(msg);
      return _node.LocalUser.Address;
    }

    protected string AddCertReply(string address, string msg) {
      AddFriend(msg);
      return _node.LocalUser.Address;
    }

    public string GetState(bool write) {
#if SVPN_NUNIT
      return String.Empty;
#else
      SocialState state = new SocialState();
      state.LocalUser = _node.LocalUser;
      state.Friends = new SocialUser[_node.Friends.Values.Count];
      state.Networks = new NetworkState[_networks.Values.Count];
      state.Pending = new List<string>();

      int i = 0;
      foreach (KeyValuePair<string, ISocialNetwork> kvp 
        in _networks) {
        state.Networks[i] = new NetworkState();
        state.Networks[i].Name = kvp.Key;
        state.Networks[i].Message = kvp.Value.Message;
      }

      foreach (string address in _pending) {
        if(!_node.Friends.ContainsKey(address)) {
          state.Pending.Add(address);
        }
      }

      i = 0;
      foreach(SocialUser user in _node.Friends.Values) {
        SocialUser tmp_user = new SocialUser(user.Certificate);
        tmp_user.IP = user.IP;
        state.Friends[i++] = tmp_user;
        if(_node.IsAllowed(user.Address)) {
          if(IsOffline(user.Address)) {
            tmp_user.Status = StatusTypes.Offline.ToString();
          }
          else {
            tmp_user.Status = StatusTypes.Online.ToString();
          }
        }
        else {
          tmp_user.Status = StatusTypes.Blocked.ToString();
        }
      }

      if(write) {
        Utils.WriteConfig(STATEPATH, state);
      }

      return SocialUtils.ObjectToXml<SocialState>(state);
#endif
    }

    public void LoadState() {
#if !SVPN_NUNIT
      try {
        SocialState state = Utils.ReadConfig<SocialState>(STATEPATH);
        foreach (SocialUser user in state.Friends) {
          _node.AddFriend(user.Certificate, user.Uid, user.IP);
          if(user.Status == StatusTypes.Blocked.ToString()) {
            _node.Block(user.Address);
          }
        }
      }
      catch {}
#endif
    }
  }

  public interface ISocialNode {
    IDictionary<string, SocialUser> Friends { get;}
    SocialUser LocalUser { get; }
    SocialUser AddFriend(string cert, string uid, string ip);
    void RemoveFriend(string address);
    void Block(string address);
    void Unblock(string address);
    void AddDnsMapping(string alias, string ip);
    void RemoveDnsMapping(string alias);
    bool IsAllowed(string address);
  }

#if SVPN_NUNIT
  [TestFixture]
  public class SocialConnectionManagerTester {
    [Test]
    public void SocialConnectionManagerTest() {
      Random rand = new Random();
      byte[] certData = SocialUtils.ReadFileBytes("local.cert");
      string certb64 = Convert.ToBase64String(certData);
      SocialUser user = new SocialUser(certb64);

      ImmutableDictionary<string, SocialUser> friends =
        ImmutableDictionary<string, SocialUser>.Empty;

      ImmutableDictionary<string, string> addresses =
        ImmutableDictionary<string, string>.Empty;

      ImmutableDictionary<string, string> fprs =
        ImmutableDictionary<string, string>.Empty;

      Mockery mocks = new Mockery();
      ISocialNode node = mocks.NewMock<ISocialNode>();
      ISocialNetwork network = mocks.NewMock<ISocialNetwork>();

      Stub.On(node).GetProperty("Friends").Will(
        Return.Value(friends));

      Stub.On(node).GetProperty("LocalUser").Will(
        Return.Value(user));

      Stub.On(node).Method("IsAllowed").Will(
       Return.Value(true));

      Stub.On(network).GetProperty("Addresses").Will(
        Return.Value(addresses));

      Stub.On(network).GetProperty("Fingerprints").Will(
        Return.Value(fprs));

      SocialConnectionManager scm = new SocialConnectionManager(node);

      string fpr1 = rand.NextDouble().ToString();
      string addr1 = rand.NextDouble().ToString();
      string jid1 = rand.NextDouble().ToString();

      addresses = addresses.InsertIntoNew(addr1, jid1);
      fprs = fprs.InsertIntoNew(addr1, fpr1);

      scm.Register("test", network);
      scm.GetPending();

      Assert.AreEqual(addr1 ,scm.Pending[0]);
    }
  } 
#endif
}
