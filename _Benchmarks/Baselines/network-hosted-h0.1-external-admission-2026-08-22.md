# Hosted Network H0.1 external-admission evidence

Captured: 2026-08-22  
Checkpoint: H0.0/H0.1 implementation stop  
Scope: real IPv4 UDP socket, externally sourced unknown paths, pre-authentication only

## Frozen sizing

| Item | Bound |
| --- | ---: |
| Cookie | 16 B |
| Cookie key | 32 B per listener lifetime |
| Cookie challenge | 49 B |
| Minimum canonical accepted hello | 109 B |
| P-256 SPKI hello used by acceptance | 136 B |
| Candidate nonce | 32 B |
| Candidate public-key storage | 512 B maximum |
| Default candidate rows | 64 |
| Default tracked source addresses | 1,024 declared / 2,048 hash rows |
| Default known admission-array payload | 77,472 B |

The known payload excludes CLR object/array headers, the shared listener's own
reservation, socket buffers, and the deployment identity provider. The host
snapshot reports listener plus admission/public-key payload separately from
opaque operating-system and cryptography-provider memory.

## Permanent acceptance

Debug and Release both pass:

```text
HostedUdpCookieValidationAllocatesNoConnectionState
HostedUdpAdmissionRateCapacityAndAmplificationAreBounded
HostedUdpAdmissionAcceptsPreviousBucketAndExpiresCandidates
HostedUdpNativeReceiveRoutesUnknownAdmission
WarmedHostedUdpUnknownAdmissionAllocatesZero
```

The suite proves:

- the externally configured listener receives a real datagram from a separately
  created client socket;
- the first hello produces one stateless cookie challenge and zero candidate,
  registered endpoint, connection, protocol-peer, or Game state;
- the deployment public key copies exactly and undersized destinations fail
  without partial publication;
- a returned current/previous-bucket cookie creates one candidate;
- duplicate validated key exchange refreshes rather than duplicating that row;
- stale and corrupted cookies create no candidate;
- per-source challenge limits suppress excess replies;
- one-row candidate pressure fails closed;
- candidate timeout returns capacity;
- malformed datagrams produce no response;
- the platform-native receive path routes unknown admission identically;
- challenge bytes never exceed their triggering hello bytes;
- a warmed 64-hello receive/challenge pass allocates 0 managed bytes on the host
  owner thread.

## Interpretation

This artifact is lifecycle/security evidence, not latency, CCU, public-WAN, or
authenticated-session evidence. H0.1 deliberately stops after cookie-validated
candidate admission. H0.2 must perform transactional ECDH/signature/protocol-peer
promotion through the existing secure protocol. N1 semantics, N0 selection, path
rebinding, connection-owner sharding, and N1.5 are unchanged.
