# Hosted H0.3 terminal-flight hardening — 2026-08-22

This immutable follow-up preserves the accepted H0.2 commit-before-ack ordering
after H0.3 transfers terminal-flight ownership into the established hosted
session.

The first `ServerFinish` is sent from the session-owned cached flight after local
publication. It does not read the cleared ownership-transfer local, and it does
not require a duplicate `ClientFinish` to recover ordinary establishment.

Permanent acceptance:

```text
HostedFirstServerFinishIsValidWithoutClientRetry
```

The regression uses one raw external UDP client, completes the authenticated
key exchange, sends exactly one valid `ClientFinish`, and requires the immediate
response to decode as the canonical `ServerFinish` with the correct proof.
Server evidence remains exactly one established promotion, one terminal flight,
and zero duplicate client finishes.

Debug Network verification passed with all H0.1-H0.3 tests. This artifact records
the narrow terminal-flight correction only; it does not revise the immutable
H0.3 implementation-stop evidence or begin H0.4.
