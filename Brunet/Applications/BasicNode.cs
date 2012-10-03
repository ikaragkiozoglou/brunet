/*
Copyright (C) 2008  David Wolinsky <davidiw@ufl.edu>, University of Florida

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
using System.Collections;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Serialization;
using System.Security.Cryptography;
using System.Threading;
using System.Net;

using Brunet;
using Brunet.Services.Coordinate;
using Brunet.Services.Dht;
using Brunet.Services.XmlRpc;
using Brunet.Security;
using Brunet.Security.Dtls;
using Brunet.Security.PeerSec;
using Brunet.Security.PeerSec.Symphony;
using Brunet.Security.Transport;
using Brunet.Relay;
using Brunet.Transport;
using Brunet.Messaging;
using Brunet.Symphony;
using Brunet.Xmpp;
using Brunet.Util;

/**
\namespace Brunet.Applications
necessary for running Brunet.Node in a simple complete manner.
Besides providing basic functionality, this namespace and class offer 
some extra features, such as the ability to cleanly shutdown via ctrl-c,
a user configured Rpc method for providing information during a crawl
(see Information), and the ability to determine the IP addresses on the
local machines and their associated names.
\brief Provides BasicNode which implements a simple Brunet P2P Node.
*/
namespace Brunet.Applications {
  /// <summary>BasicNode provides the core Brunet features in a deployable model
  /// the inputs are a xml config file called NodeConfig, which specifies which
  /// if any services to deploy.  Other projects should inherit this as their
  /// base class rather than implementing their own interfaces to Brunet.</summary>
  public class BasicNode {
    /// <summary>The path to the NodeConfig.</summary>
    protected String _path;
    /// <summary>The NodeConfig that defines the Brunet.Node.</summary>
    protected NodeConfig _node_config;
    /// <summary>Path to the node config (for updating it).</summary>
    protected string _node_config_path;

    /// <summary>Contains the StructuredNode and other Brunet features.</summary>
    protected ApplicationNode _app_node;

    /// <summary>The XmlRpc service provider.</summary>
    protected XmlRpcManagerServer _xrm;

    /// <summary>The shutdown service provider.</summary>
    public Shutdown Shutdown { get { return _shutdown; } }
    protected Shutdown _shutdown;

    /// <summary>True if the node should reincarnate itself if Node.Connect
    /// exits or throws an exception</summary>
    protected bool _running;

    protected Dictionary<string, PathELManager> _type_to_pem;
    protected Random _rand;
    protected FuzzyEvent _fe_stop_pem;
    public readonly XmppService XmppService;

    /// <summary>Prepares a BasicNode.</summary>
    /// <param name="node_config">A node config object.</param>
    public BasicNode(NodeConfig node_config)
    {
      _node_config = node_config;
      _running = true;
      _shutdown = Shutdown.GetShutdown();
      _shutdown.OnExit += OnExit;

      _type_to_pem = new Dictionary<string, PathELManager>();
      _rand = new Random();

      if(_node_config.XmppServices.Enabled) {
        XmppService = new XmppService(_node_config.XmppServices.Username,
            _node_config.XmppServices.Password,// _node_config.XmppServices.Server,
            _node_config.XmppServices.Port);
        XmppService.Connect();
      }
    }

