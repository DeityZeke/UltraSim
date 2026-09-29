# Framework save-set coordination

This BCL-only assembly owns the existing bounded crash-durable SaveSet transaction: common capture epoch, artifact hashing/flush, atomic complete-generation and `CURRENT` publication, staged Prepare → Resolve → Validate, and single-use Commit. It has no ECS project reference.

`PersistentReferenceKey(domain, scope, local)` is a narrow neutral durable-reference seam. Prepared participants may claim a namespace, which fences fallback to an old/external generation; overlapping staged owners reject. The ECS-specific conversion from `PersistentEntityKey` lives in `Server/Persistence/EcsPersistentReference.cs`. Existing ECS/Game participants still own their schemas and compose through the same coordinator. Non-ECS Engine/Extension participants can do the same without importing Entity.

Recoverable participant-specific format/schema failures are supplied explicitly by the composition caller; the generic coordinator only classifies its own structural/IO failures. Unexpected implementation faults do not turn into compatible-recovery candidates. Save-set artifact format and on-disk layout are unchanged by this extraction.

The framework proof project references only Framework.Persistence and Framework.RuntimeHost. Its sample participant serializes owner, Extension type and instance identities for two same-type Extensions, stages them, rejects unsupported format without replacing live state, and recovers the previous complete generation.
