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
using System.Text;

using Brunet.Collections;

#if SVPN_NUNIT
using NUnit.Framework;
using NMock2;
#endif

namespace Ipop.SocialVPN {

  public class SocialStatsManager {

    public const char DELIM = ' ';

    public const double _exp_factor = 0.98;

    protected readonly SocialNode _node;

    protected ImmutableDictionary<string, double> _latencies;

    public SocialStatsManager(SocialNode node) {
      _node = node;
      _latencies = ImmutableDictionary<string, double>.Empty;
    }

    public void UpdateLatency(string address, double rtt) {
      double moving_rtt;
      if(_latencies.TryGetValue(address, out moving_rtt)) {
        double new_rtt = _exp_factor * (moving_rtt - rtt) + rtt;
        _latencies = _latencies.InsertIntoNew(address, new_rtt);
      }
      else {
        _latencies = _latencies.InsertIntoNew(address, rtt);
      }
    }

    public string GetStats() {
      StringBuilder result = new StringBuilder();
      foreach(SocialUser friend in _node.Friends.Values) {
        double latency;
        if(_latencies.TryGetValue(friend.Address, out latency)) {
          int lat = (int) latency;
          string stat = friend.Address.Substring(12, 4) + DELIM + lat +
            DELIM + _node.GetStats(friend.Address) + "\n";
          result.Append(stat);
        }
      }
      return result.ToString();
    }

  }

#if SVPN_NUNIT
  [TestFixture]
  public class SocialStatsManagerTester {

    [Test]
    public void StatsTest() {
    }
  } 
#endif
}
