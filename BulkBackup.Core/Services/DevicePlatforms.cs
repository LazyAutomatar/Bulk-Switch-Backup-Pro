using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BulkBackup.Core.Services;

/// <summary>OS families, not vendor names, determine CLI dialect. Unknown devices fail closed in Auto mode.</summary>
public sealed record DevicePlatform(string Id, string Name, string Kind, string Paging,
    string Configuration, string[] Commands, bool UsesEnable = false, string? Save = null);

public static class DevicePlatforms
{
    private static DevicePlatform Switch(string id, string name, string paging, string config,
        string[] commands, bool enable = false, string? save = null) =>
        new(id, name, "Switch", paging, config, commands, enable, save);

    public static readonly DevicePlatform[] All =
    [
        Switch("ios", "Cisco IOS / IOS-XE", "terminal length 0", "show running-config", CommandProfiles.CiscoIosCommands, true, "write memory"),
        Switch("nxos", "Cisco NX-OS", "terminal length 0", "show running-config", CommandProfiles.CiscoNexusCommands, true, "copy running-config startup-config"),
        new("c9800", "Cisco Catalyst 9800 IOS-XE", "WLC", "terminal length 0", "show running-config", CommandProfiles.CiscoWlcCommands, true, "write memory"),
        new("aireos", "Cisco AireOS", "WLC", "config paging disable", "show run-config commands", CommandProfiles.CiscoAireOsWlcCommands),
        new("cisco-ap", "Cisco IOS Access Point", "AP", "terminal length 0", "show run", CommandProfiles.CiscoApCommands, true),
        Switch("cisco-sb", "Cisco Small Business / CBS", "terminal datadump", "show running-config", ["show version", "show system", "show inventory", "show interfaces status", "show vlan", "show cdp neighbors detail", "show cdp neighbors", "show lldp neighbors detail", "show running-config", "show startup-config"], true),
        Switch("arista", "Arista EOS", "terminal length 0", "show running-config", ["show version", "show inventory", "show interfaces status", "show vlan", "show port-channel summary", "show lldp neighbors detail", "show running-config", "show startup-config"], true, "write memory"),
        Switch("junos", "Juniper Junos", "set cli screen-length 0", "show configuration | no-more", ["show version", "show chassis hardware", "show chassis alarms", "show interfaces terse", "show vlans", "show lldp neighbors detail", "show configuration | no-more"]),
        Switch("aruba-cx", "Aruba AOS-CX", "no page", "show running-config", ["show version", "show system", "show interface brief", "show vlan", "show lldp neighbor-info", "show running-config", "show startup-config"], true, "write memory"),
        Switch("procurve", "Aruba / HP ProVision (AOS-S)", "no page", "show running-config", ["show version", "show system", "show interfaces brief", "show vlans", "show trunks", "show lldp info remote-device", "show running-config", "show config"], true, "write memory"),
        Switch("comware", "HPE / H3C Comware", "screen-length disable", "display current-configuration", ["display version", "display device", "display interface brief", "display vlan", "display link-aggregation summary", "display lldp neighbor-information verbose", "display current-configuration", "display saved-configuration"]),
        Switch("huawei", "Huawei VRP", "screen-length 0 temporary", "display current-configuration", ["display version", "display device", "display interface brief", "display vlan", "display eth-trunk", "display lldp neighbor brief", "display current-configuration", "display saved-configuration"]),
        Switch("dell-os10", "Dell SmartFabric OS10", "terminal length 0", "show running-configuration", ["show version", "show inventory", "show interface status", "show vlan", "show lldp neighbors", "show running-configuration", "show startup-configuration"], true),
        Switch("dell-os9", "Dell Networking OS9 / Force10", "terminal length 0", "show running-config", ["show version", "show inventory", "show interfaces status", "show vlan", "show lldp neighbors", "show running-config", "show startup-config"], true),
        Switch("dell-os6", "Dell N-Series OS6 / PowerConnect", "terminal length 0", "show running-config", ["show version", "show system", "show interfaces status", "show vlan", "show lldp remote-device all", "show running-config", "show startup-config"], true),
        Switch("extreme-exos", "Extreme EXOS / Switch Engine", "disable clipaging", "show configuration", ["show version", "show switch", "show ports information", "show vlan", "show lldp neighbors", "show configuration"]),
        Switch("extreme-voss", "Extreme VOSS / Fabric Engine", "terminal more disable", "show running-config", ["show sys-info", "show software", "show interfaces gigabitEthernet", "show vlan basic", "show lldp neighbor", "show running-config"], true),
        Switch("fastiron", "Ruckus / Brocade FastIron", "skip-page-display", "show running-config", ["show version", "show chassis", "show interfaces brief", "show vlan", "show lag", "show lldp neighbors detail", "show running-config", "show configuration"], true, "write memory"),
        Switch("alcatel", "Alcatel-Lucent OmniSwitch AOS", "no more", "show configuration snapshot", ["show microcode", "show chassis", "show interfaces status", "show vlan", "show lldp remote-system", "show configuration snapshot"]),
        Switch("fortiswitch", "Fortinet FortiSwitchOS", "", "show full-configuration", ["get system status", "get switch interface", "show full-configuration"]),
        Switch("routeros", "MikroTik RouterOS", "", "/export terse", ["/system resource print", "/system routerboard print", "/interface print detail without-paging", "/export terse"]),
        Switch("netgear", "NETGEAR managed / FASTPATH", "terminal length 0", "show running-config", ["show version", "show sysinfo", "show port all", "show vlan", "show running-config"], true),
        Switch("dlink", "D-Link DGS managed CLI", "disable clipaging", "show config current_config", ["show switch", "show ports", "show vlan", "show config current_config"]),
        Switch("tplink", "TP-Link JetStream / Omada CLI", "terminal length 0", "show running-config", ["show system-info", "show interface status", "show vlan", "show running-config"], true),
        Switch("edgeswitch", "Ubiquiti EdgeSwitch", "terminal length 0", "show running-config", ["show version", "show sysinfo", "show port all", "show vlan", "show running-config"], true),
        Switch("allied", "Allied Telesis AlliedWare Plus", "terminal length 0", "show running-config", ["show version", "show system", "show interface brief", "show vlan all", "show running-config"], true),
        Switch("ruijie", "Ruijie RGOS", "terminal length 0", "show running-config", ["show version", "show interfaces status", "show vlan", "show running-config"], true),
        Switch("zte", "ZTE ZXR10", "terminal length 0", "show running-config", ["show version", "show interface", "show vlan", "show running-config"], true),
        Switch("sonic", "SONiC management CLI", "terminal length 0", "show running-configuration", ["show version", "show interfaces status", "show vlan", "show lldp table", "show running-configuration"], true)
    ];

