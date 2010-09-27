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
using System.Text.RegularExpressions;

using Brunet;
using Brunet.Applications;
using Brunet.Util;
using Brunet.Collections;
using Brunet.Messaging;
using Brunet.Symphony;
using Brunet.Concurrent;
using Ipop.Managed;

#if SVPN_NUNIT
using NUnit.Framework;
using NMock2;
#endif

namespace Ipop.SocialVPN {

  public class SocialDnsManager : IDnsResolver {

    protected const string STATEPATH = "sdnsstate.xml";

    protected ImmutableDictionary<string, DnsMapping> _mappings;

    protected ImmutableDictionary<string, DnsMapping> _tmappings;

    protected readonly WriteOnce<IRpcSender> _sender;

    public IDictionary<string, DnsMapping> Mappings {
      get { return _mappings; }
    }

    public IDictionary<string, DnsMapping> Tmappings {
      get { return _tmappings; }
    }

    public IRpcSender Sender {
      get { return _sender.Value; }
      set { _sender.Value = value; }
    }

#if SVPN_NUNIT
    protected readonly ISocialNode _node;

    public SocialDnsManager(ISocialNode node) {
#else
    protected readonly SocialNode _node;

    public SocialDnsManager(SocialNode node) {
#endif
      _node = node;
      _mappings = ImmutableDictionary<string, DnsMapping>.Empty;
      _tmappings = ImmutableDictionary<string, DnsMapping>.Empty;
      _sender = new WriteOnce<IRpcSender>();
      LoadState();
      AddDnsMapping(_node.LocalUser.PCID, _node.LocalUser.IP);
    }

    protected string GetAddress(string ip) {
#if SVPN_NUNIT
      return "address" + ip;
#else
      if(ip == _node.LocalUser.IP) {
        return _node.LocalUser.Address;
      }

      foreach(SocialUser friend in _node.Friends.Values) {
        if(friend.IP == ip) {
          return friend.Address;
        }
      }

      throw new Exception("Invalid IP address");
#endif
    }

    protected string GetIP(string address) {
#if SVPN_NUNIT
      return address;
#else
      if(address == _node.LocalUser.Address) {
        return _node.LocalUser.IP;
      }

      SocialUser friend;
      if(_node.Friends.TryGetValue(address, out friend)) {
        return friend.IP;
      }
      else {
        throw new Exception("Invalid Address");
      }
#endif
    }

    protected string GetUid(string address) {
#if SVPN_NUNIT
      return address;
#else
      if(address == _node.LocalUser.Address) {
        return _node.LocalUser.Uid;
      }

      SocialUser friend;
      if(_node.Friends.TryGetValue(address, out friend)) {
        return friend.Uid;
      }
      else {
        throw new Exception("Invalid Address");
      }
#endif
    }

    public DnsMapping AddDnsMapping(string alias, string ip) {
      return AddDnsMapping(alias, ip, _node.LocalUser.Uid);
    }

    public DnsMapping AddDnsMapping(string alias, string ip, string source) {
      string address = GetAddress(ip);
      DnsMapping mapping = new DnsMapping(alias, address, ip, source);
      _mappings = _mappings.InsertIntoNew(mapping.Alias, mapping);
      return mapping;
    }

    public void DeleteDnsMapping(string alias) {
      ImmutableDictionary<string, DnsMapping> old;
      _mappings = _mappings.RemoveFromNew(alias, out old);
    }

    public void SearchFriends(string query, IRpcSender sender) {
      foreach(SocialUser friend in _node.Friends.Values) {
        if(_node.IsAllowed(friend.Address)) {
          sender.SendRpcMessage(friend.Address, "SearchMapping", query);
        }
      }
    }

    public string SearchMapping(string address, string query, 
      IRpcSender sender) {
      foreach(string alias in _mappings.Keys) {
        if(Regex.IsMatch(alias, query, RegexOptions.IgnoreCase)) {
          DnsMapping mapping = _mappings[alias];
          sender.SendRpcMessage(address, "AddTmpMapping", mapping.ToString());
        }
      }
      return _node.LocalUser.Address;
    }

    public string AddTmpMapping(string address, string smapping) {
      string[] parts = smapping.Split(DnsMapping.DELIM);
      string ip = GetIP(parts[1]);
      string source = GetUid(address);
      DnsMapping mapping = new DnsMapping(parts[0], parts[1], ip, source);
      return AddTmpMapping(mapping);
    }

    public string AddTmpMapping(DnsMapping mapping) {
      string id = mapping.ToIDString();
      if(!_tmappings.ContainsKey(id)) {
        _tmappings = _tmappings.InsertIntoNew(id, mapping);
      }
      return _node.LocalUser.Address;
    }

    public void ClearResults() {
      _tmappings.Clear();
    }

    public List<DnsMapping> SearchLocalCache(string pattern, bool exact, 
      bool random) {
      List<DnsMapping> searchlist = new List<DnsMapping>();

      if(pattern == "") {
        return searchlist;
      }

      foreach(DnsMapping mapping in _tmappings.Values) {
        if(Regex.IsMatch(mapping.Alias, pattern, RegexOptions.IgnoreCase)) {
          bool mapping_found = false;
          foreach(DnsMapping tmp_mapping in searchlist) {
            if(tmp_mapping.WeakEquals(mapping)) {
              tmp_mapping.Rating++;
              tmp_mapping.AddResponder(mapping.Source);
              mapping_found = true;
              break;
            }
          }
          if(exact && pattern != mapping.Alias) {
            continue;
          }
          if(!mapping_found) {
            DnsMapping new_mapping = mapping.WeakCopy();
            new_mapping.AddResponder(mapping.Source);
            searchlist.Add(new_mapping);
          }
        }
      }
      if(random) {
        searchlist.Sort(new RandomMappingComparer());
      }
      else {
        searchlist.Sort(new MappingComparer());
      }
      return searchlist;
    }

    public string DnsResolve(string name) {
      DnsMapping mapping;
      if(_mappings.TryGetValue(name, out mapping)) {
        return mapping.IP;
      }

      SearchFriends(name, Sender);
      System.Threading.Thread.Sleep(100);

      string ip = null;
      List<DnsMapping> list = SearchLocalCache(name, true, true);
      if(list.Count > 0) {
        ip = list[0].IP;
      }
      return ip;
    }

    public string GetState(string query) {
      return GetState(SearchLocalCache(query, false, false), true);
    }

    protected string GetState(List<DnsMapping> tmappings, bool write) {
      DnsState state = new DnsState();
      state.Mappings = new DnsMapping[_mappings.Count];
      _mappings.Values.CopyTo(state.Mappings, 0);
      Array.Sort(state.Mappings, new MappingComparer());
      state.TmpMappings = tmappings.ToArray();

      if(write) {
        Utils.WriteConfig(STATEPATH, state);
      }

      return SocialUtils.ObjectToXml<DnsState>(state);
    }

    public void LoadState() {
#if !SVPN_NUNIT
      try {
        DnsState state = Utils.ReadConfig<DnsState>(STATEPATH);
        foreach (DnsMapping mapping in state.Mappings) {
          AddDnsMapping(mapping.Alias, mapping.IP);
        }
      }
      catch {}
#endif
    }
  }

