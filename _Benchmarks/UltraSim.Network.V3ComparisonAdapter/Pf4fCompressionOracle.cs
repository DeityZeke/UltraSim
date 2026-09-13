using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using UltraSim.Game.Protocol;

internal static class Pf4fCompressionOracle
{
    private const int Seed = 0x50463446;
    private const int PayloadsPerWorkload = 256;
    private const int WrapperBytes = 5;

    internal static void Run(string[] args)
    {
        string output = ReadText(args, "--output") ??
            "_Benchmarks/results/network-pf4f-compression-oracle.json";
        int rounds = ReadInt(args, "--oracle-rounds", 8);
        if (rounds < 3) throw new ArgumentOutOfRangeException(nameof(args));
        CanonicalSample[] corpus = BuildCorpus();
        var candidates = new[]
        {
            Evaluate("ultrasim-lz4-block-v1", corpus, rounds, Codec.Lz4Block),
            Evaluate("brotli-q1-w10", corpus, rounds, Codec.Brotli1),
            Evaluate("brotli-q4-w10", corpus, rounds, Codec.Brotli4),
            Evaluate("zlib-fastest", corpus, rounds, Codec.ZlibFastest),
        };
        CompressionCandidateReport selected = candidates[0];
        var thresholds = new List<CompressionThresholdReport>();
        foreach (int threshold in new[] { 128, 256, 384, 512, 768, 1_024 })
        foreach (int minimumSavings in new[] { 1, 16, 32, 64, 128 })
            thresholds.Add(EvaluatePolicy(corpus, threshold, minimumSavings));
        var workloads = corpus.GroupBy(sample => sample.Workload, StringComparer.Ordinal)
            .Select(group => new CompressionCorpusWorkload(
                group.Key, group.Count(), group.Sum(sample => sample.Bytes.Length),
                group.Min(sample => sample.Bytes.Length),
                group.Max(sample => sample.Bytes.Length),
                group.Sum(sample => sample.EntryCount),
                group.Sum(sample => sample.RecordCount)))
            .ToArray();
        byte[] hashInput = new byte[corpus.Sum(sample => sample.Bytes.Length)];
        int offset = 0;
        foreach (CanonicalSample sample in corpus)
        {
            sample.Bytes.CopyTo(hashInput, offset);
            offset += sample.Bytes.Length;
        }
        string corpusHash = Convert.ToHexString(SHA256.HashData(hashInput));
        var report = new Pf4fCompressionOracleReport(
            "ultrasim-network-pf4f-compression-oracle-v1",
            DateTimeOffset.UtcNow, ReadGitSha(), Environment.Version.ToString(),
            Environment.OSVersion.VersionString, Stopwatch.Frequency, Seed,
            WrapperBytes, corpus.Length, corpusHash, workloads, candidates,
            thresholds.ToArray(),
            "The deterministic cold corpus models canonical message-129 shapes for Moving, " +
            "Crowd, mixed Chaos, and the production-shaped rated fixture. Actual product A/B " +
            "remains authoritative for live workload ratios.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        foreach (CompressionCandidateReport candidate in candidates)
            Console.WriteLine($"PF4F ORACLE {candidate.Codec}: ratio={candidate.CompleteRatio:F3} " +
                $"profitable={candidate.ProfitableMessages}/{candidate.Messages} " +
                $"encode-p95={candidate.EncodeNanoseconds.P95:F1}ns " +
                $"decode-p95={candidate.DecodeNanoseconds.P95:F1}ns " +
                $"alloc={candidate.EncodeAllocatedBytes + candidate.DecodeAllocatedBytes}");
        CompressionThresholdReport preferred = thresholds.Single(value =>
            value.MinimumCanonicalBytes == 384 && value.MinimumNetSavingsBytes == 32);
        Console.WriteLine($"PF4F ORACLE policy>=384/save>=32: " +
            $"hits={preferred.SelectedMessages}/{preferred.Messages} " +
            $"ratio={preferred.SelectedCompleteRatio:F3} " +
            $"saved={preferred.NetBytesSaved:N0}");
        Console.WriteLine(Path.GetFullPath(output));
    }

    private static CompressionCandidateReport Evaluate(
        string name,
        CanonicalSample[] corpus,
        int rounds,
        Codec codec)
    {
        var encoded = new byte[corpus.Length][];
        var encodeTimes = new List<double>(checked(corpus.Length * rounds));
        var decodeTimes = new List<double>(checked(corpus.Length * rounds));
        long encodeAllocation = 0;
        long decodeAllocation = 0;
        ushort[] hashScratch = new ushort[
            UltraSimGameProtocol.DirtyChunkBundleCompressionHashEntries];
        for (int round = 0; round <= rounds; round++)
        for (int index = 0; index < corpus.Length; index++)
        {
            CanonicalSample sample = corpus[index];
            byte[] destination = new byte[UltraSimGameProtocol.
                MaximumCompressedDirtyChunkBundleDecodedBytes];
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            int written = Compress(codec, sample.Bytes, destination, hashScratch);
            long elapsed = Stopwatch.GetTimestamp() - started;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            if (written <= 0) throw new InvalidOperationException($"{name} failed compression.");
            byte[] compressed = destination.AsSpan(0, written).ToArray();
            byte[] decoded = new byte[sample.Bytes.Length];
            allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            started = Stopwatch.GetTimestamp();
            int decodedBytes = Decompress(codec, compressed, decoded);
            long decodeElapsed = Stopwatch.GetTimestamp() - started;
            long decodeAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            if (decodedBytes != sample.Bytes.Length || !decoded.AsSpan().SequenceEqual(sample.Bytes))
                throw new InvalidOperationException($"{name} did not recover canonical bytes.");
            if (round == 0)
            {
                encoded[index] = compressed;
                continue;
            }
            encodeTimes.Add(ToNanoseconds(elapsed));
            decodeTimes.Add(ToNanoseconds(decodeElapsed));
            encodeAllocation = checked(encodeAllocation + Math.Max(0, allocated));
            decodeAllocation = checked(decodeAllocation + Math.Max(0, decodeAllocated));
        }
        int addedWrapperBytes = codec == Codec.Lz4Block ? 0 : WrapperBytes;
        long canonicalBytes = corpus.Sum(sample => (long)sample.Bytes.Length);
        long completeBytes = encoded.Sum(value => checked((long)value.Length + addedWrapperBytes));
        int profitable = 0;
        long profitableCanonical = 0;
        long profitableComplete = 0;
        for (int index = 0; index < corpus.Length; index++)
            if (encoded[index].Length + addedWrapperBytes < corpus[index].Bytes.Length)
            {
                profitable++;
                profitableCanonical += corpus[index].Bytes.Length;
                profitableComplete += encoded[index].Length + addedWrapperBytes;
            }
        return new(name, corpus.Length, canonicalBytes, completeBytes,
            canonicalBytes - completeBytes, completeBytes / (double)canonicalBytes,
            profitable, profitable / (double)corpus.Length * 100d,
            profitableCanonical == 0 ? 1d : profitableComplete / (double)profitableCanonical,
            Metric(encodeTimes), Metric(decodeTimes), encodeAllocation, decodeAllocation,
            codec switch
            {
                Codec.Lz4Block => "UltraSim bounded LZ4-block-compatible representation; " +
                    "4,096-entry caller-owned hash scratch; independent block per message",
                Codec.Brotli1 or Codec.Brotli4 =>
                    "System.IO.Compression.BrotliEncoder span API; independent stream per message",
                _ => "System.IO.Compression.ZLibStream CompressionLevel.Fastest; cold reference only",
            });
    }

    private static CompressionThresholdReport EvaluatePolicy(
        CanonicalSample[] corpus,
        int minimumBytes,
        int minimumSavings)
    {
        byte[] scratch = new byte[UltraSimGameProtocol.MaximumCompressedDirtyChunkBundleDecodedBytes];
        ushort[] hashScratch = new ushort[
            UltraSimGameProtocol.DirtyChunkBundleCompressionHashEntries];
        long attemptedBytes = 0;
        long selectedCanonical = 0;
        long selectedComplete = 0;
        int attempts = 0;
        int selected = 0;
        foreach (CanonicalSample sample in corpus)
        {
            if (sample.Bytes.Length < minimumBytes) continue;
            attempts++;
            attemptedBytes += sample.Bytes.Length;
            if (!UltraSimGameProtocol.TryWriteCompressedDirtyChunkBundle(
                    scratch, sample.Bytes, hashScratch, out int written))
                continue;
            if (sample.Bytes.Length - written < minimumSavings) continue;
            selected++;
            selectedCanonical += sample.Bytes.Length;
            selectedComplete += written;
        }
        return new(minimumBytes, minimumSavings, corpus.Length, attempts, attemptedBytes,
            selected, selectedCanonical, selectedComplete,
            selectedCanonical - selectedComplete,
            selectedCanonical == 0 ? 1d : selectedComplete / (double)selectedCanonical);
    }

    private static CanonicalSample[] BuildCorpus()
    {
        var values = new List<CanonicalSample>(PayloadsPerWorkload * 4);
        var random = new Random(Seed);
        Add("moving", sample => (2 + sample % 5, 1 + sample % 3, false));
        Add("crowd", sample => (8 + sample % 5, 1 + sample % 2, false));
        Add("mixed-chaos", sample => (2 + sample % 9, 1 + sample % 4, sample % 7 == 0));
        Add("production-shaped", sample => (8, 2 + sample % 2, false));
        return values.ToArray();

        void Add(string workload, Func<int, (int Entries, int Records, bool Absence)> shape)
        {
            for (int sample = 0; sample < PayloadsPerWorkload; sample++)
            {
                (int desiredEntries, int recordsPerEntry, bool includeAbsence) = shape(sample);
                values.Add(CreateSample(
                    workload, sample, desiredEntries, recordsPerEntry, includeAbsence, random));
            }
        }
    }

    private static CanonicalSample CreateSample(
        string workload,
        int sample,
        int desiredEntries,
        int recordsPerEntry,
        bool includeAbsence,
        Random random)
    {
        var entries = new RemoteDirtyChunkBundleEntry[desiredEntries];
        var records = new RemoteChunkDeltaRecord[desiredEntries * recordsPerEntry];
        int entryCount = 0;
        int recordCount = 0;
        int expectedBytes = RemoteDirtyChunkBundleHeaderCodec.EncodedSize;
        ulong baseRevision = checked((ulong)sample + 10);
        for (int entry = 0; entry < desiredEntries; entry++)
        {
            bool absence = includeAbsence && entry == desiredEntries - 1;
            int count = absence ? 0 : recordsPerEntry;
            int entryBytes = UltraSimGameProtocol.GetDirtyChunkBundleEntryEncodedBytes(
                absence ? RemoteChunkPublicationKind.Absence : RemoteChunkPublicationKind.Delta,
                count);
            if (expectedBytes + entryBytes >
                UltraSimGameProtocol.MaximumCompressedDirtyChunkBundleDecodedBytes)
                break;
            entries[entryCount] = new RemoteDirtyChunkBundleEntry(
                absence ? RemoteChunkPublicationKind.Absence : RemoteChunkPublicationKind.Delta,
                checked((ulong)sample * 64UL + (ulong)entry + 1), 1,
                entry - desiredEntries / 2, sample % 3,
                absence ? 0 : baseRevision,
                absence ? 0 : baseRevision + 1,
                absence ? 0 : baseRevision,
                0, checked((ushort)count), absence ? 0 : recordCount);
            for (int record = 0; record < count; record++)
            {
                int value = random.Next(0, 32_000);
                records[recordCount++] = new RemoteChunkDeltaRecord(
                    RemoteChunkPublicOperation.Present,
                    record == 0 ? RemoteChunkPublicFieldMask.Complete :
                        RemoteChunkPublicFieldMask.Transform,
                    checked((uint)(record + 1)), checked((uint)(1 + sample % 3)),
                    checked((ulong)sample * 256UL + (ulong)entry * 8UL + (ulong)record + 1),
                    checked((uint)(1 + entry % 4)), checked((ushort)value),
                    checked((ushort)(value ^ 0x1555)), sample * 3,
                    checked((ushort)(sample + entry)), 1,
                    absence ? 0 : baseRevision + 1);
            }
            expectedBytes += entryBytes;
            entryCount++;
        }
        if (entryCount < 2) throw new InvalidOperationException("PF4F corpus lost bundle shape.");
        byte[] destination = new byte[
            UltraSimGameProtocol.MaximumCompressedDirtyChunkBundleDecodedBytes];
        if (!UltraSimGameProtocol.TryWriteDirtyChunkBundle(
                destination, 1, 2, checked((ulong)sample + 1),
                entries.AsSpan(0, entryCount), [], records.AsSpan(0, recordCount),
                out int written))
            throw new InvalidOperationException("PF4F corpus could not encode canonical 129.");
        return new(workload, entryCount, recordCount, destination.AsSpan(0, written).ToArray());
    }

    private static int Compress(
        Codec codec,
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        Span<ushort> hashScratch)
    {
        if (codec == Codec.Lz4Block)
        {
            if (!UltraSimGameProtocol.TryWriteCompressedDirtyChunkBundle(
                    destination, source, hashScratch, out int written))
                return 0;
            return written;
        }
        if (codec is Codec.Brotli1 or Codec.Brotli4)
        {
            var encoder = new BrotliEncoder(codec == Codec.Brotli1 ? 1 : 4, 10);
            try
            {
                OperationStatus status = encoder.Compress(source, destination,
                    out int consumed, out int written, true);
                return status == OperationStatus.Done && consumed == source.Length ? written : 0;
            }
            finally { encoder.Dispose(); }
        }
        using var output = new MemoryStream(destination.Length);
        using (var stream = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
            stream.Write(source);
        int length = checked((int)output.Length);
        if (length > destination.Length) return 0;
        output.GetBuffer().AsSpan(0, length).CopyTo(destination);
        return length;
    }

    private static int Decompress(Codec codec, ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (codec == Codec.Lz4Block)
            return UltraSimGameProtocol.TryReadCompressedDirtyChunkBundle(
                source, destination, out int written) ? written : 0;
        if (codec is Codec.Brotli1 or Codec.Brotli4)
        {
            var decoder = new BrotliDecoder();
            try
            {
                OperationStatus status = decoder.Decompress(
                    source, destination, out int consumed, out int written);
                return status == OperationStatus.Done && consumed == source.Length ? written : 0;
            }
            finally { decoder.Dispose(); }
        }
        using var input = new MemoryStream(source.ToArray(), writable: false);
        using var stream = new ZLibStream(input, CompressionMode.Decompress);
        int total = 0;
        while (total < destination.Length)
        {
            int read = stream.Read(destination[total..]);
            if (read == 0) break;
            total += read;
        }
        return stream.ReadByte() == -1 ? total : 0;
    }

    private static CompressionMetric Metric(List<double> values)
    {
        values.Sort();
        double Percentile(double fraction) => values[Math.Clamp(
            (int)Math.Ceiling(values.Count * fraction) - 1, 0, values.Count - 1)];
        return new(values.Count, values.Average(), Percentile(.5), Percentile(.95),
            Percentile(.99), values[^1]);
    }

    private static double ToNanoseconds(long ticks) =>
        ticks * 1_000_000_000d / Stopwatch.Frequency;
    private static string? ReadText(string[] args, string name)
    {
        string prefix = name + "=";
        string? value = args.FirstOrDefault(value =>
            value.StartsWith(prefix, StringComparison.Ordinal));
        return value is null ? null : value[prefix.Length..];
    }
    private static int ReadInt(string[] args, string name, int fallback) =>
        int.TryParse(ReadText(args, name), out int value) ? value : fallback;
    private static string ReadGitSha()
    {
        using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        string value = process?.StandardOutput.ReadToEnd().Trim() ?? "unknown";
        process?.WaitForExit();
        return process?.ExitCode == 0 ? value : "unknown";
    }

    private enum Codec : byte { Lz4Block, Brotli1, Brotli4, ZlibFastest }
    private sealed record CanonicalSample(string Workload, int EntryCount, int RecordCount, byte[] Bytes);
}

internal sealed record Pf4fCompressionOracleReport(
    string Schema,
    DateTimeOffset CapturedAtUtc,
    string GitSha,
    string Runtime,
    string OperatingSystem,
    long StopwatchFrequency,
    int Seed,
    int WrapperBytes,
    int Payloads,
    string CorpusSha256,
    CompressionCorpusWorkload[] Workloads,
    CompressionCandidateReport[] Candidates,
    CompressionThresholdReport[] Thresholds,
    string Scope);

internal sealed record CompressionCorpusWorkload(
    string Workload,
    int Messages,
    long CanonicalBytes,
    int MinimumBytes,
    int MaximumBytes,
    int Entries,
    int Records);

internal sealed record CompressionCandidateReport(
    string Codec,
    int Messages,
    long CanonicalBytes,
    long CompleteBytes,
    long NetBytesSaved,
    double CompleteRatio,
    int ProfitableMessages,
    double ProfitableMessagePercent,
    double ProfitableCompleteRatio,
    CompressionMetric EncodeNanoseconds,
    CompressionMetric DecodeNanoseconds,
    long EncodeAllocatedBytes,
    long DecodeAllocatedBytes,
    string Settings);

internal sealed record CompressionThresholdReport(
    int MinimumCanonicalBytes,
    int MinimumNetSavingsBytes,
    int Messages,
    int AttemptedMessages,
    long AttemptedCanonicalBytes,
    int SelectedMessages,
    long SelectedCanonicalBytes,
    long SelectedCompleteBytes,
    long NetBytesSaved,
    double SelectedCompleteRatio);

internal readonly record struct CompressionMetric(
    int Samples,
    double Mean,
    double P50,
    double P95,
    double P99,
    double Maximum);
