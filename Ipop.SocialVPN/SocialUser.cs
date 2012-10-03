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
using Mono.Security.X509;

using Brunet.Security;

#if SVPN_NUNIT
using NUnit.Framework;
#endif

namespace Ipop.SocialVPN {

  public class SocialUser {

    public const string PICPREFIX = "http://www.gravatar.com/avatar/";

    public const string PICSUFFIX = "?d=mm";

    private readonly Certificate _cert;

    private readonly string _fingerprint;

    private readonly string _pic;

    private readonly string _certificate;

    private readonly string _ip;

    private readonly string _status;

    public string Uid {
      get { return _cert.Subject.Email.ToLower(); }
      set {}
    }

    public string Name {
      get { return _cert.Subject.Name; }
      set {}
    }

    public string PCID {
      get { return _cert.Subject.OrganizationalUnit; }
      set {}
    }

    public string Country {
      get { return _cert.Subject.Country; }
      set {}
    }

    public string Version {
      get { return _cert.Subject.Organization; }
      set {}
    }

    public string Address {
      get { return _cert.NodeAddress; }
      set {}
    }

    public string Pic {
      get { return _pic; }
      set {}
    }

    public string Fingerprint {
      get { return _fingerprint; }
      set {}
    }

    public string IP {
      get { return _ip;}
      set {}
    }

    public string Status {
      get { return _status;}
      set {}
    }

    public string Certificate {
      get { return _certificate; }
      set {}
    }

    public X509Certificate X509 {
      get { return _cert.X509; }
    }

    public SocialUser() {}

    public SocialUser(string certificate, string ip, string status) : this() {
      byte[] certBytes = Convert.FromBase64String(certificate);
      _certificate = certificate;
      _cert = new Certificate(certBytes);
      _fingerprint = SocialUtils.GetSHA1HashString(certBytes);
      _pic = PICPREFIX + SocialUtils.GetMD5HashString(Uid) + PICSUFFIX;
      _ip = ip;
      _status = status;
    }
  }

#if SVPN_NUNIT
  [TestFixture]
  public class SocialUserTester {
    [Test]
    public void SocialUserTest() {
    }
  } 
#endif

}
