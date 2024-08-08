using TcpChat.Client;

Console.WriteLine("Enter your name: ");
var name = Console.ReadLine();

var client = new TcpClient("localhost", 6000, name!);

client.Connect();
client.Run();
