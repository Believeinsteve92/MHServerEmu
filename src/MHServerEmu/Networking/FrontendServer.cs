﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using MHServerEmu.Common;
using MHServerEmu.GameServer;

namespace MHServerEmu.Networking
{
    public class FrontendServer
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private GameServerManager _gameServerManager = new();

        private Socket _socket;
        private List<FrontendClient> _clientList = new();

        public FrontendServer(int port)
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            _socket.Listen(10);

            Logger.Info($"FrontendServer is listening on localhost:{port}...");

            BeginAccept();
        }

        private void BeginAccept()
        {
            Logger.Info("Waiting for connections...");
            _socket.BeginAccept(new AsyncCallback(AcceptCallback), null);
        }

        private void AcceptCallback(IAsyncResult result)
        {
            Logger.Info("Client connected");
            Socket clientSocket = _socket.EndAccept(result);
            FrontendClient client = new FrontendClient(clientSocket, _gameServerManager);
            _clientList.Add(client);
            new Thread(() => client.Run()).Start();
            BeginAccept();
        }

        public void Shutdown()
        {
            foreach (FrontendClient client in _clientList)
            {
                client.Disconnect();
            }
        }
    }
}
