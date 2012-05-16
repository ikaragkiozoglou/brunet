
using System;
using System.Text;
using System.Threading;
using System.Diagnostics;

using Brunet;
using Brunet.Util;

namespace Ipop.SocialVPN {

  public class SslProxy {

    protected readonly SocialNode _node;
    protected SocialConnectionManager _scm;
    protected readonly Thread _thread;
    protected readonly byte[] _buf;
    protected readonly byte[] _eth_hdr;
    protected Process _ssl;

    public SslProxy(SocialNode node) {
      _node = node;
      _buf = new byte[2048];
      _eth_hdr = new byte[14];
      _eth_hdr[12] = 8;
      _eth_hdr[13] = 0;
      _thread = new Thread(this.Listen);
      _thread.Start();
      _ssl = new Process();
      _ssl.StartInfo.UseShellExecute = false;
      _ssl.StartInfo.RedirectStandardOutput = true;
      _ssl.StartInfo.RedirectStandardInput = true;
      _ssl.StartInfo.FileName = "ssl_proxy";
    }

    public void SetSCM(SocialConnectionManager scm) {
      _scm = scm;
    }

    public void Write(byte[] packet, int len, string source) {
      source = source.Substring(12);
      byte[] addr = Encoding.ASCII.GetBytes(source);
      Array.Copy(addr, 0, _buf, 40, 32);
      Array.Copy(_eth_hdr, 0, _buf, 80, 14);
      Array.Copy(packet, 0, _buf, 94, len);
      _ssl.StandardInput.BaseStream.Write(_buf, 0, len + 94);
      _ssl.StandardInput.BaseStream.Flush();
    }

    public void Listen() {
      byte[] packet = new byte[2048];
      try {
        _ssl.Start();
        Console.WriteLine("ssl started ");
        while (true) {
          int len = _ssl.StandardOutput.BaseStream.Read(packet, 0, 2048);
          string dest = Encoding.ASCII.GetString(packet, 40, 32);
          if (dest.Length == 32 && len > 80) {
            dest = "brunet:node:" + dest;
            Console.WriteLine(dest + " size " + len);
            _scm.AddToPending(dest);
            _node.SendIP(MemBlock.Reference(packet, 94, len-94),
              AddressParser.Parse(dest));
          }
          else {
            Console.WriteLine("bad packet " + len);
            break;
          }
        }
      } catch (Exception e) { Console.WriteLine(e);}
    }

  }

}

