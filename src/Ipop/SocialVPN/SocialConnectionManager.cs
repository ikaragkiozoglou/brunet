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

    public class FriendState {

      public string Certificate;

      public string IP;

      public string Status;
    }

    public class FileState {

      public string Uid;

      public string PCID;

      public FriendState[] Friends;
    }

    public class SocialState {

      public SocialUser LocalUser;

      public SocialUser[] Friends;

      public NetworkState[] Networks;

      public string[] Pending;
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

    protected readonly SocialNode _node;

    protected RpcManager _rpc;

    protected readonly SocialDnsManager _sdm;

    protected readonly SocialStatsManager _ssm;

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

    public SocialConnectionManager(SocialNode node,
      SocialDnsManager sdm, SocialStatsManager ssm, SocialConfig config) {
      _sdm = sdm;
      _rpc = null;
      _node = node;
      _ssm = ssm;
      _networks = ImmutableDictionary<string, ISocialNetwork>.Empty;
      _fprs = ImmutableDictionary<string, string>.Empty;
      _times = ImmutableDictionary<string, DateTime>.Empty;
      _pending = ImmutableList<string>.Empty;
      _blocked = ImmutableList<string>.Empty;
      _beat_counter = 0;
      _auto_allow = config.AutoFriend;
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
      if (_rpc == null && _beat_counter % 2 == 0) {
        _rpc = _node.Node.Rpc;
        _rpc.AddHandler(RPCID, this);
        _node.FinishInit();
      }
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
        ProtocolLog.WriteIf(SocialLog.SVPNLog,
          String.Format("SVPN Method Call {0}", method));
      }

      if (_node.LocalUser == null && (method == String.Empty ||
           method != "setuid")) {
        if(method != "login") {
          throw new Exception("Uid not set");
        }
      }

      if(request.ContainsKey("a") && !request["a"].StartsWith("brunet")) {
        request["a"] = "brunet:node:" + request["a"];
      }
      switch(method) {
        case "btadd":
          _node.ConnectTo(request["b"]);
          break;

        case "add":
          AddToPending(request["a"]);
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

        case "del":
          _node.RemoveFriend(request["a"]);
          break;

        case "login":
          Login(request["n"], request["u"], request["p"]);
          break;

        case "logout":
          Logout(request["n"]);
          break;

        case "setuid":
          _node.SetUid(request["u"], request["p"]);
          break;

        case "shutdown":
          _node.Close();
          break;

        case "autofriend":
          _auto_allow = (_auto_allow ^ true);
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

        case "nat":
          request["response"] = _node.GetNatType();
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

          case "GetStats":
            result = _ssm.GetStats();
            break;

          case "GetNat":
            result = _node.GetNatType();
            break;

          case "ping":
            result = "pong";
            break;

          default:
            result = new InvalidOperationException("Invalid Method");
            break;
        }
      } catch (Exception e) {
        result = e;
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

      DateTime sent = DateTime.Now;

#if !SVPN_NUNIT
      string meth_call = RPCID + "." + method;
      Channel q = new Channel();
      q.CloseAfterEnqueue();
      q.CloseEvent += delegate(object obj, EventArgs eargs) {
        try {
          RpcResult res = (RpcResult) q.Dequeue();
          string result = (string) res.Result;

          if(method == "Ping") {
            DateTime recv = DateTime.Now;
            _times = _times.InsertIntoNew(result, recv);
            TimeSpan rtt = recv - sent;
            _ssm.UpdateLatency(result, rtt.TotalMilliseconds);
          }

          ProtocolLog.WriteIf(SocialLog.SVPNLog,
            String.Format("RPC {0} {1} {2}", addr.ToString(), method, 
            query));

        } catch(Exception e) {
          ProtocolLog.WriteIf(SocialLog.SVPNLog, e.ToString());
        }
      };

      ISender sender;
      if(!secure) {
        sender = new AHExactSender(_node.Node, addr);
      }
      else {
        sender = _node.Bso.GetSecureSender(addr);
      }
      _rpc.Invoke(sender, q, meth_call, _node.Address, query);
#endif
    }

    public void Login(string name, string uid, string password) {
      _node.SetUid(uid);
      _networks[name].SetData(_node.Address, _node.LocalUser.Fingerprint);
      _networks[name].Login(uid, password);
      GetPending();
    }

    public void Logout(string name) {
      _networks[name].Logout();
    }

    protected void AddFriend(string address, string cert) {
      SocialUser user = _node.AddFriend(address, cert, null, null);

      if(_pending.Contains(user.Address)) {
        _pending = _pending.RemoveFromNew(user.Address);
      }

      if(!_auto_allow && !IsVerified(user)) {
        _node.Block(user.Address);
      }
      GetState(true);
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

    protected void Block(string address) {
      _node.Block(address);
      _blocked = _blocked.PushIntoNew(address);
    }

    protected void Unblock(string address) {
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

    protected void VerifyFriends() {
      foreach(SocialUser user in _node.Friends.Values) {

        if(_pending.Contains(user.Address)) {
          _pending = _pending.RemoveFromNew(user.Address);
        }

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

    public void AddToPending(string address) {
      if(!_node.Friends.ContainsKey(address) && address != _node.Address &&
        !_pending.Contains(address)) {
        _pending = _pending.PushIntoNew(address);
        SendCertRequest(address);
      }
    }

    protected void GetPending() {
      foreach (ISocialNetwork network in _networks.Values) {
        foreach(KeyValuePair<string, string> kvp in network.Addresses) {
          AddToPending(kvp.Key);
        }
      }
      foreach(string address in _pending) {
        SendCertRequest(address);
      }
    }

    protected string HandlePing(string address, string msg) {
      _times = _times.InsertIntoNew(address, DateTime.Now);

      if(_node.IsAllowed(address)) {
        return _node.Address;
      }
      else {
        return String.Empty;
      }
    }

    protected void SendCertRequest(string address) {
      string request = _node.LocalUser.Certificate;
      SendRpcMessage(address, "AddCertRequest", request, false);
    }

    protected string AddCertRequest(string address, string msg) {
      string reply = _node.LocalUser.Certificate;
      SendRpcMessage(address, "AddCertReply", reply, false);
      AddFriend(address, msg);
      return _node.Address;
    }

    protected string AddCertReply(string address, string msg) {
      AddFriend(address, msg);
      return _node.Address;
    }

    public string GetState(bool write) {
#if SVPN_NUNIT
      return String.Empty;
#else
      FileState fstate = new FileState();
      SocialState state = new SocialState();

      if(_node.LocalUser != null) {
        state.LocalUser = _node.LocalUser;
        fstate.Uid = state.LocalUser.Uid;
        fstate.PCID = state.LocalUser.PCID;
      }

      state.Friends = new SocialUser[_node.Friends.Values.Count];
      state.Networks = new NetworkState[_networks.Values.Count];
      state.Pending = new string[_pending.Count];
      fstate.Friends = new FriendState[_node.Friends.Values.Count];

      _pending.CopyTo(state.Pending, 0);

      int i = 0;
      foreach (KeyValuePair<string, ISocialNetwork> kvp 
        in _networks) {
        state.Networks[i] = new NetworkState();
        state.Networks[i].Name = kvp.Key;
        state.Networks[i].Message = kvp.Value.Message;
        i++;
      }

      i = 0;
      foreach(SocialUser user in _node.Friends.Values) {
        string status;

        if(_node.IsAllowed(user.Address)) {
          if(IsOffline(user.Address)) {
            status = StatusTypes.Offline.ToString();
          }
          else {
            status = StatusTypes.Online.ToString();
          }
        }
        else {
          status = StatusTypes.Blocked.ToString();
        }
        state.Friends[i] = new SocialUser(user.Certificate, user.IP, 
          status);

        FriendState friend = new FriendState();
        friend.Certificate = user.Certificate;
        friend.IP = user.IP;
        friend.Status = status;
        fstate.Friends[i] = friend;
        i++;
      }

      if(write) {
        try {
          Utils.WriteConfig(STATEPATH, fstate);
        } catch {}
      }

      return SocialUtils.ObjectToXml<SocialState>(state);
#endif
    }

    public void LoadState() {
#if !SVPN_NUNIT
      try {
        FileState fstate = Utils.ReadConfig<FileState>(STATEPATH);

        if(fstate.Uid != null) {
          _node.SetUid(fstate.Uid, fstate.PCID);
        }

        foreach (FriendState friend in fstate.Friends) {
          SocialUser user = new SocialUser(friend.Certificate, friend.IP, 
            friend.Status);
          _node.AddFriend(user.Address, user.Certificate, user.Uid, user.IP);
          if(user.Status == StatusTypes.Blocked.ToString() && !_auto_allow) {
            _node.Block(user.Address);
          }
        }
      }
      catch {
      }
#endif
    }
  }

#if SVPN_NUNIT
  [TestFixture]
  public class SocialConnectionManagerTester {
    [Test]
    public void SocialConnectionManagerTest() {
    }
  } 
#endif
}