  public class DnsMapping {

    public const char DELIM = '_';

    public string Alias;
    public string Address;
    public string IP;
    public string Source;
    public int Rating;
    public List<string> Responders;

    public DnsMapping() {}

    public DnsMapping(string alias, string address, string ip, string source) {
      Alias = CheckAlias(alias);
      Address = address;
      IP = ip;
      Source = source;
      Rating = 1;
    }

    public static string CheckAlias(string alias) {
      string result = alias;
      if(!alias.EndsWith("." + SocialNode.DNSSUFFIX)) {
        result = alias + "." + SocialNode.DNSSUFFIX;
      }
      return result;
    }

    public void AddResponder(string source) {
      if(Responders == null) {
        Responders = new List<string>();
      }
      if(!Responders.Contains(source)) {
        Responders.Add(source);
      }
    }

    public DnsMapping WeakCopy() {
      return new DnsMapping(Alias, Address, IP, null);
    }

    public bool WeakEquals(DnsMapping mapping) {
      return (mapping.Alias == Alias && mapping.Address == Address);
    }

    public bool Equals(DnsMapping mapping) {
      return (mapping.Alias == Alias && mapping.Address == Address
        && mapping.Source == Source);
    }

    public override string ToString() {
      return Alias + DELIM + Address + DELIM + Rating;
    }

