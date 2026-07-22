# LoomECS.Net.LiteNetLib

Optional [LiteNetLib](https://github.com/RevenantX/LiteNetLib) adapter for [LoomECS.Net](https://www.nuget.org/packages/LoomECS.Net).
Core `Loom.Net` stays dependency-light (`INetTransport` only); this package plugs real UDP behind that seam.

## Install

```bash
dotnet add package LoomECS.Net
dotnet add package LoomECS.Net.LiteNetLib
```

## Usage

```csharp
using Loom.Net;
using Loom.Net.LiteNetLib;

// Host
using var host = LiteNetLibTransport.StartHost(port: 9050);
host.PeerConnected += peer => server.SendSnapshot(peer);

// Client
using var client = LiteNetLibTransport.Connect("127.0.0.1", 9050);
while (!client.IsAssigned)
{
    host.Poll();
    client.Poll();
}

// Each frame (both sides):
host.Poll();   // or client.Poll()
// then AuthoritativeServer.Update / NetClient.Poll as usual
```

`Send` / `Broadcast` / `TryReceive` match `INetTransport`. Call `Poll()` regularly so LiteNetLib
events drain into the receive queue. Peer ids: server is `1`; connecting clients receive an assigned
id via a short handshake (not exposed as game packets).

See `samples/Loom.ArenaDots` for `--listen` / `--connect` two-process demo.
