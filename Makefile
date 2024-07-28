.PHONY: build

client:
	@dotnet run --project TcpChat.Client
server:
	@dotnet run --project TcpChat.Server
build:
	@dotnet publish TcpChat.Server -o build/Server -c Release
	@dotnet publish TcpChat.Client -o build/Client -c Release
