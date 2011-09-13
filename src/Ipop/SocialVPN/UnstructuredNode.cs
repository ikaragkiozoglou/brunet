/*
Copyright (C) 2011 Pierre St Juste <ptony82@ufl.edu>, University of Florida

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
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;

using Brunet;
using Brunet.Transport;
using Brunet.Connections;
using Brunet.Util;
using Brunet.Messaging;
using Brunet.Symphony;

namespace Ipop.SocialVPN 
{

  public class UnstructuredNode : Node
  {

    public class UDataHandler : IDataHandler
    {
      public UDataHandler()
      {}

      public void HandleData(MemBlock b, ISender sender, object state)
      {
        Console.WriteLine("thread name  = {0}", Thread.CurrentThread.Name);

        if(sender is Connection) {
          sender.Send(new CopyList(UIP, b));
        }
        else {
          string msg = b.GetString(new UTF8Encoding());
          Console.WriteLine(">> {0}", msg);
        }
      }
    }

    protected readonly UDataHandler _udh;

    public static readonly PType UT = new PType("ut");

    public static readonly PType UIP = new PType("uip");

    public override bool IsConnected { get { return true; } }

    public UnstructuredNode(Address addr, string realm) : base(addr, realm)
    {
      _udh = new UDataHandler();
      _rpc.AddHandler("sys:link", new ConnectionPacketHandler(this));
      ConnectionTable.ConnectionEvent += PrintTable;
      RegisterHandlers();
    }

    public void AddEdgeListener(int port)
    {
      UdpEdgeListener uel = new UdpEdgeListener(port);
      AddEdgeListener(uel);
      uel.Start();
    }

    public void RegisterHandlers()
    {
      DemuxHandler.GetTypeSource(UT).Subscribe(_udh, null);
      DemuxHandler.GetTypeSource(UIP).Subscribe(_udh, null);
    }

    public void AddConnection(string sta)
    {
      Console.WriteLine("ta = {0}", sta);
      TransportAddress bta = TransportAddressFactory.CreateInstance(sta);
      List<TransportAddress> tas = new List<TransportAddress>(1);
      tas.Add(bta);

      Linker linker = new Linker(this, null, tas, "Unstructured", sta);
      //linker.FinishEvent += FinishHandler;
      linker.Start();
    }

    protected void FinishHandler(object linker, EventArgs args) {
      PrintTable(linker, args);
    }

    public void SendMessage(string msg, string address)
    {
      UTF8Encoding enc = new UTF8Encoding();
      MemBlock msg_block = enc.GetBytes(msg);

      Connection con = GetConnection(address);
      HandleData(MemBlock.Concat(UT, msg_block), con, null);
    }

    public void PrintTable(object obj, EventArgs args)
    {
      foreach(Connection con in ConnectionTable.State) {
        Console.WriteLine("{0} {1}", con.ConType, con);
      }
    }

    public Connection GetConnection(string address)
    {
      //Address addr = AddressParser.Parse(address);
      Connection result = null;
      ConnectionList list = ConnectionTable.State.GetConnections(
          ConnectionType.Unstructured);
      foreach(Connection con in list) {
        result = con;
        break;
      }
      //int idx = list.IndexOf(result.Address);
      //Console.WriteLine("idx = {0}", idx);
      return result;
    }

    public override void Abort()
    {
    }

    public override void Connect()
    {
      base.Connect();
      StartAllEdgeListeners();
      AnnounceThread();
    }

    public static void Main(string[] args) {
      Address addr = new AHAddress(new RNGCryptoServiceProvider());
      UnstructuredNode node = new UnstructuredNode(addr, "ufl_svpn_030");

      Console.WriteLine("Address = {0}", addr);

      Thread thread = new Thread(node.Connect);
      thread.Start();

      node.AddEdgeListener(Int32.Parse(args[0]));

      Console.Write("Enter ip and port: ");
      string sta = "brunet.udp://" + Console.ReadLine();

      node.AddConnection(sta);

      while(true) {
        Console.WriteLine("Enter message: ");
        string msg = Console.ReadLine();
        //foreach(TransportAddress lta in node.LocalTAs) {
        //  Console.WriteLine("lta {0}", lta);
        //}
        //node.SendMessage(msg, "");
      }

    }

  }

}

