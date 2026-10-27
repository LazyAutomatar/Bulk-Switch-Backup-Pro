using System.Text.RegularExpressions;
using BulkBackup.Core.Models;

namespace BulkBackup.Core.Services;

internal static class CounterDiagnostics
{
    public static void Calculate(DeviceCapture device)
    {
        var samples = device.Commands.Where(c => c.SampleNumber != null).ToArray();
        if (samples.Length == 0) return;
        device.CounterInterpretation = "Lifetime counters show historical errors/discards. Rates describe only the observed sampling interval; they do not establish current packet-loss percentage or prove AP disconnections. Decreasing counters may indicate reset/wrap; no rate is inferred.";
        foreach (var group in samples.GroupBy(c => c.SampleGroup))
        {
            var first = group.FirstOrDefault(c => c.SampleNumber == 1);
            var second = group.FirstOrDefault(c => c.SampleNumber == 2);
            if (first?.Completeness != "complete" || second?.Completeness != "complete") continue;
            double interval = (second.CompletedAtUtc!.Value - first.CompletedAtUtc!.Value).TotalSeconds;
            if (interval <= 0) continue;
            var a = Parse(first.Attempts.Last().RawOutput);
            var b = Parse(second.Attempts.Last().RawOutput);
            foreach (var (key, before) in a)
            {
                if (!b.TryGetValue(key, out long after)) continue;
                long? delta = after >= before ? after - before : null;
                device.CounterRates.Add(new CounterRate(key.Port, key.Counter, before, after, interval,
                    delta, delta / interval, delta == null ? "resetOrWrap" : "observed"));
            }
        }
    }

    private static Dictionary<(string Port, string Counter), long> Parse(string text)
    {
        var result = new Dictionary<(string, string), long>();
        string[]? headers = null;
        foreach (string line in text.Split('\n'))
        {
            var fields = Regex.Split(line.Trim(), @"\s+");
            if (fields[0] is "Port" or "Interface") { headers = fields; continue; }
            if (headers == null || fields.Length != headers.Length || !Regex.IsMatch(fields[0], @"^[A-Za-z]+[\d/.:]+$")) continue;
            for (int i = 1; i < fields.Length; i++)
                if (long.TryParse(fields[i].Replace(",", ""), out long count) && count >= 0)
                    result[(fields[0], headers[i])] = count;
        }
        return result;
    }
}
