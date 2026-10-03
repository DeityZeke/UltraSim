using System.Text.Json;
using Microsoft.Diagnostics.Tracing.Etlx;

if (args.Length != 1)
    throw new ArgumentException("Pass one complete .nettrace path.");

string tracePath = Path.GetFullPath(args[0]);
string etlxPath = TraceLog.CreateFromEventPipeDataFile(tracePath);
using TraceLog log = new(etlxPath);
var byType = new Dictionary<string, long>(StringComparer.Ordinal);
var byLeaf = new Dictionary<string, long>(StringComparer.Ordinal);
var byApplicationFrame = new Dictionary<string, long>(StringComparer.Ordinal);
var byApplicationAndType = new Dictionary<string, long>(StringComparer.Ordinal);
long totalBytes = 0;
long ticks = 0;
long missingStacks = 0;
foreach (var record in log.Events)
{
    if (!record.EventName.Contains("AllocationTick", StringComparison.Ordinal))
        continue;
    object? amountValue = record.PayloadByName("AllocationAmount64") ??
        record.PayloadByName("AllocationAmount");
    long amount = Convert.ToInt64(amountValue);
    if (amount <= 0) continue;
    totalBytes += amount;
    ticks++;
    string type = record.PayloadByName("TypeName")?.ToString() ?? "<unknown>";
    Add(byType, type, amount);
    var stack = record.CallStack();
    if (stack is null)
    {
        missingStacks++;
        continue;
    }
    string leaf = stack.CodeAddress.FullMethodName;
    Add(byLeaf, leaf, amount);
    for (var frame = stack; frame is not null; frame = frame.Caller)
    {
        string method = frame.CodeAddress.FullMethodName;
        if (method.Contains("UltraSim.", StringComparison.Ordinal))
        {
            Add(byApplicationFrame, method, amount);
            Add(byApplicationAndType, $"{method} -> {type}", amount);
            break;
        }
    }
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    Format = "UltraSimClientAllocationTraceSummary/v1",
    TracePath = Path.GetFullPath(args[0]),
    Ticks = ticks,
    SampledBytes = totalBytes,
    MissingStacks = missingStacks,
    TopTypes = Top(byType),
    TopLeaves = Top(byLeaf),
    TopApplicationFrames = Top(byApplicationFrame),
    TopApplicationAndTypes = Top(byApplicationAndType),
}, new JsonSerializerOptions { WriteIndented = true }));

static void Add(Dictionary<string, long> totals, string key, long amount) =>
    totals[key] = totals.GetValueOrDefault(key) + amount;

static object[] Top(Dictionary<string, long> totals) => totals
    .OrderByDescending(pair => pair.Value).Take(30)
    .Select(pair => (object)new { Name = pair.Key, SampledBytes = pair.Value })
    .ToArray();
