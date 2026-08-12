import os
import re
import shutil
import tempfile
import threading
from datetime import datetime
import concurrent.futures
import customtkinter as ctk
from customtkinter import filedialog
import ipaddress
from netmiko import ConnectHandler
from netmiko.exceptions import NetmikoTimeoutException, NetmikoAuthenticationException

# Device Profiles Commands
COMMAND_PROFILES = {
    "Cisco Switch": [
        "terminal length 0", "show clock", "show version", "show inventory",
        "show switch", "show switch stack-ring speed", "show switch virtual",
        "show environment", "show environment all", "show environment temperature",
        "show env power", "show env fan", "show power inline", "show post",
        "show processes cpu | include CPU", "show processes memory", "show ip interface brief",
        "show interfaces description", "show interfaces status", "show interfaces counters errors",
        "show interfaces trunk", "show vlan", "show vlan brief", "show mac address-table",
        "show ip arp", "show ip route", "show ip route summary", "show ip protocols",
        "show spanning-tree", "show spanning-tree active", "show spanning-tree root",
        "show spanning-tree summary", "show vtp status", "show etherchannel summary",
        "show cdp neighbors", "show cdp neighbors detail", "show lldp neighbors",
        "show lldp neighbors detail", "show port-security", "show port-security address",
        "show ip dhcp snooping", "show ip source binding", "show access-lists", "show logging",
        "show log | include Interface", "show license", "show license usage", "show standby brief",
        "show standby all", "show vrrp brief", "show vrrp all", "show ip sla summary",
        "show ip sla statistics", "show ntp status", "show ntp associations",
        "show running-config", "show startup-config"
    ],
    "Cisco Nexus Switch": [
        "terminal length 0", "show clock", "show version", "show inventory", "show license usage",
        "show system resources", "show processes cpu", "show processes memory", "show environment",
        "show environment temperature", "show environment power", "show environment fan",
        "show interface status", "show interface counters errors", "show interface description",
        "show vlan brief", "show mac address-table", "show ip arp", "show ip route",
        "show ip route summary", "show ip protocols", "show spanning-tree", "show spanning-tree root",
        "show port-channel summary", "show port-channel usage", "show cdp neighbors detail",
        "show lldp neighbors detail", "show hsrp brief", "show hsrp all", "show vrrp brief",
        "show vrrp all", "show ip sla summary", "show ip sla statistics", "show logging",
        "show logging logfile", "show ntp peers", "show ntp status", "show running-config",
        "show startup-config"
    ],
    "Custom": [
        "! Enter your custom commands here"
    ]
}

def perform_discovery(ip, credentials, log_callback, progress_callback, vlan_to_resolve=None):
    progress_callback(ip, 10, "Discovery: Connecting...", "Connecting...")
    net_connect = None
    protocol_used = ""
    discovered_neighbors = {}
    resolved_subnet = None

    current_device = credentials.copy()
    current_device['host'] = ip
    current_device['global_delay_factor'] = 2

    try:
        current_device['device_type'] = 'cisco_ios'
        net_connect = ConnectHandler(**current_device)
        protocol_used = "SSH"
    except Exception as e:
        try:
            current_device['device_type'] = 'cisco_ios_telnet'
            net_connect = ConnectHandler(**current_device)
            protocol_used = "Telnet"
        except Exception as e2:
            progress_callback(ip, 0, "Discovery Failed.", "Failed")
            return ip, False, {}, None, protocol_used

    try:
        progress_callback(ip, 50, f"Discovery: Running CDP...", protocol_used)
        net_connect.enable() 
        
        try:
            cdp_out = net_connect.send_command("show cdp neighbors detail")
            blocks = cdp_out.split("Device ID:")
            for block in blocks[1:]:
                ip_match = re.search(r'IP(?:v4)?\s+[aA]ddress:\s*(\d{1,3}(?:\.\d{1,3}){3})', block)
                if ip_match:
                    n_ip = ip_match.group(1)
                    platform_match = re.search(r'Platform:\s*([^,\n]+)', block)
                    platform = platform_match.group(1).strip() if platform_match else ""
                    cap_match = re.search(r'Capabilities:\s*(.+)', block)
                    caps = cap_match.group(1) if cap_match else ""
                    
                    # Broad AP detection based on common Cisco AP platforms and capabilities
                    is_ap = bool(re.search(r'AIR-|C91|CW91|CW|CAP|Aironet', platform, re.IGNORECASE)) or 'Trans-Bridge' in caps or 'WLAN' in caps or 'Access-Point' in caps
                    discovered_neighbors[n_ip] = is_ap
        except Exception as e:
            log_callback(ip, f"CDP error on {ip}: {e}")
                
        if vlan_to_resolve:
            try:
                vlan_out = net_connect.send_command(f"show interface Vlan{vlan_to_resolve}")
                match = re.search(r'Internet address is (\d{1,3}(?:\.\d{1,3}){3}/\d+)', vlan_out)
                if match:
                    resolved_subnet = match.group(1)
            except Exception:
                pass

        progress_callback(ip, 100, "Discovery Complete", protocol_used)
        return ip, True, discovered_neighbors, resolved_subnet, protocol_used

    except Exception as e:
        progress_callback(ip, 0, f"Error: {str(e)[:20]}...", "Failed")
        return ip, False, {}, None, protocol_used
    finally:
        if net_connect:
            net_connect.disconnect()


