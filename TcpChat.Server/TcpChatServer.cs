using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TcpChat.Server;

public class TcpChatServer
{
    private TcpListener _listener;

    private Dictionary<TcpClient, string> _names = [];
    private Dictionary<string, TcpClient> _clients = [];

    private SemaphoreSlim ConnectionSemaphore = new(Environment.ProcessorCount);
    private SpinLock DisconnectLock = new();
    private SpinLock SendLock = new();
    private SpinLock CheckLock = new();

    private Queue<string> _messageQueue = [];

    public readonly string ChatName;
    public readonly int Port;
    public bool Running { get; set; }

    public readonly UInt16 BufferSize = 2 * 1024;

    public TcpChatServer(string chatName, int port)
    {
        ChatName = chatName;
        Port = port;

        Running = false;
        _listener = new(IPAddress.Any, port);
    }

    public void Shutdown()
    {
        Running = false;
        Console.WriteLine("Shutting down server");
    }

    public void Run()
    {
        Console.WriteLine("Starting the `{0}` TCP Chat Server on port {1}", ChatName, Port);
        Console.WriteLine("Press Ctrl-C to shut down the server.");

        _listener.Start();
        Running = true;

        while (Running)
        {
            Task.Run(HandleNewConnection);
            Task.Run(CheckForDisconnect);
            Task.Run(SendMessages);
            Task.Run(CheckForNewMessages);

            Thread.Sleep(10);
        }

        foreach (var (_, client) in _clients)
            CleanupClient(client);

        Console.WriteLine("Server is shut down");
    }

    private void HandleNewConnection()
    {
        if (!_listener.Pending() || !ConnectionSemaphore.Wait(1)) return;

        var newClient = _listener.AcceptTcpClient();

        ConnectionSemaphore.Release();

        var stream = newClient.GetStream();
        stream.ReadTimeout = 10;

        newClient.SendBufferSize = BufferSize;
        newClient.ReceiveBufferSize = BufferSize;

        var endpoint = newClient.Client.RemoteEndPoint;
        Console.WriteLine("Handling new client from {0}...", endpoint);

        byte[] buffer = new byte[BufferSize];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);

        if (bytesRead <= 0)
        {
            Console.WriteLine("Can't identify {0}", endpoint);
            CleanupClient(newClient);
            return;
        }

        string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        if (msg.StartsWith("name:"))
        {
            string name = msg[5..].Trim();

            ConnectionSemaphore.Wait();

            if (name == String.Empty || _clients.TryGetValue(name, out var _))
            {
                Console.WriteLine("Cant' identify messenger {0}", endpoint);
                CleanupClient(newClient);
                return;
            }

            _names.Add(newClient, name);
            _clients.Add(name, newClient);

            Console.WriteLine("{0} is a Messenger with name {1}", endpoint, name);

            msg = String.Format("Welcome to the `{0}` Chat Server!", ChatName);
            buffer = Encoding.UTF8.GetBytes(msg);
            stream.Write(buffer, 0, buffer.Length);

            _messageQueue.Enqueue(String.Format("{0} has joined the chat", name));

            ConnectionSemaphore.Release();
        } 
    }

    private void CheckForDisconnect()
    {
        bool acquired = false;
        DisconnectLock.TryEnter(ref acquired);
        if (!acquired) return;

        foreach (var (_, client) in _clients)
        {
            if (IsDisconnected(client))
            {
                string name = _names[client];

                _messageQueue.Enqueue(String.Format("{0} has left the chat", name));

                _names.Remove(client);
                _clients.Remove(name);
                CleanupClient(client);
            }
        }

        DisconnectLock.Exit();
    }

    private void CheckForNewMessages()
    {
        bool acquired = false;
        CheckLock.TryEnter(ref acquired);
        if (!acquired) return;

        foreach (var (_, client) in _clients)
        {
            int messageLength = client.Available;
            if (messageLength > 0)
            {
                byte[] buffer = new byte[messageLength];
                client.GetStream().Read(buffer, 0, messageLength);

                string msg = String.Format(
                    "{0}: {1}",
                    _names[client],
                    Encoding.UTF8.GetString(buffer)
                );
                _messageQueue.Enqueue(msg);
            }
        }

        CheckLock.Exit();
    }

    private void SendMessages()
    {
        bool acquired = false;
        SendLock.TryEnter(ref acquired);
        if (!acquired) return;

        while (_messageQueue.Count > 0)
        {
            _messageQueue.TryDequeue(out var msg);
            if (msg == null) break;

            byte[] buffer = Encoding.UTF8.GetBytes(msg);
            Console.WriteLine(msg);

            foreach (var (_, client) in _clients)
                client.GetStream().Write(buffer);
        }

        SendLock.Exit();
    }

    private static bool IsDisconnected(TcpClient client)
    {
        try
        {
            return client.Client.Poll(1 * 1000, SelectMode.SelectRead) && (client.Client.Available == 0);
        }
        catch (SocketException exception)
        {
            Console.WriteLine(exception.Message);
            return true;
        }
    }

    private static void CleanupClient(TcpClient client)
    {
        client.GetStream().Close();
        client.Close();
    }
}