    /// <summary>This should be called by the Main after all the setup is done
    /// this passes control to the _node and won't return until the program is
    /// exiting.  (It is synchronous.)</summary>
    public virtual void Run()
    {
      int sleep = 60, sleep_min = 60, sleep_max = 3600;
      DateTime start_time = DateTime.UtcNow;

      // Keep creating new nodes no matter what!
      while(_running) {
        ApplicationNode node = CreateNode(_node_config);
        _app_node = node;

        new Information(node.Node, "BasicNode", node.SecurityOverlord);
        Console.WriteLine("Starting at {0}, {1} is connecting to {2}.",
            DateTime.UtcNow, node.Node.Address, node.Node.Realm);

        node.Node.DisconnectOnOverload = true;
        start_time = DateTime.UtcNow;

        Thread pthread = null;
        // Must do this to remove it after successfully creating the new node
        Node.StateChangeHandler add_node = null;
        if(node.PrivateNode != null) {
          // Delayed add, removes ~15 seconds off bootstrapping time
          add_node = delegate(Node n, Node.ConnectionState cs) {
            if(cs != Node.ConnectionState.Connected) {
              return;
            }
            node.Node.StateChangeEvent -= add_node;

            new Information(node.PrivateNode.Node, "PrivateBasicNode", node.SecurityOverlord);
            pthread = new Thread(node.PrivateNode.Node.Connect);
            pthread.Start();
          };
          node.Node.StateChangeEvent += add_node;
        }

        node.Node.Connect();

        if(node.PrivateNode != null) {
          ApplicationNode pnode = node.PrivateNode;
          pnode.Node.Disconnect();
          if(pthread != null) {
            pthread.Join();
          }
        }

        // Assist in garbage collection
        if(_xrm != null) {
          _xrm.Remove(node.Node);
        }
        node = null;
        _app_node = null;

        // DisconnectOnOverload seems to be having issues with pathing...
        foreach(var kvm in _type_to_pem) {
          kvm.Value.Stop();
        }

        _type_to_pem.Clear();

        if(!_running) {
          break;
        }

        DateTime now = DateTime.UtcNow;
        Thread.Sleep(sleep * 1000);

        if(now - start_time < TimeSpan.FromSeconds(sleep_max)) {
          sleep *= 2;
          sleep = (sleep > sleep_max) ? sleep_max : sleep;
        } else {
          sleep /= 2;
          sleep = (sleep < sleep_min) ? sleep_min : sleep;
        }
      }
    }

