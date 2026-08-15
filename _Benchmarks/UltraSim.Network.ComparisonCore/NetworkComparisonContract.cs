namespace UltraSim.Network.Comparison;

public enum NetworkComparisonClass
{
    Direct,
    Provisional,
    Directional,
    Unsupported,
}

public sealed record NetworkEvidenceProfile(
    string WorkloadIdentity,
    string SemanticContract,
    string CapacityProfile,
    string ClockProfile,
    string ExecutionTopology,
    string AllocationScope)
{
    public NetworkEvidenceProfile Validate()
    {
        Require(WorkloadIdentity, nameof(WorkloadIdentity));
        Require(SemanticContract, nameof(SemanticContract));
        Require(CapacityProfile, nameof(CapacityProfile));
        Require(ClockProfile, nameof(ClockProfile));
        Require(ExecutionTopology, nameof(ExecutionTopology));
        Require(AllocationScope, nameof(AllocationScope));
        return this;
    }

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Evidence profile values must be nonempty.", name);
    }
}

public sealed record NetworkComparisonDecision(
    NetworkComparisonClass Classification,
    bool SemanticallyComparable,
    bool MayReportHeadlineRatio,
    string Reason);

public static class NetworkComparisonContract
{
    public static NetworkComparisonDecision Evaluate(
        NetworkEvidenceProfile left,
        NetworkEvidenceProfile right,
        NetworkComparisonClass requestedClassification)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        left.Validate();
        right.Validate();

        if (requestedClassification == NetworkComparisonClass.Unsupported)
            return new(requestedClassification, false, false,
                "The declared contract has no honest equivalent; report capability only and no ratio.");

        string? mismatch = FindMismatch(left, right,
            permitTopologyDifference: requestedClassification == NetworkComparisonClass.Provisional);
        if (mismatch is not null)
            return new(NetworkComparisonClass.Unsupported, false, false,
                $"Evidence profiles differ in {mismatch}; a relative headline is prohibited.");

        return requestedClassification switch
        {
            NetworkComparisonClass.Direct => new(requestedClassification, true, true,
                "Equivalent semantic work and measurement profiles admit a direct relative result."),
            NetworkComparisonClass.Provisional => new(requestedClassification, true, true,
                "Equivalent observable work admits a provisional relative result while transport topology differs."),
            NetworkComparisonClass.Directional => new(requestedClassification, true, false,
                "Directional evidence may be reported side by side but not as a headline ratio."),
            _ => throw new ArgumentOutOfRangeException(nameof(requestedClassification)),
        };
    }

    private static string? FindMismatch(
        NetworkEvidenceProfile left,
        NetworkEvidenceProfile right,
        bool permitTopologyDifference)
    {
        if (!StringComparer.Ordinal.Equals(left.WorkloadIdentity, right.WorkloadIdentity)) return "workload identity";
        if (!StringComparer.Ordinal.Equals(left.SemanticContract, right.SemanticContract)) return "semantic contract";
        if (!StringComparer.Ordinal.Equals(left.CapacityProfile, right.CapacityProfile)) return "capacity profile";
        if (!StringComparer.Ordinal.Equals(left.ClockProfile, right.ClockProfile)) return "clock profile";
        if (!permitTopologyDifference &&
            !StringComparer.Ordinal.Equals(left.ExecutionTopology, right.ExecutionTopology))
            return "execution topology";
        if (!StringComparer.Ordinal.Equals(left.AllocationScope, right.AllocationScope)) return "allocation scope";
        return null;
    }
}
