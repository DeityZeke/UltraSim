# Hosted Network H0.2 authenticated-promotion evidence

Captured: 2026-08-22
Checkpoint: H0.2 implementation stop
Scope: real IPv4 UDP sockets, external client, authenticated promotion and secure traffic

## Frozen bounds

| Item | Bound |
| --- | ---: |
| Candidate identity | 8 B (`slot + generation`) |
| Candidate client public key | 512 B maximum |
| Candidate server public key | 512 B maximum |
| Canonical transcript | 1,082 B maximum |
| Candidate master secret | 32 B |
| Cached handshake flight | one configured datagram |
| Default promotion candidates | 64 |
| Default secure server sessions | 256 |
| Default known admission + promotion-array payload | 298,208 B |

The payload figure excludes the shared listener reservation, CLR object/array
headers, socket buffers, opaque cryptography-provider state, and secure-peer
arrays created only for established sessions. The host snapshot reports active
peer reservations as sessions are established.

## Permanent acceptance

Debug and Release run these H0.2 gates in addition to every retained H0.1/N0 gate:

```text
HostedCandidateIdentityIsGenerationFencedAndFixedWidth
HostedPromotionPublishesOnlyAfterFinishAndCarriesSecureTraffic
HostedPromotionReusesImmutableCandidateAndRejectsCredentialReplacement
HostedPromotionFailureAndTimeoutReleaseAllConnectionState
HostedPromotionCapacityIsTransactionalAndReusable
HostedDuplicateClientFinishRecoversWithoutSecondSession
HostedPromotionNativeReceiveMatchesScalarLifecycle
HostedPromotionResamplesMonotonicTimeAfterInboundPublication
WarmedHostedSecureDeliveryAllocatesZero
HostedManifestCookieBindingAndDeploymentSignerLifetimeAreExact
```

The suite proves that cookie validation and key exchange publish no route; only a
valid `ClientFinish` publishes the exact path, established `ConnectionId`, and
secure server peer. Bidirectional application messages use the existing protocol.
Candidate reuse is generation-fenced, promotion credentials are immutable,
duplicate flights reuse cached work, invalid keys/proofs and capacity pressure
publish no partial session, timeouts and retirement reclaim connection capacity,
and the host never disposes its deployment-owned signer. Scalar and native receive
paths converge identically. One warmed secure delivery and application-consume
pass allocates 0 managed bytes on the owner thread.

## Interpretation

This is an authenticated external-process-capable transport-session gate, not a
Game Admission, account, participant, public-WAN, rebinding, owner-sharding, CCU,
or N1 semantic claim. H0.3 must earn endpoint rebinding independently. H0.4/H0.5,
N1.5, N0 deletion, and 2K/4K work remain unstarted.