    /// <summary>Creates an ApplicationNode and prepares it for connection to
    /// the overlay.  For historical reasons it is linked to _node, _dht,
    /// _rpc_dht, and _bso.</summary>
    public virtual ApplicationNode CreateNode(NodeConfig node_config) {
      // Get a Node ID for the new Node
      AHAddress address = null;
      try {
        address = (AHAddress) AddressParser.Parse(node_config.NodeAddress);
      } catch {
        address = Utils.GenerateAHAddress();
      }

      // Create the Node state
      StructuredNode node = new StructuredNode(address, node_config.BrunetNamespace);
      _shutdown.OnExit += node.Disconnect;
      IEnumerable addresses = IPAddresses.GetIPAddresses(node_config.DevicesToBind);

      SecurityOverlord so = null;
      // Enable Security if requested
      if(node_config.Security.Enabled) {
        if(node_config.Security.SelfSignedCertificates) {
          SecurityPolicy.SetDefaultSecurityPolicy(SecurityPolicy.DefaultEncryptor,
              SecurityPolicy.DefaultAuthenticator, true);
        }

        byte[] blob = null;
        using(FileStream fs = File.Open(node_config.Security.KeyPath, FileMode.Open)) {
          blob = new byte[fs.Length];
          fs.Read(blob, 0, blob.Length);
        }

        RSACryptoServiceProvider rsa_private = new RSACryptoServiceProvider();
        rsa_private.ImportCspBlob(blob);

        CertificateHandler ch = null;
        if(node_config.Security.Dtls) {
          ch = new OpenSslCertificateHandler(node_config.Security.CertificatePath,
              address.ToString());
        } else {
          ch = new CertificateHandler(node_config.Security.CertificatePath,
              address.ToString());
        }


        if(node_config.Security.SecureEdges) {
          node.EdgeVerifyMethod = EdgeVerify.AddressInSubjectAltName;
        }

        // A hack to enable a test for security that doesn't require each peer
        // to exchange certificates
        if(node_config.Security.TestEnable) {
          blob = rsa_private.ExportCspBlob(false);
          RSACryptoServiceProvider rsa_pub = new RSACryptoServiceProvider();
          rsa_pub.ImportCspBlob(blob);
          CertificateMaker cm = new CertificateMaker("United States", "UFL", 
              "ACIS", "David Wolinsky", "davidiw@ufl.edu", rsa_pub,
              "brunet:node:abcdefghijklmnopqrs");
          Certificate cacert = cm.Sign(cm, rsa_private);

          cm = new CertificateMaker("United States", "UFL", 
              "ACIS", "David Wolinsky", "davidiw@ufl.edu", rsa_pub,
              address.ToString());
          Certificate cert = cm.Sign(cacert, rsa_private);
          ch.AddCACertificate(cacert.X509);
          ch.AddSignedCertificate(cert.X509);
        }

        if(node_config.Security.Dtls) {
          OpenSslCertificateHandler ssl_ch = ch as OpenSslCertificateHandler;
          so = new DtlsOverlord(rsa_private, ssl_ch, new PType(20));
          node.GetTypeSource(new PType(20)).Subscribe(so, null);
        } else {
          so = new SymphonySecurityOverlord(node, rsa_private, ch, node.Rrm);
          node.GetTypeSource(PeerSecOverlord.Security).Subscribe(so, null);
        }
        so.Subscribe(node, null);
      }

      // Add Dht
      new TableServer(node);
      IDht dht = new Dht(node, 3, 20);
      RpcDhtProxy dht_proxy = new RpcDhtProxy(dht, node);

      // Setup Vivaldi if requested
      IRelayOverlap ito = null;
      NCService ncservice = null;
      if(node_config.NCService.Enabled) {
        ncservice = new NCService(node, node_config.NCService.Checkpoint);

        if (node_config.NCService.OptimizeShortcuts) {
          node.Ssco.TargetSelector = new VivaldiTargetSelector(node, ncservice);
        }
        ito = new NCRelayOverlap(ncservice);
      } else {
        ito = new SimpleRelayOverlap();
      }

      // Create the ApplicationNode
      ApplicationNode app_node = new ApplicationNode(node, dht, dht_proxy, ncservice, so);

      // Add Edge listeners
      EdgeListener el = null;
      foreach(NodeConfig.EdgeListener item in node_config.EdgeListeners) {
        el = CreateEdgeListener(item, app_node, addresses);
        if(node_config.Security.SecureEdgesEnabled) {
          el = new SecureEdgeListener(el, so);
        }
        node.AddEdgeListener(el);
      }

      // Create the tunnel and potentially wrap it in a SecureEL
      el = new Relay.RelayEdgeListener(node, ito);
      if(node_config.Security.SecureEdgesEnabled) {
        el = new SecureEdgeListener(el, so);
      }
      node.AddEdgeListener(el);

      List<TransportAddress> RemoteTAs = null;
      if(node_config.RemoteTAs != null) {
        RemoteTAs = new List<TransportAddress>();
        foreach(String ta in node_config.RemoteTAs) {
          RemoteTAs.Add(TransportAddressFactory.CreateInstance(ta));
        }
        node.RemoteTAs = RemoteTAs;
      }

      // Add XmlRpc
      if(node_config.XmlRpcManager.Enabled) {
        if(_xrm == null) {
          _xrm = new XmlRpcManagerServer(node_config.XmlRpcManager.Port);
        }
        _xrm.Add(node, GetXmlRpcUri(app_node));
        new RpcDht(dht, node);
      }

      if(node_config.PrivateNodeConfig != null &&
          node_config.PrivateNodeConfig.Enabled)
      {
        CreatePrivateNode(app_node, NodeConfig.GetPrivateNodeConfig(node_config));
      }
      return app_node;
    }

    /// <summary>Create a PrivateNode given a shared node and a node_config.</summary>
    protected void CreatePrivateNode(ApplicationNode shared,
        NodeConfig node_config)
    {
      // Not necessary, but its nice to map the public and private overlay nodes
      node_config.NodeAddress = shared.Node.Address.ToString();
      ApplicationNode app_node = CreateNode(node_config);

      EdgeListener el = new SubringEdgeListener(shared.Node, app_node.Node);
      if(node_config.Security.SecureEdgesEnabled) {
        el = new SecureEdgeListener(el, app_node.SecurityOverlord);
      }
      app_node.Node.AddEdgeListener(el);
      Discovery d = new DhtDiscovery(app_node.Node as StructuredNode,
          shared.Dht, shared.Node.Realm, shared.DhtProxy);
      app_node.Node.AddTADiscovery(d);
      shared.PrivateNode = app_node;
    }

