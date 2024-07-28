using TcpChat.Client;

Console.WriteLine("Enter your name: ");
var name = Console.ReadLine();

var client = new TcpChatClient("localhost", 6000, name!);

client.Run();
