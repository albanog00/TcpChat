using TcpChat.Server;

TcpServer chat;

void InterruptHandler(object? sender, ConsoleCancelEventArgs args)
{
    chat.Shutdown();
    args.Cancel = true;
}

string name = "IRC";
int port = 6000;
chat = new TcpServer(name, port);

Console.CancelKeyPress += InterruptHandler;

chat.Run();