    protected virtual string GetXmlRpcUri(ApplicationNode appnode)
    {
      return appnode.Node.Realm + ":" + appnode.Node.Address;
    }


    /// <summary>Given an EdgeListener info, attempts to find a PathEL, if one is not
    /// found, creates a base EL and wraps it with a PathEL.</summary>
    protected EdgeListener CreateEdgeListener(NodeConfig.EdgeListener el_info,
        ApplicationNode node, IEnumerable addresses)
    {
      PathELManager pem = null;
      if(!_type_to_pem.TryGetValue(el_info.type, out pem)) {
        pem = new PathELManager(CreateBaseEdgeListener(el_info, node, addresses), node.Node);
        pem.Start();
        _type_to_pem[el_info.type] = pem;
      }

      EdgeListener el = pem.CreatePath();
      return el;
    }

    /// <summary>Given a EdgeListener info and a list of addresses to advertise,
    /// returns an EdgeListener.</summary>
    protected EdgeListener CreateBaseEdgeListener(NodeConfig.EdgeListener el_info,
        ApplicationNode node, IEnumerable addresses)
    {
      EdgeListener el = null;
      int port = el_info.port;
      if(el_info.type == "tcp") {
        try {
          el = new TcpEdgeListener(port, addresses);
        } catch {
          el = new TcpEdgeListener(0, addresses);
        }
      } else if(el_info.type == "udp") {
        try {
          el = new UdpEdgeListener(port, addresses);
        } catch {
          el = new UdpEdgeListener(0, addresses);
        }
      } else if(el_info.type == "function") {
        port = port == 0 ? (new Random()).Next(1024, 65535) : port;
        el = new FunctionEdgeListener(port, 0, null);
      } else if(el_info.type == "xmpp") {
        if(!_node_config.XmppServices.Enabled) {
          throw new Exception("XmppServices must be enabled to use XmppEL");
        }
        el = new XmppEdgeListener(XmppService);
        node.Node.AddTADiscovery(new XmppDiscovery(node.Node, XmppService, node.Node.Realm));
      } else {
        throw new Exception("Unrecognized transport: " + el_info.type);
      }
      return el;
    }

    /// <summary>All nodes are disconnected?  Stop the PathEL.</summary>
    protected virtual void StopPem(DateTime now)
    {
      ApplicationNode appnode = _app_node;
      bool stop = appnode == null;

      if(!stop) {
        stop = appnode.Node.ConState == Node.ConnectionState.Disconnected;
      }

      if(stop) {
        foreach(PathELManager pem in _type_to_pem.Values) {
          pem.Stop();
        }
        _type_to_pem.Clear();
        _fe_stop_pem.TryCancel();
      }
    }

    /**
    <summary>This method is registered as a delegate to Shutdown.OnExit and
    will be called when ctrl-c is pressed by the user.  This stops services,
    prevents the node from reincarnating, and then disconnects the node.
    </summary>
    */
    public virtual void OnExit() {
      _running = false;

      if(_xrm != null) {
        _xrm.Stop();
        _xrm = null;
      }

      ApplicationNode appnode = _app_node;
      _app_node = null;

      NCService ncservice = null;
      if(appnode != null) {
        ncservice = appnode.NCService;
      }

      if(ncservice != null && _node_config.NCService.Checkpointing) {
        string checkpoint = ncservice.GetCheckpoint();
        string prev_cp = _node_config.NCService.Checkpoint;
        string empty_cp = (new Point()).ToString();
        if(!checkpoint.Equals(prev_cp) && !checkpoint.Equals(empty_cp))
        {
          _node_config.NCService.Checkpoint = checkpoint;
          _node_config.WriteConfig();
        }
      }

      _fe_stop_pem = Brunet.Util.FuzzyTimer.Instance.DoEvery(StopPem, 500, 500);
    }
  }
}
