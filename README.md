# ProxyProtocol
Implementation of Proxy Protocol V2 in C#. Example usage:

```
var listener = new TcpListener(IPAddress.Any, 25);
listener.Start();
while (true) {
  var client = await listener.AcceptTcpClientAsync();  
  var proxyprotocol = new ProxyProtocol(client);
  var realRemoteEndpoint = await proxyprotocol.GetRemoteEndpoint();
  realClientIp = realRemoteEndpoint.Address.ToString();  
  
  // do work
}
```
