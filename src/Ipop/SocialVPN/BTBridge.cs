
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Ipop.SocialVPN {

public class BTBridge {

    protected const int MAX_SEND = 2;

    protected readonly SocialNode _node;
    protected readonly Socket _sock;
    protected readonly IPEndPoint _server_ep;
    protected EndPoint _rem_ep;
    protected int  _scount;
    protected readonly Thread _thread;

    public BTBridge(SocialNode node) {
      _node = node;
      _server_ep = new IPEndPoint(IPAddress.Any, 51234);
      _rem_ep = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 50000);

      _sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 
                         ProtocolType.Udp);
      _sock.Bind(_server_ep);
      _scount = MAX_SEND;
      _thread = new Thread(this.Listen);
      _thread.Start();
    }

    public void ConnectTo(string bt_addr) {
      byte[] msg = Encoding.ASCII.GetBytes(bt_addr);
      _sock.SendTo(msg, _rem_ep);
      _scount = 0;
      Console.WriteLine("sent connecto " + bt_addr);
    }

    public bool Send(byte[] data) {
      if (_scount < MAX_SEND) {
        _scount++;
        _sock.SendTo(data, _rem_ep);
        Console.WriteLine("sent packet over BT size" + data.Length);
        return true;
      }
      Console.WriteLine("sent packet over WiFi size " + data.Length);
      return false;
    }

    public void Listen() {
      int count;
      byte[] buf = new byte[1500];
      while (true) {
        count = _sock.ReceiveFrom(buf, ref _rem_ep);
        if (count < 4) {
          _scount--;
          Console.WriteLine("ACK recv");
        }
        else {
          _node.HandleData(buf, count);
          Console.WriteLine("Handled packet of size " + count);
        }
      }
    }
  }
}
