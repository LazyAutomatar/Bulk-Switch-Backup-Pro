using System.Collections.Generic;

namespace BulkBackup.Core.Services
{
    public static class CommandProfiles
    {
        public static readonly string[] CiscoIosCommands = new[]
        {
            "terminal length 0",
            "show clock",
            "show version",
            "show inventory",
            "show environment all",
            "show environment power",
            "show environment temperature",
            "show processes cpu sorted",
            "show processes memory sorted",
            "show switch detail",
            "show switch stack-ring speed",
            "show vtp status",
            "show platform hardware fed switch active qos queue stats internal cpu policer",
            "show ip interface brief",
            "show interfaces description",
            "show interfaces status",
            "show interfaces counters errors",
            "show interfaces trunk",
            "show vlan brief",
            "show mac address-table",
            "show ip arp",
            "show ip route",
            "show spanning-tree summary",
            "show etherchannel summary",
            "show cdp neighbors detail",
            "show cdp neighbors",
            "show lldp neighbors detail",
            "show logging",
            "show running-config",
            "show startup-config"
        };

        public static readonly string[] CiscoNexusCommands = new[]
        {
            "terminal length 0",
            "show clock",
            "show version",
            "show inventory",
            "show system resources",
            "show environment",
            "show interface status",
            "show interface counters errors",
            "show interface description",
            "show vlan brief",
            "show mac address-table",
            "show ip arp",
            "show ip route",
            "show spanning-tree summary",
            "show port-channel summary",
            "show cdp neighbors detail",
            "show lldp neighbors detail",
            "show logging logfile",
            "show running-config",
            "show startup-config"
        };

        public static readonly string[] CiscoWlcCommands = new[]
        {
            "terminal length 0",
            "show clock",
            "show version",
            "show inventory",
            "show platform software status control-processor brief",
            "show platform resources",
            "show environment all",
            "show processes cpu",
            "show redundancy",
            "show ap summary",
            "show ap uptime",
            "show wireless stats ap history",
            "show wireless wps rogue ap summary",
            "show ap dot11 5ghz cleanair config",
            "show ap tag summary",
            "show ap dot11 5ghz summary",
            "show ap dot11 24ghz summary",
            "show wlan summary",
            "show wireless profile policy summary",
            "show wireless tag policy summary",
            "show wireless tag site summary",
            "show wireless tag rf summary",
            "show wireless client summary",
            "show wireless mobility summary",
            "show ip interface brief",
            "show interfaces description",
            "show ip route",
            "show ip arp",
            "show logging",
            "show running-config",
            "show startup-config"
        };

        public static readonly string[] CiscoAireOsWlcCommands = new[]
        {
            "config paging disable",
            "show sysinfo",
            "show time",
            "show redundancy summary",
            "show ap summary",
            "show ap join stats summary all",
            "show ap image all",
            "show ap cdp neighbors all",
            "show advanced 802.11a summary",
            "show advanced 802.11b summary",
            "show wlan summary",
            "show client summary",
            "show mobility summary",
            "show exclusionlist",
            "show rogue ap summary",
            "show rogue client summary",
            "show interface summary",
            "show port summary",
            "show route summary",
            "show run-config commands"
        };

        public static readonly string[] CustomTemplateCommands = new[]
        {
            "! Custom Backup Script",
            "! Enter your CLI commands sequentially (one per line):",
            "terminal length 0",
            "show version",
            "show ip interface brief",
            "show running-config"
        };

        public static readonly string[] CiscoApCommands = new[]
        {
            "terminal length 0",
            "show version",
            "show capwap client rcb",
            "show capwap client config",
            "show controllers dot11Radio 0",
            "show run",
            "show interfaces wired 0",
            "show inventory"
        };
    }
}

