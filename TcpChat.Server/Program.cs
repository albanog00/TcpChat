using TcpChat.Server;

TcpChatServer chat;

void InterruptHandler(object? sender, ConsoleCancelEventArgs args)
{
    chat.Shutdown();
    args.Cancel = true;
}

string name = "IRC";
int port = 6000;
chat = new TcpChatServer(name, port);

Console.CancelKeyPress += InterruptHandler;

chat.Run();