    public static DevicePlatform? Find(string id) => All.FirstOrDefault(p => p.Id == id);

    // Only identity command bodies belong here; never infer from hostnames, configuration or neighbor output.
    public static DevicePlatform? Recognize(string identity)
    {
        bool Has(string pattern) => Regex.IsMatch(identity, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
        string? id =
            Has(@"\b(?:C9800[\w-]*|Catalyst\s*9800)\b") ? "c9800" :
            Has(@"AireOS|Cisco Controller|Cisco Wireless Controller|Product Name\s*\.+\s*Cisco.*(?:Controller|Wireless)") ? "aireos" :
            Has(@"NX-OS|Nexus Operating System") ? "nxos" :
            Has(@"\b(?:CBS[23]\d\d|SG[235]\d\d|SF[235]\d\d)[\w-]*\b|Cisco Small Business") ? "cisco-sb" :
            Has(@"\bC1000[\w-]*\b|Catalyst\s*1000") ? "ios" :
            Has(@"\b(?:AP[1236]G\d*|C[123]\d00-(?:K9W7|K9W8)|AIR-(?:AP|CAP|LAP)[\w-]+)\b|Cisco.*Aironet") ? "cisco-ap" :
            Has(@"Cisco IOS|IOS-XE Software|IOS XE Software") ? "ios" :
            Has(@"Arista|vEOS") ? "arista" : Has(@"\bJUNOS\b|\bJunos\b") ? "junos" :
            Has(@"ArubaOS-CX|AOS-CX") ? "aruba-cx" :
            Has(@"Comware") ? "comware" : Has(@"Huawei|Versatile Routing Platform|VRP \(R\)") ? "huawei" :
            Has(@"ProCurve|ProVision|ArubaOS-Switch|Aruba.*(?:2530|2540|2930|3810|5400)|Image stamp:[\s\S]*\b(?:WC|YA|YB|KB|WB|K|RA)\.\d") ? "procurve" :
            Has(@"SmartFabric OS10|Dell.*OS10") ? "dell-os10" :
            Has(@"Force10|Dell.*(?:OS9|Operating System)") ? "dell-os9" :
            Has(@"PowerConnect|Dell.*(?:N[2346]\d{3}|Networking OS6)") ? "dell-os6" :
            Has(@"ExtremeXOS|EXOS|Switch Engine") ? "extreme-exos" :
            Has(@"VOSS|Fabric Engine|Virtual Services Platform") ? "extreme-voss" :
            Has(@"FastIron|Ruckus.*ICX|Brocade.*ICX") ? "fastiron" :
            Has(@"OmniSwitch") ? "alcatel" : Has(@"FortiSwitch") ? "fortiswitch" :
            Has(@"RouterOS|MikroTik|board-name:\s*CRS\d") ? "routeros" : Has(@"NETGEAR") ? "netgear" :
            Has(@"D-Link|Device Type\s*:\s*DGS-") ? "dlink" :
            Has(@"TP-Link|JetStream") ? "tplink" : Has(@"EdgeSwitch") ? "edgeswitch" :
            Has(@"AlliedWare Plus|Allied Telesis") ? "allied" : Has(@"Ruijie|RGOS") ? "ruijie" :
            Has(@"ZXR10") ? "zte" : Has(@"SONiC") ? "sonic" : null;
        return id == null ? null : Find(id);
    }

    public static string[] Probes => ["show version", "show sysinfo", "display version", "get system status", "show system", "show inventory", "show sys-info", "show microcode", "show switch", "show system-info", "/system resource print"];

    public static bool IsRejected(string output) => Regex.IsMatch(output,
        @"(?im)^\s*(?:%\s*)?(?:Error\s*:?\s*)?(?:Invalid (?:input|command|parameter)|Unrecognized command|Unknown command|Incomplete command|Ambiguous command|Incorrect usage|Syntax error|Command (?:not found|not supported|not authorized|authorization failed)|Permission denied|Access denied|Authorization failed|Not enough permissions|Insufficient privileges|Unrecognized word|Wrong parameter|Bad command name)\b|(?im)^\s*%\s*(?:Invalid|Error|Unrecognized|Unknown|Incomplete|Ambiguous)|(?im)^\s*Error:|(?im)^\s*syntax error,");

    public static string Body(string output, string command) => string.Join("\n", Regex.Replace(output, @"\x1B\[[0-?]*[ -/]*[@-~]|\x08", "").Replace("\r", "").Split('\n')
        .Where(line => !line.Trim().Equals(command, StringComparison.OrdinalIgnoreCase) && !IsPrompt(line.Trim()))).Trim();

    public static bool IsPrompt(string line) => Regex.IsMatch(line,
        @"^(?:<[\w.-]+>|\[[\w.@:/ -]+\](?:\s*>|\s*#)?|(?:\([\w. -]+\)|[* ]*[\w.@:/-]+)(?:\([^)]*\))?\s*[>#])$");

    public static IEnumerable<string> Alternatives(DevicePlatform? platform, string command)
    {
        yield return command;
        if (platform?.Id is "ios" or "cisco-ap")
        {
            if (command == "show running-config") yield return "show run";
            if (command == "show mac address-table") yield return "show mac-address-table";
            if (command == "show environment all") yield return "show environment";
        }
        if (platform?.Id == "aireos" && command == "show run-config commands") yield return "show run-config";
    }
}
