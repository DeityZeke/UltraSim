using UltraSim.Client.BuiltInTestWorlds;
using UltraSim.Client.Content;

var tests = new (string Name, Action Body)[]
{
    ("FrozenFingerprintsAreDeterministic", FrozenFingerprintsAreDeterministic),
    ("DefaultOwnsExactCabinDefinitions", DefaultOwnsExactCabinDefinitions),
    ("ReplaceChangesOnlyWallAndDependentResolution", ReplaceChangesOnlyWallAndDependentResolution),
    ("AddPreservesBaseAndAddsExactNamespace", AddPreservesBaseAndAddsExactNamespace),
    ("LocalPackagesAreExactVerifiedPayloadSets", LocalPackagesAreExactVerifiedPayloadSets),
    ("MissingRequiredVariantIsDiagnosableAndTransactional", MissingRequiredVariantIsDiagnosableAndTransactional),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"All {tests.Length} built-in Test World content tests passed.");

static void FrozenFingerprintsAreDeterministic()
{
    CabinContentOracle first = CabinContentOracle.Create();
    CabinContentOracle second = CabinContentOracle.Create();

    Equal(first.BaseCatalog.Fingerprint, second.BaseCatalog.Fingerprint);
    Equal(first.Default.Catalog.Fingerprint, second.Default.Catalog.Fingerprint);
    Equal(first.Replace.Catalog.Fingerprint, second.Replace.Catalog.Fingerprint);
    Equal(first.Add.Catalog.Fingerprint, second.Add.Catalog.Fingerprint);
    NotEqual(first.Default.Catalog.Generation, second.Default.Catalog.Generation);
    NotEqual(first.Replace.Catalog.Generation, second.Replace.Catalog.Generation);
    NotEqual(first.Add.Catalog.Generation, second.Add.Catalog.Generation);

    Console.WriteLine($"ORACLE base={first.BaseCatalog.Fingerprint} default={first.Default.Catalog.Fingerprint} " +
        $"replace={first.Replace.Catalog.Fingerprint} add={first.Add.Catalog.Fingerprint}");

    Equal(CabinContentFingerprints.BaseCatalog, first.BaseCatalog.Fingerprint.ToString());
    Equal(CabinContentFingerprints.DefaultCatalog, first.Default.Catalog.Fingerprint.ToString());
    Equal(CabinContentFingerprints.ReplaceCatalog, first.Replace.Catalog.Fingerprint.ToString());
    Equal(CabinContentFingerprints.AddCatalog, first.Add.Catalog.Fingerprint.ToString());

    foreach (CabinResolvedContent content in Variants(first))
    {
        Equal(CabinContentOracle.FixtureRealm, content.Plan.Realm);
        Equal(first.BaseCatalog.Fingerprint, content.Plan.RequiredBaseCatalogFingerprint);
        Equal(content.Plan.PlanHash, content.Catalog.PlanHash);
        Equal(content.Plan.Activation, content.Catalog.Activation);
    }
    NotEqual(first.Default.Catalog.Fingerprint, first.Replace.Catalog.Fingerprint);
    NotEqual(first.Default.Catalog.Fingerprint, first.Add.Catalog.Fingerprint);
    NotEqual(first.Replace.Catalog.Fingerprint, first.Add.Catalog.Fingerprint);

}

static void DefaultOwnsExactCabinDefinitions()
{
    CabinContentOracle oracle = CabinContentOracle.Create();
    ResolvedContentCatalog catalog = oracle.Default.Catalog;
    LogicalContentId[] expected =
    [
        CabinContentIds.ChestIcon,
        CabinContentIds.TestWorldIcon,
        CabinContentIds.WallMaterial,
        CabinContentIds.WoodMaterial,
        CabinContentIds.WallTexture,
        CabinContentIds.WoodTexture,
    ];
    Array.Sort(expected);
    SequenceEqual(expected, catalog.Entries.Select(static entry => entry.Id).ToArray());
    Equal(0, oracle.Default.Plan.Packages.Count);
    Equal(oracle.BaseCatalog.Fingerprint, catalog.RequiredBaseCatalogFingerprint);
    foreach (ResolvedContentEntry entry in catalog.Entries)
    {
        Equal(oracle.BasePackage.Manifest.PackageId, entry.OwnerPackageId);
        Equal(oracle.BasePackage.Manifest.ManifestHash, entry.OwnerManifestHash);
    }
}

