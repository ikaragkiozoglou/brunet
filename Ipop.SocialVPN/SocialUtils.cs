/*
Copyright (C) 2009 Pierre St Juste <ptony82@ufl.edu>, University of Florida
                   David Wolinsky <davidiw@ufl.edu>, University of Florida

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
using System.IO;
using System.Net.Security;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using System.Web;
using System.Net;

using Brunet.Util;
using Brunet.Applications;

#if SVPN_NUNIT
using NUnit.Framework;
#endif

namespace Ipop.SocialVPN {

  public static class SocialUtils {

    public static SocialConfig CreateConfig() {
      SocialConfig social_config = new SocialConfig();
      social_config.BrunetConfig = "brunet.config";
      social_config.IpopConfig = "ipop.config";
      social_config.HttpPort = "58888";
      social_config.JabberPort = "5222";
      social_config.JabberID = "user@example.com";
      social_config.JabberPass = "password";
      social_config.AutoLogin = false;
      social_config.GlobalBlock = true;
      social_config.AutoFriend = false;
      social_config.AutoDns = true;
      Utils.WriteConfig(SocialNode.CONFIGPATH, social_config);
      return social_config;
    }

    public static string GetSHA1HashString(string input) {
      byte[] data = Encoding.Default.GetBytes(input.ToLower());
      return GetSHA1HashString(data);
    }

    public static string GetSHA1HashString(byte[] data) {
      SHA1CryptoServiceProvider sha1 = new SHA1CryptoServiceProvider();
      string hash = BitConverter.ToString(sha1.ComputeHash(data));
      hash = hash.Replace("-", "");
      return hash.ToLower();
    }

    public static string GetMD5HashString(string input) {
      byte[] data = Encoding.Default.GetBytes(input.ToLower());
      return GetMD5HashString(data);
    }

    public static string GetMD5HashString(byte[] data) {
      MD5CryptoServiceProvider sha1 = new MD5CryptoServiceProvider();
      string hash = BitConverter.ToString(sha1.ComputeHash(data));
      hash = hash.Replace("-", "");
      return hash.ToLower();
    }

    public static byte[] ReadFileBytes(string path) {
      FileStream file = File.Open(path, FileMode.Open);
      byte[] blob = new byte[file.Length];
      file.Read(blob, 0, (int)file.Length);
      file.Close();
      return blob;
    }

    public static void WriteToFile(byte[] data, string path) {
      FileStream file = File.Open(path, FileMode.Create);
      file.Write(data, 0, data.Length);
      file.Close();
    }

    public static T XmlToObject1<T>(string val) {
      XmlSerializer serializer = new XmlSerializer(typeof(T));
      T res = default(T);
      using (StringReader sr = new StringReader(val)) {
        res = (T)serializer.Deserialize(sr);
      }
      return res;
    }

    public static string ObjectToXml1<T>(T val) {
      using (StringWriter sw = new StringWriter()) {
        XmlSerializer serializer = new XmlSerializer(typeof(T));
        serializer.Serialize(sw, val);
        return sw.ToString();
      }
    }

    // taken from online http://www.dotnetjohn.com/articles.aspx?articleid=173
    public static string ObjectToXml<T>(T val) {
      try {
        String XmlizedString = null;
        MemoryStream memoryStream = new MemoryStream();
        XmlSerializer xs = new XmlSerializer(typeof(T));
        XmlTextWriter xmlTextWriter = new XmlTextWriter(memoryStream, 
                                                        Encoding.UTF8);
        xs.Serialize(xmlTextWriter, val);
        memoryStream = (MemoryStream) xmlTextWriter.BaseStream;
        XmlizedString = UTF8ByteArrayToString(memoryStream.ToArray());
        return XmlizedString;
      } catch ( Exception e ) {
        System.Console.WriteLine(e);
        return null;
      }
    }

    // taken from online http://www.dotnetjohn.com/articles.aspx?articleid=173
    public static T XmlToObject<T>(string val) {
      XmlSerializer xs = new XmlSerializer(typeof(T));
      MemoryStream memoryStream = new MemoryStream(StringToUTF8ByteArray(val));
      return (T) xs.Deserialize(memoryStream);
    }

    public static string UTF8ByteArrayToString(Byte[] characters) {
      UTF8Encoding encoding = new UTF8Encoding();
      String constructedString = encoding.GetString(characters);
      return constructedString;
    }

    public static Byte[] StringToUTF8ByteArray(String pXmlString) {
      UTF8Encoding encoding = new UTF8Encoding ( );
      Byte[ ] byteArray = encoding.GetBytes ( pXmlString );
      return byteArray;
    }

    public static string UrlEncode(Dictionary<string, string> parameters) {
      StringBuilder sb = new StringBuilder();
      int count = 0;
      foreach (KeyValuePair<string, string> de in parameters) {
        count++;
        sb.Append(HttpUtility.UrlEncode(de.Key));
        sb.Append('=');
        sb.Append(HttpUtility.UrlEncode(de.Value));
        if (count < parameters.Count){
          sb.Append('&');
        }
      }

      return sb.ToString();
    }

    public static Dictionary<string, string> DecodeUrl(string request) {
      Dictionary<string, string> result = new Dictionary<string, string>();
      string[] pairs = request.Split('&');

      for (int x = 0; x < pairs.Length; x++) {
        string[] item = pairs[x].Split('=');
        if(item.Length > 1 ) {
          result.Add(HttpUtility.UrlDecode(item[0]), 
                     HttpUtility.UrlDecode(item[1]));
        }
        
      }
      return result;
    }

    public static string Request(string url) {
      return Request(url, (byte[])null);
    }

    public static string Request(string url, Dictionary<string, string> 
                                 parameters) {
      ProtocolLog.WriteIf(SocialLog.SVPNLog,
                          String.Format("HTTP REQUEST: {0} {1} {2}",
                          DateTime.Now.TimeOfDay, url, parameters["m"]));
      return Request(url, Encoding.ASCII.GetBytes(UrlEncode(parameters)));
    }

    public static string Request(string url, byte[] parameters) {
      HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(url);
      webRequest.ContentType = "application/x-www-form-urlencoded";

      if (parameters != null) {
        webRequest.Method = "POST";
        webRequest.ContentLength = parameters.Length;

        using (Stream buffer = webRequest.GetRequestStream()) {
          buffer.Write(parameters, 0, parameters.Length);
          buffer.Close();
        }
      }
      else {
        webRequest.Method = "GET";
      }

      WebResponse webResponse = webRequest.GetResponse();
      using (StreamReader streamReader = 
        new StreamReader(webResponse.GetResponseStream())) {
        return streamReader.ReadToEnd();
      }
    }
  }

#if SVPN_NUNIT
  [TestFixture]
  public class SocialUtilsTester {
    [Test]
    public void SocialUtilsTest() {
      Assert.AreEqual("test", "test");
    }
  } 
#endif
}
