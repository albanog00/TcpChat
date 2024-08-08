using System.Net.Sockets;
using System.Text;

namespace TcpChat.Client;

public class TcpClient
{
    private System.Net.Sockets.TcpClient _client;
    private NetworkStream _stream => _client.GetStream();

    private readonly Queue<string> _messages = [];

    private readonly Mutex Mutex = new();

    private const int BufferSize = 8 << 10;
    public readonly string Name;
    public readonly string ServerAddress;
    public readonly UInt16 Port;
    public bool Running { get; set; } = false;

    public TcpClient(string serverAddress, UInt16 port, string name)
    {
        ServerAddress = serverAddress;
        Port = port;
        Name = name;

        _client = new System.Net.Sockets.TcpClient();
    }

    public void Connect()
    {
        _client.Connect(ServerAddress, Port);
        _client.SendBufferSize = BufferSize;
        _client.ReceiveBufferSize = BufferSize;

        var endpoint = _client.Client.RemoteEndPoint;

        if (!_client.Connected)
        {
            _client.Close();
            Console.WriteLine("Could not connect to {0}:{1} at {2}", ServerAddress, Port, endpoint);
            return;
        }

        Send(String.Format("name:{0}", Name));

        if (IsDisconnected())
        {
            Console.WriteLine("Rejected");
            CleanupClient();
            return;
        }

        Running = true;
    }

    public void Send(string msg)
    {
        msg += "\r\n";
        msg = Convert.ToString(msg.Length).PadLeft(4, '0') + msg;

        byte[] buffer = Encoding.UTF8.GetBytes(msg);
        _stream.Write(buffer, 0, buffer.Length);
    }

    public byte[] Read()
    {
        int messageLength = _client.Available;

        byte[] buffer = new byte[messageLength];
        int written = _stream.Read(buffer);
        return buffer[..written];
    }

    public void Close() => CleanupClient();

    private void SendMessage()
    {
        if (!Mutex.WaitOne(1))
            return;

        try
        {
            var msg = Console.ReadLine()!.Trim();
            Console.CursorTop -= 1;

            if (msg is "quit" or "exit")
            {
                Console.WriteLine("Disconnecting...");
                Running = false;
            }
            else if (!string.IsNullOrEmpty(msg))
            {
                Send(msg);
            }
        }
        catch { }

        Mutex.ReleaseMutex();
    }

    private void ListenForMessages()
    {
        if (_client.Available == 0)
            return;

        var buffer = Read();
        _messages.Enqueue(Encoding.UTF8.GetString(buffer));
    }

    private void ShowMessages()
    {
        if (_messages.Count == 0)
            return;

        while (_messages.TryDequeue(out var msg))
            Console.Write(msg);
    }

    private void CheckDisconnected()
    {
        if (IsDisconnected())
        {
            Running = false;
            Console.WriteLine("Server has disconnected.");
        }
    }

    public void Run()
    {
        while (Running)
        {
            Task.Run(SendMessage);

            ListenForMessages();
            ShowMessages();
            CheckDisconnected();

            Thread.Sleep(1);
        }

        CleanupClient();
    }

    private void CleanupClient()
    {
        _stream.Close();
        _client.Close();
    }

    private bool IsDisconnected()
    {
        bool disconnected = false;

        try
        {
            Socket socket = _client.Client;
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
}
