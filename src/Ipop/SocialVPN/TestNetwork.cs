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
using System.Collections.Generic;
using System.Net;
using System.IO;

using Brunet.Collections;
using Brunet.Concurrent;

#if SVPN_NUNIT
using NUnit.Framework;
#endif

namespace Ipop.SocialVPN {

  public class TestNetwork : ISocialNetwork {

    public const string URL = "http://socialvpntest.appspot.com";

    protected ImmutableDictionary<string, string> _addresses;

    protected ImmutableDictionary<string, string> _fingerprints;

    protected string _message;

    protected string _address;

    protected string _fingerprint;

    public string Message { get { return _message; }}

    public IDictionary<string, string> Addresses {
      get { return _addresses; }
    }

    public IDictionary<string, string> Fingerprints {
      get { return _fingerprints; }
    }

    public TestNetwork() {
      _message = String.Empty;
      _address = String.Empty;
      _fingerprint = String.Empty;
      _addresses = ImmutableDictionary<string, string>.Empty;
      _fingerprints = ImmutableDictionary<string, string>.Empty;
    }

    public void SetData(string address, string fingerprint) {
      _address = address;
      _fingerprint = fingerprint;
    }

    protected void Publish(string uid) {
      string url = URL + "/publish?a=" + _address + "&f=" + _fingerprint +
        "&u=" + uid;
      SocialUtils.Request(url);
    }

    protected void Retreive() {
      string url = URL + "/";
      string result = SocialUtils.Request(url);

      string[] lines = result.Split('\n');
      foreach(string line in lines) {
        if(line.Length < 10) continue;
        string[] parts = line.Split(' ');
        _fingerprints = _fingerprints.InsertIntoNew(parts[0], parts[1]);
        _addresses = _addresses.InsertIntoNew(parts[0], parts[2]);
      }
    }

    public void Login(string uid, string password) {
      if(password == "publish") {
        Publish(uid);
      }
      Retreive();
    }

    public void Logout() {
    }

  }

#if SVPN_NUNIT
  [TestFixture]
  public class TestNetworkTester {
    [Test]
    public void TestNetworkTest() {

      TestNetwork network = new TestNetwork();
      network.SetData("address", "fingerprint");
      network.Login("test@server.sdns", "publish");
      foreach(KeyValuePair<string, string> kvp in network.Addresses) {
        Console.WriteLine(kvp.Key + " = " + kvp.Value);
      }
      foreach(KeyValuePair<string, string> kvp in network.Fingerprints) {
        Console.WriteLine(kvp.Key + " = " + kvp.Value);
      }
    }
  } 
#endif
}
