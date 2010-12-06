/*
Copyright (C) 2008 Pierre St Juste <ptony82@ufl.edu>, University of Florida

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
using System.Web;
using System.IO;
using System.Threading;

using Brunet.Util;

namespace Ipop.SocialVPN {

  public class HttpInterface {
    public event EventHandler ProcessEvent;

    protected readonly HttpListener _listener;
    
    protected readonly Thread _runner;

    protected readonly string _port;

    protected bool _running;

    public HttpInterface(string port) {
      _listener = new HttpListener();
      _listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
      _runner = new Thread(Run);
      _runner.IsBackground = true;
      _running = false;
      _port = port;
    }

    protected string Process(Dictionary<string, string> request) {
      EventHandler process_event = ProcessEvent;
      string response = String.Empty;
      if (process_event != null) {
        try {
          process_event(request, EventArgs.Empty);
          response = request["response"];
        } catch (Exception e) {
          response = SocialUtils.ObjectToXml<string>(e.Message);
        }
      }

      if(request.ContainsKey("html")) {
        response = "<html><head><meta HTTP-EQUIV=\"REFRESH\" " +
          "content=\"0;url=html\" /></head></html>";
      }

      return response;
    }

    public void Start() {
      if(_running) {
        return;
      }
      _running = true;
      _listener.Start();
      _runner.Start();
    }

    public void Stop() {
      _running = false;
      _listener.Stop();
      _runner.Join();
    }

    protected void Run() {
      while(_running) {
        HttpListenerContext context = null;

        try {
          context = _listener.GetContext();
        } catch(Exception e) {
          Console.WriteLine(e);
          ProtocolLog.WriteIf(SocialLog.SVPNLog,
            String.Format("SVPN Exception: {0}", e));
        }

        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        string responseString;
        response.ContentType = "text/xml";

        if (request.RawUrl.StartsWith("/state.xml?")) {
          string getData = request.RawUrl.Substring(11);
          try {
            responseString = Process(SocialUtils.DecodeUrl(getData));
          } catch (Exception e) {
            responseString = e.Message;
          }
        }
        else if (request.RawUrl == "/state.xml") {
          StreamReader reader = new StreamReader(request.InputStream,
                                                 request.ContentEncoding);

          string postData = reader.ReadToEnd();
          request.InputStream.Close();
          reader.Close();
          try {
            responseString = Process(SocialUtils.DecodeUrl(postData));
          } catch (Exception e) {
            responseString = e.Message;
          }
        }
        else if (request.RawUrl == "/socialvpn.js") {
          using (StreamReader text = new StreamReader("socialvpn.js")) {
            responseString = text.ReadToEnd();
          }
          response.ContentType = "text/javascript";
        }
        else if (request.RawUrl == "/socialvpn.css") {
          using (StreamReader text = new StreamReader("socialvpn.css")) {
            responseString = text.ReadToEnd();
          }
          response.ContentType = "text/css";
        }
        else if (request.RawUrl == "/socialdns.js") {
          using (StreamReader text = new StreamReader("socialdns.js")) {
            responseString = text.ReadToEnd();
          }
          response.ContentType = "text/javascript";
        }
        else if (request.RawUrl == "/socialdns.css") {
          using (StreamReader text = new StreamReader("socialdns.css")) {
            responseString = text.ReadToEnd();
          }
          response.ContentType = "text/css";
        }
        else if (request.RawUrl == "/jquery-ui.css") {
          using (StreamReader text = new StreamReader("jquery-ui.css")) {
            responseString = text.ReadToEnd();
          }
          response.ContentType = "text/css";
        }
        else if (request.RawUrl == "/jquery.js") {
          using (StreamReader text = new StreamReader("jquery.js")) {
            responseString = text.ReadToEnd();
          }
          response.ContentType = "text/javascript";
        }
        else if (request.RawUrl == "/jquery-ui.js") {
          using (StreamReader text = new StreamReader("jquery-ui.js")) {
            responseString = text.ReadToEnd();
          }
          response.ContentType = "text/javascript";
        }
        else if (request.RawUrl == "/sdns") {
          using (StreamReader text = new StreamReader("socialdns.html")) {
            responseString = text.ReadToEnd();
          }
          response.ContentType = "text/html";
        }
        else {
          using (StreamReader text = new StreamReader("socialvpn.html")) {
            responseString = text.ReadToEnd();
          }
          response.ContentType = "text/html";
        }

        if(responseString.StartsWith("<html>")) {
          response.ContentType = "text.html";
        }

        byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
        response.ContentLength64 = buffer.Length;
        response.AddHeader("Cache-Control", "No-cache");
        System.IO.Stream output = response.OutputStream;
        try {
          output.Write(buffer, 0, buffer.Length);
          output.Close();
        } catch (Exception e) {
          Console.WriteLine(e);
          ProtocolLog.WriteIf(SocialLog.SVPNLog,
            String.Format("SVPN Exception: {0}", e));
        }
      }
    }
  }
}
