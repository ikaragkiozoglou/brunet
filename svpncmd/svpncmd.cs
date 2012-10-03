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
using System.Text;

using Brunet;
using Brunet.Applications;

namespace Ipop.SocialVPN {

  /**
   * SocialNode Class. Extends the RpcIpopNode to support adding friends based
   * on X509 certificates.
   */
  public class Svpncmd {

    public static string _url = null;

    public static void SetUrl() {
      if(_url == null && System.IO.File.Exists("social.config")) {
        SocialConfig config = Utils.ReadConfig<SocialConfig>("social.config");
        _url = "http://127.0.0.1:" + config.HttpPort + "/state.xml";
      }
    }

    public static string Add(string address) {
      Dictionary<string, string> parameters = 
        new Dictionary<string, string>();

      parameters["m"] = "add";
      parameters["a"] = address;
      return Print(SocialUtils.Request(_url, parameters));
    }

    public static string Remove(string address) {
      Dictionary<string, string> parameters = 
        new Dictionary<string, string>();

      parameters["m"] = "del";
      parameters["a"] = address;
      return Print(SocialUtils.Request(_url, parameters));
    }

    public static string Block(string address) {
      Dictionary<string, string> parameters = 
        new Dictionary<string, string>();

      parameters["m"] = "block";
      parameters["a"] = address;
      return Print(SocialUtils.Request(_url, parameters));
    }

    public static string Unblock(string address) {
      Dictionary<string, string> parameters = 
        new Dictionary<string, string>();

      parameters["m"] = "unblock";
      parameters["a"] = address;
      return Print(SocialUtils.Request(_url, parameters));
    }

    public static string Login(string network, string user, string pass) {
      Dictionary<string, string> parameters = 
        new Dictionary<string, string>();

      parameters["m"] = "login";
      parameters["n"] = network;
      parameters["u"] = user;
      parameters["p"] = pass;
      return Print(SocialUtils.Request(_url, parameters));
    }

    public static string Logout(string network) {
      Dictionary<string, string> parameters = 
        new Dictionary<string, string>();

      parameters["m"] = "logout";
      parameters["n"] = network;
      return Print(SocialUtils.Request(_url, parameters));
    }

    public static string GetInfo() {
      Dictionary<string, string> parameters = 
        new Dictionary<string, string>();

      parameters["m"] = "getstate";
      return Print(SocialUtils.Request(_url, parameters));
    }

    public static string Print(string output) {
      Console.WriteLine(output);
      return output;
    }

    public static void ShowHelp() {
      string help = "usage: svpncmd.exe <option> <fingerprint>\n\n" +
                    "options:\n" +
                    "  login <network> <user> <pass> - log in user\n" +
                    "  logout <network> - log out user\n" +
                    "  add <address> - add a friend\n" +
                    "  remove <address> - remove a friend\n" +
                    "  block <address> - block a friend\n" + 
                    "  unblock <address> - unblock a friend\n" + 
                    "  getstate - print current state in xml\n" + 
                    "  help - shows this help";
      Console.WriteLine(help);
    }

    /**
     * The main function, starting point for the program.
     */
    public static void Main(string[] args) {
      SetUrl();
      if(args.Length < 1) {
        ShowHelp();
        return;
      }
      switch (args[0]) {
        case "help":
          ShowHelp();
          break;

        case "login":
          Login(args[1], args[2], args[3]);
          break;

        case "logout":
          Logout(args[1]);
          break;

        case "add":
          Add(args[1]);
          break;

        case "remove":
          Remove(args[1]);
          break;

        case "block":
          Block(args[1]);
          break;

        case "unblock":
          Unblock(args[1]);
          break;

        case "getstate":
          GetInfo();
          break;

        default:
          ShowHelp();
          break;
      }
    }
  }
}
