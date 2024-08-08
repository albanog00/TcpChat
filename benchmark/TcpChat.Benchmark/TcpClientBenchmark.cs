using BenchmarkDotNet.Attributes;
using TcpChat.Client;

namespace TcpChat.Benchmark;

[MemoryDiagnoser]
// [SimpleJob(invocationCount: 1 << 10)]
public class TcpClientBenchmark
{
    [Benchmark]
    public void Send()
    {
        TcpClient client = new("localhost", 6000, Guid.NewGuid().ToString());

        client.Connect();
        client.Close();
    }
}
