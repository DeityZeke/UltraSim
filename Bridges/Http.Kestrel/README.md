# Kestrel HTTP Endpoint Bridge

External-technology realization of `Framework.Http.Endpoint`; references that contract and
`Microsoft.AspNetCore.App`, not Server, Client or Game. One terminal ASP.NET request delegate
creates the neutral exchange and calls the Framework router. ASP.NET owns no semantic route table
or UltraSim process lifetime. `IWebHost.StartAsync/StopAsync/Dispose` explicitly own the component.

HTTP/1.1, exact IPv4 binding, no TLS, MVC, controllers, DI service-provider architecture or dynamic
routes. Native `RawTarget` avoids authorizing escaped/normalized aliases. Requests with query data
remain distinct rejections. A service holds its worker through `Body.WriteAsync` and `CompleteAsync`.
Request-aborted and deadline cancellation use supported connection Abort and observe the real write.
Abort is idempotent per exchange. Unexpected dispatch failures are retained for retirement; no NRE
or blanket lifecycle suppression is used.

Defensive backend limits: 1,024 connections, zero request body, 8 KiB/32 request headers, 2 KiB
request line, 5 s headers and 30 s keep-alive. These do not replace independent service limits.
Native graceful-stop allowance is 100 ms before Kestrel aborts remaining connections; Framework
requires observed backend and worker retirement before successful disposal. Private callbacks are
not individually instrumented: the supported lifecycle, observable zero ownership, late old-socket
controls and same-port rebinding form the assurance boundary.

Dedicated .NET hosting requires ASP.NET Core 8 runtime (tested 8.0.31). Complete's already-running
Godot CoreCLR cannot attach another shared framework. Its project restores the official
`Microsoft.AspNetCore.App.Runtime.linux-x64` 8.0.31 pack and publishes those assemblies as private
dependencies, including dependency-manifest entries. SinglePlayer/Online do not reference or ship
this bridge. Current exports are Linux x64; another export platform needs its matching supported
runtime-pack composition, not an architecture/protocol change. Framework is not Kestrel-dependent.
