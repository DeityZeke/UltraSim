# Framework HTTP Endpoint

Instance-owned **inbound** HTTP contracts, frozen service-owned method/path routes, independent
bounded service queues/workers/deadlines, diagnostics and retirement. This is Framework runtime
infrastructure, not an HTTP Engine Core or an outbound HTTP API. It references BCL only.

Composition supplies finite `HttpServiceDefinition` providers and an `IHttpEndpointBackend`.
Duplicate service IDs or method/path owners fail before any backend starts. Requests disclose only
method, exact raw canonical path and query presence. Services return immutable response memory;
the owner must retain it through completion. No native response, Game authority, authentication
context, package, discovery, or ASP.NET type crosses this seam.

The active budget includes handler execution, the real response write and native completion.
Queue refusal returns 503 without creating admitted debt. Unknown method/path/query returns 404.
Each admitted exchange reconciles to completed, aborted, faulted or currently active/queued work.
Service timeouts cancel and abort the exchange, then await the real operation. An unexpected
handler failure closes that service's admission and retains every already-running worker failure
in finite worker slots. Repeated/concurrent retirement callers receive the same retained failure.

Retirement closes admission, cancels service work, stops the native backend through its supported
lifecycle, joins service owners, disposes resources, then publishes Disposed. Ownership gates are
never held over handler execution, writes or waits. Handlers must cooperate with cancellation.
The finite failure deadline is **not** a license to abandon ownership: an unretired handler/backend
makes Dispose fail with Faulted state and keeps resources owned rather than publishing success.
Do not dispose an endpoint from its own handler (self-join).

One owner exists per bound endpoint, not per feature or process. A future Runtime Host may
supply providers, but there is no dynamic registration/generation/package framework here. Listener
sharing does not share authorization or backpressure domains. Discovery must remain an open,
transport-neutral application contract consumable by clients, websites and third parties; registry,
status/proof, federation, richer web and outbound integrations are not implemented here. Future
read services should consume committed bounded projections, not live mutable ECS/Game state.

Current production composition: `Server/ConceptDemo` supplies `StaticContentHttpService` from
`Server/Hosting`; `Bridges/Http.Kestrel` realizes the endpoint. A test-only tiny status-like provider
proves coexistence during Content saturation on the same port without implementing Discovery.