def perform_backup(ip, credentials, backup_dir, log_callback, progress_callback, custom_commands, write_config, is_ap=False):
    progress_callback(ip, 5, "Backup: Connecting...", "Connecting...")
    today_date = datetime.now().strftime("%Y-%m-%d")
    device_type = "AP" if is_ap else "Switch"
    filename = f"{backup_dir}/{ip}_{device_type}_backup_{today_date}.txt"
    net_connect = None
    protocol_used = ""

    current_device = credentials.copy()
    current_device['host'] = ip
    current_device['global_delay_factor'] = 2

    try:
        current_device['device_type'] = 'cisco_ios'
        net_connect = ConnectHandler(**current_device)
        protocol_used = "SSH"
    except Exception as e:
        try:
            current_device['device_type'] = 'cisco_ios_telnet'
            net_connect = ConnectHandler(**current_device)
            protocol_used = "Telnet"
        except Exception as e2:
            progress_callback(ip, 0, "Connection Failed.", "Failed")
            return ip, False

    try:
        progress_callback(ip, 20, f"Connected.", protocol_used)
        net_connect.enable() 
        
        commands_to_run = ["show run", "show interfaces wired 0", "show inventory"] if is_ap else custom_commands
        
        with open(filename, "w") as backup_file:
            backup_file.write(f"Backup Date: {datetime.now()}\n")
            backup_file.write(f"Device IP: {ip}\n")
            backup_file.write(f"Device Type: {device_type}\n")
            backup_file.write(f"Protocol: {protocol_used}\n")
            backup_file.write("=" * 50 + "\n")

            for i, cmd in enumerate(commands_to_run):
                progress_val = int(20 + ((i+1) / len(commands_to_run)) * 70)
                progress_callback(ip, progress_val, f"Running: {cmd}", protocol_used)
                output = net_connect.send_command(cmd)
                backup_file.write(f"\n\n{'='*30}\nCommand: {cmd}\n{'='*30}\n")
                backup_file.write(output)

        if write_config and not is_ap:
            progress_callback(ip, 95, "Saving configuration...", protocol_used)
            net_connect.save_config()
            
        progress_callback(ip, 100, "Backup Complete!", protocol_used)
        return ip, True

    except Exception as e:
        progress_callback(ip, 0, f"Error: {str(e)[:20]}...", "Failed")
        return ip, False
    finally:
        if net_connect:
            net_connect.disconnect()