static void ReplaceChangesOnlyWallAndDependentResolution()
{
    CabinContentOracle oracle = CabinContentOracle.Create();
    ResolvedContentCatalog baseline = oracle.Default.Catalog;
    ResolvedContentCatalog replaced = oracle.Replace.Catalog;
    Equal(baseline.Entries.Count, replaced.Entries.Count);
    Equal(1, oracle.Replace.Plan.Packages.Count);

    foreach (ResolvedContentEntry before in baseline.Entries)
    {
        True(replaced.TryGet(before.Id, out ResolvedContentEntry? after));
        if (before.Id == CabinContentIds.WallTexture)
        {
            Equal(oracle.ReplacePackage.Manifest.PackageId, after!.OwnerPackageId);
            Equal(oracle.ReplacePackage.Manifest.ManifestHash, after.OwnerManifestHash);
            NotEqual(before.Definition.DefinitionHash, after.Definition.DefinitionHash);
            NotEqual(before.ReferencedFiles.Single(), after.ReferencedFiles.Single());
        }
        else if (before.Id == CabinContentIds.WallMaterial)
        {
            Equal(before.OwnerPackageId, after!.OwnerPackageId);
            Equal(before.OwnerManifestHash, after.OwnerManifestHash);
            Equal(before.Definition.DefinitionHash, after.Definition.DefinitionHash);
            NotEqual(before.ReferencedFiles.Single(), after.ReferencedFiles.Single());
            Equal(oracle.ReplacePackage.Manifest.Files.Single().Sha256, after.ReferencedFiles.Single());
        }
        else
        {
            EntryEqual(before, after!);
        }
    }
}

static void AddPreservesBaseAndAddsExactNamespace()
{
    CabinContentOracle oracle = CabinContentOracle.Create();
    ResolvedContentCatalog baseline = oracle.Default.Catalog;
    ResolvedContentCatalog added = oracle.Add.Catalog;
    Equal(baseline.Entries.Count + 3, added.Entries.Count);
    Equal(1, oracle.Add.Plan.Packages.Count);

    foreach (ResolvedContentEntry before in baseline.Entries)
    {
        True(added.TryGet(before.Id, out ResolvedContentEntry? after));
        EntryEqual(before, after!);
    }

    LogicalContentId[] expectedAdded =
    [
        CabinContentIds.BlueGlowMaterial,
        CabinContentIds.BlueGlowTexture,
        CabinContentIds.StreamedBadgeIcon,
    ];
    Array.Sort(expectedAdded);
    LogicalContentId[] actualAdded = added.Entries
        .Where(entry => entry.OwnerPackageId == oracle.AddPackage.Manifest.PackageId)
        .Select(static entry => entry.Id)
        .ToArray();
    SequenceEqual(expectedAdded, actualAdded);
    foreach (LogicalContentId id in expectedAdded)
    {
        Equal("test-cabin", id.Namespace.Value!);
        True(added.TryGet(id, out ResolvedContentEntry? entry));
        Equal(oracle.AddPackage.Manifest.ManifestHash, entry!.OwnerManifestHash);
    }
}

