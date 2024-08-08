using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TcpChat.Server;

public class TcpServer
{
    private TcpListener _listener;

    private readonly ConcurrentDictionary<string, TcpClient> _clients = [];
    private readonly ConcurrentDictionary<TcpClient, NetworkStream> _streams = [];
    private readonly ConcurrentQueue<string> _messageQueue = [];

    private readonly CancellationTokenSource CancellationToken;

    public readonly string ChatName;
    public readonly int Port;

    public const int BufferSize = 8 << 10;

    public TcpServer(string chatName, int port)
    {
        ChatName = chatName;
        Port = port;
        CancellationToken = new();

        _listener = new(IPAddress.Any, port);
    }

    public void Shutdown()
    {
        CancellationToken.Cancel();

        _listener.Stop();
        _messageQueue.Clear();

        Console.WriteLine("Shutting down server");
    }

    public void Run()
    {
        Console.WriteLine("Starting the `{0}` TCP Chat Server on port {1}", ChatName, Port);
        Console.WriteLine("Press Ctrl-C to shut down the server.");

        _listener.Start();

        while (!CancellationToken.IsCancellationRequested)
            HandleNewConnection();

        Console.WriteLine("Server is shut down");
    }

    private int GetMessageLength(NetworkStream stream)
    {
        byte[] lengthBytes = new byte[4];
        stream.ReadExactly(lengthBytes, 0, 4);

        var bytes = lengthBytes.Select(x => x - 48);
        int length = 0;

        int pos = 1000;
        foreach (var b in bytes)
        {
            length += b * pos;
            pos /= 10;
        }

        return length;
    }

    private void HandleNewConnection()
    {
        try
        {
            var newClient = _listener.AcceptTcpClient();

            Task.Run(() =>
            {
                var stream = newClient.GetStream();

                newClient.SendBufferSize = BufferSize;
                newClient.ReceiveBufferSize = BufferSize;
                stream.ReadTimeout = 10;

                var endpoint = newClient.Client.RemoteEndPoint;
                Console.WriteLine("Handling new client from {0}...", endpoint);

                int length = GetMessageLength(stream);

                if (length <= 0)
                {
                    Console.WriteLine("Can't identify {0}", endpoint);
                    stream.Close();
                    newClient.Client.Close();
                    return;
                }

                byte[] buffer = new byte[length];
                stream.ReadExactly(buffer, 0, length);
                string msg = Encoding.UTF8.GetString(buffer, 0, length);

                if (!msg.StartsWith("name:"))
                {
                    Console.WriteLine("Can't identify {0}", endpoint);
                    stream.Close();
                    newClient.Client.Close();
                    return;
                }

                string name = msg[5..].Trim();
                if (name == String.Empty || _clients.TryGetValue(name, out _))
                {
                    Console.WriteLine("Cant' identify messenger {0}", endpoint);
                    stream.Close();
                    newClient.Client.Close();
                    return;
                }

                _clients.TryAdd(name, newClient);
                _streams.TryAdd(newClient, stream);

                Console.WriteLine("{0} is a Messenger with name {1}", endpoint, name);

                // msg = String.Format("Welcome to the `{0}` Chat Server!\r\n", ChatName);
                // buffer = Encoding.UTF8.GetBytes(msg);
                // stream.Write(buffer, 0, buffer.Length);

                EnqueueMessage(String.Format("{0} has joined the chat\r\n", name));

                Task.Run(() =>
                {
                    int queueIndex = _messageQueue.Count - 1;

                    while (!CancellationToken.IsCancellationRequested)
                    {
                        if (_messageQueue.Count < queueIndex)
                            queueIndex = _messageQueue.Count - 1;

                        if (CheckForDisconnect(newClient, name))
                            break;

                        SendMessages(newClient, stream, ref queueIndex);
                        CheckForNewMessages(newClient, stream, name);

                        Thread.Sleep(1);
                    }

                    CleanupClient(newClient, name);
                });
            });
        }
        catch { }
    }

    private bool CheckForDisconnect(TcpClient client, string name)
    {
        if (IsDisconnected(client))
        {
            RemoveClient(client, name);
            return true;
        }

        return false;
    }

    private void CheckForNewMessages(TcpClient client, NetworkStream stream, string name)
    {
        try
        {
            if (client.Available > 0)
            {
                int length = GetMessageLength(stream);
                byte[] buffer = new byte[length];

                stream.Read(buffer, 0, length);
                var msg = String.Format("{0}: {1}", name, Encoding.UTF8.GetString(buffer));

                EnqueueMessage(msg);
            }
        }
        catch { }
    }

    private void SendMessages(TcpClient client, NetworkStream stream, ref int queueIndex)
    {
        try
        {
            while (queueIndex < _messageQueue.Count)
            {
                var msg = _messageQueue.ElementAt(queueIndex++);
                byte[] buffer = Encoding.UTF8.GetBytes(msg);
                stream.Write(buffer);
            }
        }
        catch { }
    }

    private void EnqueueMessage(string msg)
    {
        if (_messageQueue.Count > BufferSize)
            _messageQueue.Clear();
        _messageQueue.Enqueue(msg);
    }

    private bool IsDisconnected(TcpClient client)
    {
        bool disconnected = false;

        try
        {
            Socket socket = client.Client;
            disconnected =
                socket.Poll(10 * TimeSpan.FromMicroseconds(1), SelectMode.SelectRead)
                && (socket.Available == 0);
        }
        catch
        {
            disconnected = true;
        }

        return disconnected;
    }

    private void RemoveClient(TcpClient client, string name)
    {
        var msg = String.Format("{0} has left the chat\r\n", name);

        EnqueueMessage(msg);
        _clients.TryRemove(name, out _);
        _streams.TryRemove(client, out _);
    }

    private void CleanupClient(TcpClient client, string name)
    {
        try
        {
            _streams[client].Close();
            client.Close();
            RemoveClient(client, name);
        }
        catch { }
    }
}
