.PHONY: build benchmark

client:
	@dotnet run --project src/TcpChat.Client
server:
	@dotnet run --project src/TcpChat.Server
build:
	@dotnet publish src/TcpChat.Server -o build/Server -c Release
	@dotnet publish src/TcpChat.Client -o build/Client -c Release
benchmark:
	@dotnet run --project benchmark/TcpChat.Benchmark -c Release 
