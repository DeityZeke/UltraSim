using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>Shared cold construction and validation for generated query-plan families.</summary>
internal static class ArchetypeQueryPlanUtilities
{
    internal static ComponentId[] MergeRequiredComponents(
        ReadOnlySpan<ComponentId> accessComponents,
        ReadOnlySpan<ComponentId> filterRequired)
    {
        var combined = new ComponentId[filterRequired.Length + accessComponents.Length];
        filterRequired.CopyTo(combined);
        accessComponents.CopyTo(combined.AsSpan(filterRequired.Length));
        Array.Sort(combined);

        int uniqueCount = 0;
        for (int index = 0; index < combined.Length; index++)
        {
            if (uniqueCount == 0 || combined[uniqueCount - 1] != combined[index])
                combined[uniqueCount++] = combined[index];
        }
        if (uniqueCount == combined.Length)
            return combined;

        var normalized = new ComponentId[uniqueCount];
        combined.AsSpan(0, uniqueCount).CopyTo(normalized);
        return normalized;
    }

    internal static bool SignatureMatches(
        ReadOnlySpan<ComponentId> signature,
        ReadOnlySpan<ComponentId> required,
        ReadOnlySpan<ComponentId> excluded) =>
        ContainsAll(signature, required) && !ContainsAny(signature, excluded);

    internal static void ValidateColumn<T>(
        in ComponentColumnLayout column,
        ComponentId componentId,
        ComponentFlags flags)
        where T : unmanaged
    {
        if (column.ComponentId != componentId ||
            column.Flags != flags ||
            !column.HasData ||
            column.ElementSizeBytes != Unsafe.SizeOf<T>())
        {
            ThrowQueryInvariant("Generated query metadata and archetype storage disagree.");
        }
    }

    internal static void ValidateTrackedColumn(
        in ComponentColumnLayout column,
        ComponentId componentId,
        ComponentFlags flags)
    {
        if (column.ComponentId != componentId || column.Flags != flags)
            ThrowQueryInvariant("Tracked component metadata and archetype storage disagree.");
    }

    private static bool ContainsAll(
        ReadOnlySpan<ComponentId> signature,
        ReadOnlySpan<ComponentId> required)
    {
        int signatureIndex = 0;
        int requiredIndex = 0;
        while (signatureIndex < signature.Length && requiredIndex < required.Length)
        {
            int comparison = signature[signatureIndex].CompareTo(required[requiredIndex]);
            if (comparison == 0)
            {
                signatureIndex++;
                requiredIndex++;
            }
            else if (comparison < 0)
            {
                signatureIndex++;
            }
            else
            {
                return false;
            }
        }
        return requiredIndex == required.Length;
    }

    private static bool ContainsAny(
        ReadOnlySpan<ComponentId> signature,
        ReadOnlySpan<ComponentId> excluded)
    {
        int signatureIndex = 0;
        int excludedIndex = 0;
        while (signatureIndex < signature.Length && excludedIndex < excluded.Length)
        {
            int comparison = signature[signatureIndex].CompareTo(excluded[excludedIndex]);
            if (comparison == 0)
                return true;
            if (comparison < 0)
                signatureIndex++;
            else
                excludedIndex++;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowQueryInvariant(string message) =>
        throw new InvalidOperationException(message);
}