static void LocalPackagesAreExactVerifiedPayloadSets()
{
    CabinContentOracle oracle = CabinContentOracle.Create();
    foreach (CabinLocalContentPackage package in Packages(oracle))
    {
        Equal(package.Manifest.Files.Count, package.Payloads.Count);
        Equal(package.Manifest.PackageId, package.Acquisition.Package);
        Equal(package.Manifest.ManifestHash, package.Acquisition.ManifestHash);
        Equal(1, package.Acquisition.Sources.Count);
        Equal(ContentSourceKind.Local, package.Acquisition.Sources[0].Kind);
        True(package.Acquisition.Sources[0].IsValid);
        foreach (ContentFileDescriptor file in package.Manifest.Files)
        {
            True(package.TryGetPayload(file.Path, out ReadOnlyMemory<byte> payload));
            Equal(file.SizeBytes, payload.Length);
            Equal(file.Sha256, ContentHash.Compute(payload.Span));
            True(IsCompletePng(payload.Span));
        }
        True(ContentPackageValidator.TryValidatePayloads(
            package.Manifest, package.Payloads, out VerifiedContentPackage? verified, out string? error), error);
        Equal(package.Manifest.ManifestHash, verified!.ManifestHash);
    }
}

static void MissingRequiredVariantIsDiagnosableAndTransactional()
{
    CabinContentOracle oracle = CabinContentOracle.Create();
    ContentHash defaultFingerprint = oracle.Default.Catalog.Fingerprint;
    False(ContentCatalogResolver.TryResolve(
        oracle.BaseCatalog,
        oracle.Replace.Plan,
        [],
        out ResolvedContentCatalog? missingReplace,
        out ContentCatalogResolutionError replaceError));
    Null(missingReplace);
    Equal(ContentCatalogResolutionError.MissingRequiredPackage, replaceError);
    False(ContentCatalogResolver.TryResolve(
        oracle.BaseCatalog,
        oracle.Add.Plan,
        [],
        out ResolvedContentCatalog? missingAdd,
        out ContentCatalogResolutionError addError));
    Null(missingAdd);
    Equal(ContentCatalogResolutionError.MissingRequiredPackage, addError);
    Equal(defaultFingerprint, oracle.Default.Catalog.Fingerprint);
    Equal(oracle.BaseCatalog.Fingerprint, oracle.Default.Catalog.RequiredBaseCatalogFingerprint);
}

static IEnumerable<CabinResolvedContent> Variants(CabinContentOracle oracle)
{
    yield return oracle.Default;
    yield return oracle.Replace;
    yield return oracle.Add;
}

static IEnumerable<CabinLocalContentPackage> Packages(CabinContentOracle oracle)
{
    yield return oracle.BasePackage;
    yield return oracle.ReplacePackage;
    yield return oracle.AddPackage;
}

static bool IsCompletePng(ReadOnlySpan<byte> bytes) =>
    bytes.Length >= 45 &&
    bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
    bytes[^8..^4].SequenceEqual("IEND"u8);

static void EntryEqual(ResolvedContentEntry expected, ResolvedContentEntry actual)
{
    Equal(expected.Id, actual.Id);
    Equal(expected.Kind, actual.Kind);
    Equal(expected.OwnerPackageId, actual.OwnerPackageId);
    Equal(expected.OwnerManifestHash, actual.OwnerManifestHash);
    Equal(expected.Definition.DefinitionHash, actual.Definition.DefinitionHash);
    SequenceEqual(expected.ReferencedFiles.ToArray(), actual.ReferencedFiles.ToArray());
}

static void True(bool value, string? message = null)
{
    if (!value) throw new InvalidOperationException(message ?? "Expected true.");
}

static void False(bool value)
{
    if (value) throw new InvalidOperationException("Expected false.");
}

static void Null(object? value)
{
    if (value is not null) throw new InvalidOperationException("Expected null.");
}

static void Equal<T>(T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', actual '{actual}'.");
}

static void NotEqual<T>(T left, T right) where T : notnull
{
    if (EqualityComparer<T>.Default.Equals(left, right))
        throw new InvalidOperationException($"Expected values to differ, both were '{left}'.");
}

static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual) where T : notnull
{
    if (expected.Count != actual.Count)
        throw new InvalidOperationException($"Expected {expected.Count} values, actual {actual.Count}.");
    for (int index = 0; index < expected.Count; index++)
        Equal(expected[index]!, actual[index]!);
}
