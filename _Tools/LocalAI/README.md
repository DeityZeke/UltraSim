# UltraSim Local Qwen Reviews

This directory provides a controlled local-review workflow for the Ollama
`qwen3:14b` model. The model is a critic and second opinion, not an authority and
not an autonomous shell operator. Every finding must be checked against source,
tests, benchmarks, traces, or profiling before it changes UltraSim.

## Create the project model

From the repository root:

```bash
ollama create ultrasim-qwen -f _Tools/LocalAI/Modelfile.ultrasim-qwen
```

The default context is `16384`, selected as a starting point for the local
16-GB GPU. Test longer contexts before adopting them: a larger KV cache can
reduce GPU residency and throughput.

## Run focused reviews

```bash
_Tools/LocalAI/qwen-review.sh ecs \
  Kernels/ECS/Entities/Entity.cs \
  Kernels/ECS/Entities/EntityPool.cs

_Tools/LocalAI/qwen-review.sh benchmark \
  _Benchmarks/UltraSim.ECS.Benchmarks/Program.cs

git diff -- Kernels/ECS _Tests/UltraSim.ECS.Tests |
  _Tools/LocalAI/qwen-review.sh patch
```

The wrapper rejects inputs over `60000` bytes by default instead of silently
truncating them. Submit coherent slices and preserve room for the answer.

Environment overrides:

- `ULTRASIM_QWEN_MODEL`: model name; default `ultrasim-qwen`.
- `ULTRASIM_QWEN_CONTEXT`: request context; default `16384`.
- `ULTRASIM_QWEN_MAX_INPUT_BYTES`: explicit input limit; default `60000`.
- `OLLAMA_HOST`: Ollama API base URL; default `http://127.0.0.1:11434`.

Do not expose an unauthenticated Ollama endpoint to another network. Do not run
model-suggested commands without inspecting them. Keep proprietary source local.

## Review policy

Use Qwen when an independent pass is valuable: architecture decisions,
low-level layout, benchmark validity, concurrency, persistence boundaries,
network semantics, test gaps, or a nontrivial patch. Avoid invoking it for
mechanical edits where the extra review adds no signal.

Record only verified findings in design documents or implementation notes. When
Qwen and primary analysis disagree, add the smallest discriminating test or
benchmark instead of deciding by confidence or prose quality.