    public string ToIDString() {
      return Alias + DELIM + Address + DELIM + Source;
    }
  }

  public class DnsState {
    public DnsMapping[] Mappings;
    public DnsMapping[] TmpMappings;
  }

  public class MappingComparer : IComparer<DnsMapping> {
    public int Compare(DnsMapping x, DnsMapping y) {
      int val = y.Rating - x.Rating;
      if(val == 0) {
        return String.Compare(x.Alias, y.Alias);
      }
      else {
        return val;
      }
    }
  }

  public class RandomMappingComparer : IComparer<DnsMapping> {
    private readonly Random rand;

    public RandomMappingComparer() {
      rand = new Random();
    }

    public RandomMappingComparer(int seed) {
      rand = new Random(seed);
    }

    public int Compare(DnsMapping x, DnsMapping y) {
      int val = y.Rating - x.Rating;
      if(val == 0) {
        double sample = rand.NextDouble();
        if(sample < 0.5) {
          return -1;
        }
        else {
          return 1;
        }
      }
      else {
        return val;
      }
    }
  }

#if SVPN_NUNIT
  [TestFixture]
  public class SocialDnsManagerTester {

    [Test]
    public void DnsMappingTest() {
      DnsMapping mapping = new DnsMapping("pierre.sdns", "brunet123",
        "172.31.0.2", "ptony82@ufl.edu");

      mapping.AddResponder("ptony82@gmail.com");
      mapping.AddResponder("ptony82@yahoo.com");
      mapping.AddResponder("ptony82@gmail.com");

      Assert.AreEqual(2, mapping.Responders.Count);
      Assert.AreEqual("ptony82@gmail.com", mapping.Responders[0]);
      Assert.AreEqual("ptony82@yahoo.com", mapping.Responders[1]);

    }

    [Test]
    public void SocialDnsManagerTest() {
      byte[] certData = SocialUtils.ReadFileBytes("local.cert");
      string certb64 = Convert.ToBase64String(certData);
      SocialUser user = new SocialUser(certb64);

      ImmutableDictionary<string, SocialUser> friends =
        ImmutableDictionary<string, SocialUser>.Empty;

      Mockery mocks = new Mockery();
      ISocialNode node = mocks.NewMock<ISocialNode>();
      IRpcSender sender = mocks.NewMock<IRpcSender>();

      Stub.On(node).GetProperty("Friends").Will(
        Return.Value(friends));

      Stub.On(node).GetProperty("LocalUser").Will(
        Return.Value(user));

      Stub.On(node).Method("IsAllowed").Will(
       Return.Value(true));

      Stub.On(sender).Method("SendRpcMessage");

      SocialDnsManager sdm = new SocialDnsManager(node);
      sdm.Sender = sender;

      sdm.AddDnsMapping("pierre", "172.31.21.1");

      Assert.AreEqual(sdm.Mappings["pierre.sdns"].Address, 
        "address172.31.21.1");

      sdm.SearchMapping("address", "pierre", sender);

      DnsMapping mapping = new DnsMapping("pierre", "brunet123", "ip", 
        "source");
      DnsMapping mapping1 = new DnsMapping("pierre", "brunet124", "ip", 
        "source2");
      DnsMapping mapping2 = new DnsMapping("pierre", "brunet124", "ip", 
        "source3");

      sdm.AddTmpMapping("address", mapping.ToString());
      sdm.AddTmpMapping("address1", mapping1.ToString());
      sdm.AddTmpMapping("address2", mapping2.ToString());

      Assert.AreEqual(3, sdm.Tmappings.Values.Count);

      List<DnsMapping> list = sdm.SearchLocalCache("pierre", false, false);
      Assert.AreEqual(list[0].Address, "brunet124");
      Assert.AreEqual(list[0].Rating, 2);
      Assert.AreEqual(list[1].Rating, 1);
      Assert.AreEqual(list.Count, 2);

      List<DnsMapping> list1 = sdm.SearchLocalCache("pierre.sdns", true, false);
      Assert.AreEqual(list1.Count, 2);
      Assert.AreEqual(list1[0].Rating, 2);

      Console.WriteLine(sdm.GetState(""));
    }
  } 
#endif
}
