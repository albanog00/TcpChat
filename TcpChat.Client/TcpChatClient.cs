using System.Net.Sockets;
using System.Text;

namespace TcpChat.Client;

public class TcpChatClient
{
    public readonly string ServerAddress;
    public readonly UInt16 Port;

    private TcpClient _client;
    private NetworkStream _stream;

    private static SpinLock SendLock = new();
    private static SpinLock ListenLock = new();
    private static SpinLock ShowLock = new();
    
    public bool Running { get; set; } = false;

    private readonly Queue<string> _messages;

    private readonly UInt16 BufferSize = 2 * 1024;

    public readonly string Name;

    public TcpChatClient(string serverAddress, UInt16 port, string name)
    {
        ServerAddress = serverAddress;
        Port = port;
        Name = name;

        _client = new TcpClient();
        _client.SendBufferSize = BufferSize;
        _client.ReceiveBufferSize = BufferSize;

        Connect();

        _stream = _client.GetStream();
        _messages = [];
    }

    private void Connect()
    {
        _client.Connect(ServerAddress, Port);
        var endpoint = _client.Client.RemoteEndPoint;

        if (!_client.Connected)
        {
            _client.Close();
            Console.WriteLine("Could not connect to {0}:{1} at {2}", ServerAddress, Port, endpoint);
            return;
        }

        byte[] buffer = Encoding.UTF8.GetBytes(String.Format("name:{0}", Name));

        var stream = _client.GetStream();
        stream.Write(buffer, 0, buffer.Length);

        if (IsDisconnected())
        {
            Console.WriteLine("Rejected");
            CleanupNetworkResources();
            return;
        }

        Running = true;
    }

    private void SendMessages()
    {
        bool acquired = false;
        SendLock.TryEnter(ref acquired);
        if (!acquired) return;

        string? msg = Console.ReadLine()!.Trim();
        Console.CursorTop -= 1;

        if (msg.ToLower() is "quit" or "exit")
        {
            Console.WriteLine("Disconnecting...");
            Running = false;
        }
        else if (!string.IsNullOrEmpty(msg))
        {
            byte[] buffer = Encoding.UTF8.GetBytes(msg);
            _stream.Write(buffer, 0, buffer.Length);
        }

        SendLock.Exit();
    }

    private void ListenForMessages()
    {
        bool acquired = false;
        ListenLock.TryEnter(ref acquired);
        if (!acquired) return;

        int messageLength = _client.Available;
        if (messageLength > 0)
        {
            byte[] buffer = new byte[messageLength];
            _stream.Read(buffer, 0, messageLength);
            _messages.Enqueue(Encoding.UTF8.GetString(buffer));
        }

        ListenLock.Exit();
    }

    private void ShowMessages()
    {
        bool acquired = false;
        ShowLock.TryEnter(ref acquired);
        if (!acquired) return;

        while (_messages.Count > 0)
        {
            var msg = _messages.Dequeue();
            Console.WriteLine(msg);
        }

        ShowLock.Exit();
    }

    public void Run()
    {
        while (Running)
        {
            if (_client.Available > 0) Task.Run(ListenForMessages);
            if (_messages.Count > 0) Task.Run(ShowMessages);
            Task.Run(SendMessages);

            if (IsDisconnected())
            {
                Running = false;
                Console.WriteLine("Server has disconnected.");
            }

            Thread.Sleep(10);
        }
        CleanupNetworkResources();
    }

    private void CleanupNetworkResources()
    {
        _stream.Close();
        _client.Close();
    }

    private bool IsDisconnected()
    {
        try
        {
            Socket socket = _client.Client;
            return socket.Poll(10 * 1000, SelectMode.SelectRead) && (socket.Available == 0);
        }
        catch (SocketException ex)
        {
            Console.WriteLine(ex.Message);
            return true;
        }
    }
}
