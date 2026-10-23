namespace BulkBackup.Core.Services;

/// <summary>Per-run inventory with indexed IP/name lookups. Never merges distinct IPs by hostname.</summary>
internal sealed class DiscoveryTargets
{
    private readonly object _gate = new();
    private readonly Dictionary<string, NetworkEngine.DiscoveredTarget> _targets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _names = new(StringComparer.OrdinalIgnoreCase);
    public int Count { get { lock (_gate) return _targets.Count; } }
    public NetworkEngine.DiscoveredTarget[] Snapshot() { lock (_gate) return _targets.Values.ToArray(); }

    private void Index(string key, string name)
    {
        string host = HostKey.Of(name);
        if (host.Length == 0) return;
        if (!_names.TryGetValue(host, out var keys)) _names[host] = keys = new(StringComparer.OrdinalIgnoreCase);
        keys.Add(key);
    }

    private void Unindex(string key, string name)
    {
        string host = HostKey.Of(name);
        if (!_names.TryGetValue(host, out var keys)) return;
        keys.Remove(key);
        if (keys.Count == 0) _names.Remove(host);
    }

    public void UpdateHostname(string ip, string hostname)
    {
        lock (_gate)
        {
            if (!_targets.TryGetValue(ip, out var target) ||
                (!string.IsNullOrWhiteSpace(target.Hostname) && target.Hostname != ip)) return;
            Unindex(ip, target.Hostname);
            target.Hostname = hostname;
            Index(ip, hostname);
        }
    }

    public bool AddOrMerge(NetworkEngine.DiscoveredTarget target)
    {
        lock (_gate)
        {
            bool hasIp = !string.IsNullOrWhiteSpace(target.IpAddress) && !target.IpAddress.StartsWith("placeholder:", StringComparison.OrdinalIgnoreCase);
            string host = HostKey.Of(target.Hostname);
            string key = hasIp ? target.IpAddress : $"placeholder:{host}";
            if (!hasIp && _names.TryGetValue(host, out var known) && known.Count == 1)
                key = known.First();
            bool newAddress = false;
            if (!_targets.TryGetValue(key, out var existing) && hasIp &&
                _targets.Remove($"placeholder:{host}", out existing))
            {
                Unindex($"placeholder:{host}", existing.Hostname);
                existing.IpAddress = target.IpAddress;
                _targets[key] = existing;
                Index(key, existing.Hostname);
                newAddress = true;
            }
            if (existing == null)
            {
                if (!hasIp) target.IpAddress = key;
                _targets.Add(key, target);
                Index(key, target.Hostname);
                return true;
            }
            if (string.IsNullOrWhiteSpace(existing.Hostname) || existing.Hostname == existing.IpAddress)
            {
                Unindex(key, existing.Hostname);
                existing.Hostname = target.Hostname;
                Index(key, existing.Hostname);
            }
            if (string.IsNullOrWhiteSpace(existing.UplinkDevice) ||
                (existing.UplinkDevice.StartsWith("Core", StringComparison.OrdinalIgnoreCase) &&
                 !target.UplinkDevice.StartsWith("Core", StringComparison.OrdinalIgnoreCase)))
            {
                existing.UplinkDevice = target.UplinkDevice;
                existing.UplinkIp = target.UplinkIp;
            }
            if (string.IsNullOrWhiteSpace(existing.LocalPort)) existing.LocalPort = target.LocalPort;
            if (string.IsNullOrWhiteSpace(existing.RemotePort)) existing.RemotePort = target.RemotePort;
            existing.IsAp |= target.IsAp;
            return newAddress;
        }
    }
}