class App(ctk.CTk):
    def __init__(self):
        super().__init__()

        self.title("Bulk Switch Backup Pro")
        self.geometry("1400x800")
        ctk.set_appearance_mode("System")
        ctk.set_default_color_theme("blue")

        self.grid_columnconfigure(0, weight=1)
        self.grid_columnconfigure(1, weight=3)
        self.grid_rowconfigure(0, weight=1)

        # ---- LEFT PANEL (Settings) ----
        self.left_frame = ctk.CTkFrame(self, corner_radius=15)
        self.left_frame.grid(row=0, column=0, padx=20, pady=20, sticky="nsew")

        self.logo_label = ctk.CTkLabel(self.left_frame, text="Backup Settings", font=ctk.CTkFont(size=24, weight="bold"))
        self.logo_label.pack(pady=(20, 10), padx=10)

        # TABVIEW
        self.tabview = ctk.CTkTabview(self.left_frame, height=450)
        self.tabview.pack(fill="both", expand=True, padx=10, pady=5)
        self.tab_general = self.tabview.add("General")
        self.tab_commands = self.tabview.add("Commands")

        # ---- GENERAL TAB ----
        self.cred_frame = ctk.CTkFrame(self.tab_general, fg_color="transparent")
        self.cred_frame.pack(fill="x", padx=10, pady=5)
        
        self.username_entry = ctk.CTkEntry(self.cred_frame, placeholder_text="Username", height=30)
        self.username_entry.pack(pady=5, fill="x")

        self.password_entry = ctk.CTkEntry(self.cred_frame, placeholder_text="Password", show="*", height=30)
        self.password_entry.pack(pady=5, fill="x")

        self.secret_entry = ctk.CTkEntry(self.cred_frame, placeholder_text="Enable Secret (Optional)", show="*", height=30)
        self.secret_entry.pack(pady=5, fill="x")

        self.write_config_var = ctk.BooleanVar(value=True)
        self.write_checkbox = ctk.CTkCheckBox(self.cred_frame, text="Save Configuration (write memory)", variable=self.write_config_var)
        self.write_checkbox.pack(pady=(10, 5), anchor="w")

        self.targets_frame = ctk.CTkFrame(self.tab_general, fg_color="transparent")
        self.targets_frame.pack(fill="both", expand=True, padx=10, pady=5)

        self.targets_label = ctk.CTkLabel(self.targets_frame, text="Target Devices (IPs)", font=ctk.CTkFont(size=14, weight="bold"))
        self.targets_label.pack(anchor="w", pady=(0, 5))

        self.ip_textbox = ctk.CTkTextbox(self.targets_frame, height=80)
        self.ip_textbox.pack(pady=5, fill="both", expand=True)

        self.file_btn = ctk.CTkButton(self.targets_frame, text="Import Devices from File...", command=self.load_ips, height=30)
        self.file_btn.pack(pady=5, fill="x")

        # ---- CDP TRAVERSAL ----
        self.cdp_frame = ctk.CTkFrame(self.tab_general, fg_color="transparent")
        self.cdp_frame.pack(fill="x", padx=10, pady=5)
        
        self.cdp_top_frame = ctk.CTkFrame(self.cdp_frame, fg_color="transparent")
        self.cdp_top_frame.pack(fill="x", pady=(5, 5))
        
        self.enable_cdp_var = ctk.BooleanVar(value=False)
        self.cdp_checkbox = ctk.CTkCheckBox(self.cdp_top_frame, text="Enable CDP Traversal", variable=self.enable_cdp_var, command=self.toggle_cdp_options)
        self.cdp_checkbox.pack(side="left")
        
        self.include_aps_var = ctk.BooleanVar(value=False)
        self.ap_checkbox = ctk.CTkCheckBox(self.cdp_top_frame, text="Include APs", variable=self.include_aps_var)
        self.ap_checkbox.pack(side="left", padx=(15, 0))
        
        self.cdp_options_frame = ctk.CTkFrame(self.cdp_frame, fg_color="transparent")
        self.cdp_options_frame.pack(fill="x", pady=0)
        
        self.cdp_filter_type_var = ctk.StringVar(value="Subnet")
        self.cdp_filter_menu = ctk.CTkOptionMenu(self.cdp_options_frame, variable=self.cdp_filter_type_var, values=["Subnet", "VLAN ID"], width=90, command=self.update_cdp_placeholder)
        self.cdp_filter_menu.pack(side="left", padx=(0, 5))
        
        self.cdp_filter_entry = ctk.CTkEntry(self.cdp_options_frame, placeholder_text="e.g. 10.1.1.0/24", height=28)
        self.cdp_filter_entry.pack(side="left", fill="x", expand=True)
        
        self.toggle_cdp_options()

        # ---- COMMANDS TAB ----
        self.profile_label = ctk.CTkLabel(self.tab_commands, text="Device Profile:")
        self.profile_label.pack(anchor="w", padx=10, pady=(5, 0))

        self.profile_var = ctk.StringVar(value="Cisco Switch")
        self.profile_menu = ctk.CTkOptionMenu(self.tab_commands, variable=self.profile_var, 
                                              values=["Cisco Switch", "Cisco Nexus Switch", "Custom"],
                                              command=self.load_profile_commands)
        self.profile_menu.pack(fill="x", padx=10, pady=5)

        self.cmd_textbox = ctk.CTkTextbox(self.tab_commands)
        self.cmd_textbox.pack(fill="both", expand=True, padx=10, pady=10)
        self.load_profile_commands("Cisco Switch")

        # Action Section (Below Tabs)
        self.start_btn = ctk.CTkButton(self.left_frame, text="START BULK BACKUP", command=self.start_backup_thread, 
                                       fg_color="#2FA572", hover_color="#107C4F", height=40, font=ctk.CTkFont(size=14, weight="bold"))
        self.start_btn.pack(pady=(10, 5), padx=20, fill="x")

        self.download_frame = ctk.CTkFrame(self.left_frame, fg_color="transparent")
        self.download_frame.pack(pady=(5, 10), padx=20, fill="x")
        self.download_frame.grid_columnconfigure(0, weight=1)
        self.download_frame.grid_columnconfigure(1, weight=1)

        self.download_btn = ctk.CTkButton(self.download_frame, text="SAVE AS ZIP", command=self.download_zip, 
                                       state="disabled", height=35, font=ctk.CTkFont(size=12, weight="bold"))
        self.download_btn.grid(row=0, column=0, padx=(0, 5), sticky="ew")

        self.download_files_btn = ctk.CTkButton(self.download_frame, text="SAVE AS FILES", command=self.download_files, 
                                       state="disabled", height=35, font=ctk.CTkFont(size=12, weight="bold"))
        self.download_files_btn.grid(row=0, column=1, padx=(5, 0), sticky="ew")
        
        self.made_by_label = ctk.CTkLabel(self.left_frame, text="Made by Yash Nalawde", font=ctk.CTkFont(size=12), text_color="gray")
        self.made_by_label.pack(side="bottom", pady=20)

        self.current_temp_dir = None
        self.ip_status_widgets = {}

        # ---- RIGHT PANEL (Dashboard & Logs) ----
        self.right_frame = ctk.CTkFrame(self, corner_radius=15, fg_color="transparent")
        self.right_frame.grid(row=0, column=1, padx=(0, 20), pady=20, sticky="nsew")
        
        # Split 50/50
        self.right_frame.grid_rowconfigure(0, weight=1)
        self.right_frame.grid_rowconfigure(1, weight=1)
        self.right_frame.grid_columnconfigure(0, weight=1)

        # TOP 50%: Dashboard Frame
        self.dashboard_container = ctk.CTkFrame(self.right_frame, corner_radius=15)
        self.dashboard_container.grid(row=0, column=0, pady=(0, 10), sticky="nsew")
        self.dashboard_container.grid_rowconfigure(1, weight=1)
        self.dashboard_container.grid_columnconfigure(0, weight=1)

        self.dash_label = ctk.CTkLabel(self.dashboard_container, text="Live Device Dashboard", font=ctk.CTkFont(size=20, weight="bold"))
        self.dash_label.grid(row=0, column=0, pady=(15, 5), padx=20, sticky="w")

        self.dashboard_frame = ctk.CTkScrollableFrame(self.dashboard_container, fg_color="transparent")
        self.dashboard_frame.grid(row=1, column=0, padx=10, pady=(0, 10), sticky="nsew")
        try:
            self.dashboard_frame._scrollbar.grid_remove()
        except Exception:
            pass

        # BOTTOM 50%: Log Frame
        self.log_container = ctk.CTkFrame(self.right_frame, corner_radius=15)
        self.log_container.grid(row=1, column=0, pady=(10, 0), sticky="nsew")
        self.log_container.grid_rowconfigure(1, weight=1)
        self.log_container.grid_columnconfigure(0, weight=1)

        self.log_label = ctk.CTkLabel(self.log_container, text="Execution Logs", font=ctk.CTkFont(size=20, weight="bold"))
        self.log_label.grid(row=0, column=0, pady=(15, 5), padx=20, sticky="w")

        self.log_textbox = ctk.CTkTextbox(self.log_container, state="disabled", font=ctk.CTkFont(family="Consolas", size=13))
        self.log_textbox.grid(row=1, column=0, padx=20, pady=(0, 20), sticky="nsew")
        try:
            self.log_textbox._scrollbar.grid_remove()
        except Exception:
            pass

    def toggle_cdp_options(self):
        if self.enable_cdp_var.get():
            self.cdp_filter_menu.configure(state="normal")
            self.cdp_filter_entry.configure(state="normal")
            self.ap_checkbox.configure(state="normal")
        else:
            self.cdp_filter_menu.configure(state="disabled")
            self.cdp_filter_entry.configure(state="disabled")
            self.ap_checkbox.configure(state="disabled")

    def update_cdp_placeholder(self, choice):
        if choice == "Subnet":
            self.cdp_filter_entry.configure(placeholder_text="e.g. 10.1.1.0/24")
        else:
            self.cdp_filter_entry.configure(placeholder_text="e.g. 100")

    def load_profile_commands(self, profile_name):
        commands = COMMAND_PROFILES.get(profile_name, [])
        self.cmd_textbox.delete("1.0", ctk.END)
        self.cmd_textbox.insert("1.0", "\n".join(commands))

    def load_ips(self):
        file_path = filedialog.askopenfilename(
            title="Select Devices File",
            filetypes=(("Text Files", "*.txt"), ("CSV Files", "*.csv"), ("All Files", "*.*"))
        )
        
        if file_path:
            try:
                with open(file_path, "r") as file:
                    content = file.read()
                
                ips = re.findall(r'\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b', content)
                
                if not ips:
                    self.show_error("Invalid file format. No valid IP addresses found in the selected file.")
                    return
                
                self.ip_textbox.delete("1.0", ctk.END)
                self.ip_textbox.insert(ctk.END, "\n".join(ips))
                self.append_log("System", f"Successfully loaded {len(ips)} IP addresses from {os.path.basename(file_path)}")
                
            except Exception as e:
                self.show_error(f"Failed to read file: {e}")

    def show_error(self, message):
        alert = ctk.CTkToplevel(self)
        alert.title("Error")
        alert.geometry("400x200")
        alert.attributes("-topmost", True)
        
        lbl = ctk.CTkLabel(alert, text=message, wraplength=350, font=ctk.CTkFont(size=14))
        lbl.pack(pady=40, padx=20)
        
        btn = ctk.CTkButton(alert, text="OK", command=alert.destroy, width=100)
        btn.pack(pady=10)

    def append_log(self, source, message):
        def update():
            self.log_textbox.configure(state="normal")
            timestamp = datetime.now().strftime("%H:%M:%S")
            self.log_textbox.insert(ctk.END, f"[{timestamp}] [{source}] {message}\n")
            self.log_textbox.see(ctk.END)
            self.log_textbox.configure(state="disabled")
            
            try:
                if self.log_textbox._textbox.yview() == (0.0, 1.0):
                    self.log_textbox._scrollbar.grid_remove()
                else:
                    self.log_textbox._scrollbar.grid()
            except Exception:
                pass
                
        self.after(0, update)

    def add_dashboard_card(self, ip, title="Waiting..."):
        if ip in self.ip_status_widgets:
            return
        
        card = ctk.CTkFrame(self.dashboard_frame, corner_radius=10)
        card.pack(fill="x", pady=5, padx=10)
        
        header_frame = ctk.CTkFrame(card, fg_color="transparent")
        header_frame.pack(fill="x", padx=10, pady=(10, 5))
        
        ip_label = ctk.CTkLabel(header_frame, text=ip, font=ctk.CTkFont(size=14, weight="bold"))
        ip_label.pack(side="left")
        
        proto_label = ctk.CTkLabel(header_frame, text=title, font=ctk.CTkFont(size=12))
        proto_label.pack(side="right")
        
        progress = ctk.CTkProgressBar(card)
        progress.pack(fill="x", padx=10, pady=5)
        progress.set(0)
        
        status_label = ctk.CTkLabel(card, text="Pending", font=ctk.CTkFont(size=12))
        status_label.pack(fill="x", padx=10, pady=(0, 10), anchor="w")
        
        self.ip_status_widgets[ip] = {
            'card': card,
            'proto_label': proto_label,
            'progress': progress,
            'status_label': status_label
        }
        
        try:
            if self.dashboard_frame._parent_canvas.yview() == (0.0, 1.0):
                self.dashboard_frame._scrollbar.grid_remove()
            else:
                self.dashboard_frame._scrollbar.grid()
        except Exception:
            pass

    def init_dashboard_cards(self, ip_list):
        for widget in self.dashboard_frame.winfo_children():
            widget.destroy()
        self.ip_status_widgets.clear()
        for ip in ip_list:
            self.add_dashboard_card(ip)

    def update_dashboard(self, ip, percentage, status_text, protocol="Unknown"):
        def update():
            if ip in self.ip_status_widgets:
                widgets = self.ip_status_widgets[ip]
                widgets['progress'].set(percentage / 100.0)
                widgets['status_label'].configure(text=status_text)
                
                if protocol != "Unknown":
                    widgets['proto_label'].configure(text=f"[{protocol}]")
                    if protocol == "SSH":
                        widgets['proto_label'].configure(text_color="#2FA572")
                    elif protocol == "Telnet":
                        widgets['proto_label'].configure(text_color="#F39C12")
                    elif protocol == "Failed":
                        widgets['proto_label'].configure(text_color="#E74C3C")
            else:
                self.after(100, lambda: self.update_dashboard(ip, percentage, status_text, protocol))
        self.after(0, update)

    def start_backup_thread(self):
        threading.Thread(target=self.run_backups, daemon=True).start()

    def run_backups(self):
        username = self.username_entry.get().strip()
        password = self.password_entry.get().strip()
        secret = self.secret_entry.get().strip()
        write_config = self.write_config_var.get()
        
        enable_cdp = self.enable_cdp_var.get()
        include_aps = self.include_aps_var.get()
        cdp_filter_type = self.cdp_filter_type_var.get()
        cdp_filter_val = self.cdp_filter_entry.get().strip()

        if not username or not password:
            self.show_error("Username and Password are required.")
            return

        raw_ips = self.ip_textbox.get("1.0", ctk.END).strip().split('\n')
        ip_list = [ip.strip() for ip in raw_ips if ip.strip()]

        if not ip_list:
            self.show_error("No valid IP addresses provided.")
            return

        raw_cmds = self.cmd_textbox.get("1.0", ctk.END).strip().split('\n')
        cmd_list = [c.strip() for c in raw_cmds if c.strip()]
        
        if not cmd_list:
            self.show_error("No commands specified in the Commands tab.")
            return
            
        resolved_subnet_obj = None
        if enable_cdp and cdp_filter_type == "Subnet" and cdp_filter_val:
            try:
                resolved_subnet_obj = ipaddress.ip_network(cdp_filter_val, strict=False)
            except ValueError:
                self.show_error("Invalid Subnet format. Use e.g., 10.1.1.0/24")
                return

        self.start_btn.configure(state="disabled", text="RUNNING...")
        
        # Clear dashboard before starting dynamic run
        def clear_dash():
            for widget in self.dashboard_frame.winfo_children():
                widget.destroy()
            self.ip_status_widgets.clear()
        self.after(0, clear_dash)
        
        credentials = {
            'username': username,
            'password': password,
            'secret': secret if secret else password
        }

        if not self.current_temp_dir:
            self.current_temp_dir = tempfile.mkdtemp(prefix="switch_backups_")
        backup_dir = self.current_temp_dir

        discovered_devices_to_backup = {} # {ip: is_ap}

        if enable_cdp:
            self.after(0, lambda: self.dash_label.configure(text="Phase 1: Mapping Topology..."))
            self.append_log("System", f"Starting Phase 1: Fast Topology Discovery (Subnet filter: {cdp_filter_val})")

            visited_discovery = set()
            queue_ips = ip_list.copy()
            pending_discovered = {}

            with concurrent.futures.ThreadPoolExecutor(max_workers=5) as executor:
                futures_map = {}
                
                def submit_discovery(ip):
                    if ip not in visited_discovery:
                        visited_discovery.add(ip)
                        self.after(0, lambda ip=ip: self.add_dashboard_card(ip, title="Discovering..."))
                        vlan_to_resolve = None
                        if cdp_filter_type == "VLAN ID" and not resolved_subnet_obj and cdp_filter_val:
                            vlan_to_resolve = cdp_filter_val
                        future = executor.submit(
                            perform_discovery, ip, credentials, self.append_log, self.update_dashboard, vlan_to_resolve
                        )
                        futures_map[future] = ip

                for ip in queue_ips:
                    discovered_devices_to_backup[ip] = False
                    submit_discovery(ip)
                    
                while futures_map:
                    done, _ = concurrent.futures.wait(futures_map.keys(), return_when=concurrent.futures.FIRST_COMPLETED)
                    for future in done:
                        ip = futures_map.pop(future)
                        try:
                            _, success, discovered_neighbors, resolved_subnet, _ = future.result()
                            if success:
                                if cdp_filter_type == "VLAN ID" and resolved_subnet and not resolved_subnet_obj:
                                    try:
                                        resolved_subnet_obj = ipaddress.ip_network(resolved_subnet, strict=False)
                                        self.append_log("System", f"Successfully initialized CDP filter with subnet {resolved_subnet_obj}")
                                        for p_ip, p_is_ap in list(pending_discovered.items()):
                                            if p_ip not in discovered_devices_to_backup:
                                                if p_is_ap:
                                                    if include_aps:
                                                        discovered_devices_to_backup[p_ip] = True
                                                        visited_discovery.add(p_ip)
                                                        self.append_log("CDP", f"Discovered Access Point: {p_ip}")
                                                        self.after(0, lambda p_ip=p_ip: self.add_dashboard_card(p_ip, title="[AP Discovered]"))
                                                else:
                                                    try:
                                                        if ipaddress.ip_address(p_ip) in resolved_subnet_obj:
                                                            discovered_devices_to_backup[p_ip] = False
                                                            submit_discovery(p_ip)
                                                    except ValueError:
                                                        pass
                                        pending_discovered.clear()
                                    except ValueError:
                                        self.append_log("System", f"Failed to parse resolved subnet {resolved_subnet}")
                                
                                for d_ip, is_ap in discovered_neighbors.items():
                                    if d_ip not in visited_discovery and d_ip not in discovered_devices_to_backup:
                                        if is_ap:
                                            if include_aps:
                                                discovered_devices_to_backup[d_ip] = True
                                                visited_discovery.add(d_ip)
                                                self.append_log("CDP", f"Discovered Access Point: {d_ip}")
                                                self.after(0, lambda d_ip=d_ip: self.add_dashboard_card(d_ip, title="[AP Discovered]"))
                                        else:
                                            if resolved_subnet_obj:
                                                try:
                                                    if ipaddress.ip_address(d_ip) in resolved_subnet_obj:
                                                        discovered_devices_to_backup[d_ip] = False
                                                        submit_discovery(d_ip)
                                                except ValueError:
                                                    pass
                                            elif cdp_filter_type == "VLAN ID":
                                                pending_discovered[d_ip] = is_ap
                        except Exception as e:
                            self.append_log("System", f"Discovery error for {ip}: {str(e)}")
        else:
            for ip in ip_list:
                discovered_devices_to_backup[ip] = False
                self.after(0, lambda ip=ip: self.add_dashboard_card(ip))

        # PHASE 2: Bulk Backup
        self.after(0, lambda: self.dash_label.configure(text="Phase 2: Bulk Backup..."))
        self.append_log("System", f"Starting Phase 2: Backing up {len(discovered_devices_to_backup)} devices concurrently")

        # Reset UI progress for Phase 2
        for ip, is_ap in discovered_devices_to_backup.items():
            title = "[AP Pending]" if is_ap else "[Pending]"
            self.after(0, lambda ip=ip, t=title: self.update_dashboard(ip, 0, "Waiting for Backup...", t))

        with concurrent.futures.ThreadPoolExecutor(max_workers=5) as executor:
            backup_futures = []
            for ip, is_ap in discovered_devices_to_backup.items():
                backup_futures.append(executor.submit(
                    perform_backup, ip, credentials, backup_dir, 
                    self.append_log, self.update_dashboard, cmd_list, write_config, is_ap
                ))
            concurrent.futures.wait(backup_futures)

        self.append_log("System", "All backups completed.")
        self.after(0, lambda: self.dash_label.configure(text="Live Device Dashboard (Finished)"))
        self.start_btn.configure(state="normal", text="START BULK BACKUP")
        self.download_btn.configure(state="normal")
        self.download_files_btn.configure(state="normal")

    def download_zip(self):
        if not self.current_temp_dir or not os.path.exists(self.current_temp_dir):
            self.show_error("No backups available to download.")
            return

        save_path = filedialog.asksaveasfilename(
            title="Save Backups As ZIP",
            defaultextension=".zip",
            filetypes=[("ZIP Archives", "*.zip")],
            initialfile=f"Switch_Backups_{datetime.now().strftime('%Y%m%d_%H%M%S')}.zip"
        )

        if save_path:
            try:
                base_name = save_path
                if save_path.endswith('.zip'):
                    base_name = save_path[:-4]
                
                shutil.make_archive(base_name, 'zip', self.current_temp_dir)
                self.append_log("System", f"Successfully saved backups to {save_path}")
                
                shutil.rmtree(self.current_temp_dir, ignore_errors=True)
                self.current_temp_dir = None
                self.download_btn.configure(state="disabled")
                self.download_files_btn.configure(state="disabled")
            except Exception as e:
                self.show_error(f"Failed to save ZIP: {e}")

    def download_files(self):
        if not self.current_temp_dir or not os.path.exists(self.current_temp_dir):
            self.show_error("No backups available to download.")
            return

        dest_dir = filedialog.askdirectory(title="Select Folder to Save Raw Backups")

        if dest_dir:
            try:
                for item in os.listdir(self.current_temp_dir):
                    s = os.path.join(self.current_temp_dir, item)
                    d = os.path.join(dest_dir, item)
                    if os.path.isdir(s):
                        shutil.copytree(s, d, dirs_exist_ok=True)
                    else:
                        shutil.copy2(s, d)
                        
                self.append_log("System", f"Successfully saved raw backup files to {dest_dir}")
                
                shutil.rmtree(self.current_temp_dir, ignore_errors=True)
                self.current_temp_dir = None
                self.download_btn.configure(state="disabled")
                self.download_files_btn.configure(state="disabled")
            except Exception as e:
                self.show_error(f"Failed to save files: {e}")

if __name__ == "__main__":
    app = App()
    app.mainloop()