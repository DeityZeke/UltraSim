"""Summarize perf CPU leaf samples using a retained .NET perf map.

Usage: python3 PerfMapSamples.py <perf.data> <perf-PID.map> <start-s> <stop-s>
This is CPU-sample attribution, not wall-time or allocation attribution.
"""

import bisect
import collections
import re
import subprocess
import sys


perf_data, map_path, start_text, stop_text = sys.argv[1:]
start = float(start_text)
stop = float(stop_text)
addresses = []
ends = []
names = []
with open(map_path, encoding="utf-8") as perf_map:
    for line in perf_map:
        fields = line.rstrip().split(" ", 2)
        if len(fields) != 3:
            continue
        address = int(fields[0], 16)
        addresses.append(address)
        ends.append(address + int(fields[1], 16))
        names.append(fields[2])
ordered = sorted(zip(addresses, ends, names))
addresses, ends, names = map(list, zip(*ordered))

result = subprocess.run(
    ["perf", "script", "-i", perf_data, "-F", "comm,tid,time,event,ip,sym,dso",
     "-G", "--reltime"],
    check=True, capture_output=True, text=True,
)
pattern = re.compile(r"^\s*(.*?)\s+(\d+)\s+([0-9.]+): cycles:u:\s+([0-9a-f]+)\s+(.+?)\s+\((.+)\)$")
by_thread = collections.Counter()
by_symbol = collections.Counter()
by_half_second = collections.Counter()
examples = {}
unknown_jit = 0
for line in result.stdout.splitlines():
    match = pattern.match(line)
    if not match:
        continue
    command, thread_text, time_text, ip_text, symbol, dso = match.groups()
    time = float(time_text)
    if time < start or time >= stop:
        continue
    thread = (command.strip(), int(thread_text))
    by_thread[thread] += 1
    if "memfd:doublemapper" in dso:
        ip = int(ip_text, 16)
        index = bisect.bisect_right(addresses, ip) - 1
        if index >= 0 and ip < ends[index]:
            symbol = names[index]
        else:
            unknown_jit += 1
    elif symbol == "[unknown]":
        symbol = dso
    by_symbol[(thread, symbol)] += 1
    examples.setdefault((thread, symbol), (time, ip_text))
    by_half_second[(int(time * 2) / 2, command.strip())] += 1

print(f"interval={start:.3f}-{stop:.3f}s samples={sum(by_thread.values())} unknown_jit={unknown_jit}")
print("threads:")
for key, count in by_thread.most_common(15):
    print(f"  {count:5d} {key}")
print("top leaf symbols:")
for (thread, symbol), count in by_symbol.most_common(35):
    print(f"  {count:5d} {thread} {symbol} example={examples[(thread, symbol)]}")
print("half-second sample counts:")
for key, count in sorted(by_half_second.items()):
    print(f"  {key[0]:5.1f}s {key[1]} {count}")
