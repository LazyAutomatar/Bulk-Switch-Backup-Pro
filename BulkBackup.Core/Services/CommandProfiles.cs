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
            "show switch",
            "show switch stack-ring speed",
            "show switch virtual",
            "show environment",
            "show environment all",
            "show environment temperature",
            "show env power",
            "show env fan",
            "show power inline",
            "show post",
            "show processes cpu | include CPU",
            "show processes memory",
            "show ip interface brief",
            "show interfaces description",
            "show interfaces status",
            "show interfaces counters errors",
            "show interfaces trunk",
            "show vlan",
            "show vlan brief",
            "show mac address-table",
            "show ip arp",
            "show ip route",
            "show ip route summary",
            "show ip protocols",
            "show spanning-tree",
            "show spanning-tree active",
            "show spanning-tree root",
            "show spanning-tree summary",
            "show vtp status",
            "show etherchannel summary",
            "show cdp neighbors",
            "show cdp neighbors detail",
            "show lldp neighbors",
            "show lldp neighbors detail",
            "show port-security",
            "show port-security address",
            "show ip dhcp snooping",
            "show ip source binding",
            "show access-lists",
            "show logging",
            "show log | include Interface",
            "show license",
            "show license usage",
            "show standby brief",
            "show standby all",
            "show vrrp brief",
            "show vrrp all",
            "show ip sla summary",
            "show ip sla statistics",
            "show ntp status",
            "show ntp associations",
            "show running-config",
            "show startup-config"
        };

        public static readonly string[] CiscoNexusCommands = new[]
        {
            "terminal length 0",
            "show clock",
            "show version",
            "show inventory",
            "show license usage",
            "show system resources",
            "show processes cpu",
            "show processes memory",
            "show environment",
            "show environment temperature",
            "show environment power",
            "show environment fan",
            "show interface status",
            "show interface counters errors",
            "show interface description",
            "show vlan brief",
            "show mac address-table",
            "show ip arp",
            "show ip route",
            "show ip route summary",
            "show ip protocols",
            "show spanning-tree",
            "show spanning-tree root",
            "show port-channel summary",
            "show port-channel usage",
            "show cdp neighbors detail",
            "show lldp neighbors detail",
            "show hsrp brief",
            "show hsrp all",
            "show vrrp brief",
            "show vrrp all",
            "show ip sla summary",
            "show ip sla statistics",
            "show logging",
            "show logging logfile",
            "show ntp peers",
            "show ntp status",
            "show running-config",
            "show startup-config"
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
            "show run",
            "show interfaces wired 0",
            "show inventory"
        };
    }
}
