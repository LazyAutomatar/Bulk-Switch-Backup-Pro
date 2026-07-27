import os
import re
import shutil
import tempfile
import threading
from datetime import datetime
import concurrent.futures
import customtkinter as ctk
from customtkinter import filedialog
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

def enable_ssh(net_connect, domain_name, log_callback, ip):
    """Configures domain name, generates RSA keys, and enables SSH on VTY lines."""
    log_callback(ip, "Telnet used. Configuring and enabling SSH...")
    
    ssh_configs = [
        f"ip domain-name {domain_name}", 
        "ip ssh version 2",
        "line vty 0 15",
        "transport input telnet ssh",   
        "exit"
    ]
    net_connect.send_config_set(ssh_configs)
    net_connect.config_mode()
    
    key_prompt = net_connect.send_command_timing("crypto key generate rsa")
    
    if "replace" in key_prompt.lower():
        net_connect.send_command_timing("no")
        log_callback(ip, "RSA keys already exist. Kept existing keys.")
    elif "modulus" in key_prompt.lower() or "bits" in key_prompt.lower():
        net_connect.send_command_timing("2048")
        log_callback(ip, "RSA keys generated successfully (2048-bit).")
    elif "hostname" in key_prompt.lower() or "name" in key_prompt.lower():
        log_callback(ip, "Failed: The switch is still using the default hostname ('Switch').")
    elif "invalid input" in key_prompt.lower():
        log_callback(ip, "Failed: Switch does not support the 'crypto' command.")
    else:
        log_callback(ip, f"Unexpected response from switch: {key_prompt.strip()}")
        
    net_connect.exit_config_mode()
    log_callback(ip, "SSH config block completed.")

def perform_backup(ip, credentials, domain_name, backup_dir, log_callback, custom_commands, write_config):
    log_callback(ip, "Starting backup process...")
    today_date = datetime.now().strftime("%Y-%m-%d")
    filename = f"{backup_dir}/{ip}_backup_{today_date}.txt"
    net_connect = None
    protocol_used = ""

    current_device = credentials.copy()
    current_device['host'] = ip
    current_device['global_delay_factor'] = 2

    # Connection Logic
    try:
        current_device['device_type'] = 'cisco_ios'
        log_callback(ip, "Attempting SSH connection...")
        net_connect = ConnectHandler(**current_device)
        protocol_used = "SSH"
    except Exception as e:
        log_callback(ip, "SSH failed. Attempting Telnet fallback...")
        try:
            current_device['device_type'] = 'cisco_ios_telnet'
            net_connect = ConnectHandler(**current_device)
            protocol_used = "Telnet"
        except Exception as e2:
            log_callback(ip, f"Both SSH and Telnet failed. Skipping.")
            return

    try:
        log_callback(ip, f"Connected successfully via {protocol_used}.")
        net_connect.enable() 
        
        if protocol_used == "Telnet":
            enable_ssh(net_connect, domain_name, log_callback, ip)
        
        log_callback(ip, "Running backup commands...")
        with open(filename, "w") as backup_file:
            backup_file.write(f"Backup Date: {datetime.now()}\n")
            backup_file.write(f"Device IP: {ip}\n")
            backup_file.write(f"Protocol: {protocol_used}\n")
            backup_file.write("=" * 50 + "\n")

            for i, cmd in enumerate(custom_commands):
                progress = int(((i+1) / len(custom_commands)) * 100)
                log_callback(ip, f"[{progress}%] Running: {cmd}")
                output = net_connect.send_command(cmd)
                
                backup_file.write(f"\n\n{'='*30}\nCommand: {cmd}\n{'='*30}\n")
                backup_file.write(output)

        if write_config:
            log_callback(ip, "Saving configuration (write memory)...")
            net_connect.save_config()
        else:
            log_callback(ip, "Skipping configuration save (disabled).")
            
        log_callback(ip, f"✅ Backup complete! Temp file created.")

    except NetmikoAuthenticationException:
        log_callback(ip, f"Authentication failed via {protocol_used}.")
    except Exception as e:
        log_callback(ip, f"An unexpected error occurred: {e}")
    finally:
        if net_connect:
            net_connect.disconnect()


class App(ctk.CTk):
    def __init__(self):
        super().__init__()

        self.title("Bulk Switch Backup Pro")
        self.geometry("1200x750")
        ctk.set_appearance_mode("System")
        ctk.set_default_color_theme("blue")

        self.grid_columnconfigure(0, weight=1)
        self.grid_columnconfigure(1, weight=2)
        self.grid_rowconfigure(0, weight=1)

        # Left Panel
        self.left_frame = ctk.CTkFrame(self, corner_radius=15)
        self.left_frame.grid(row=0, column=0, padx=20, pady=20, sticky="nsew")

        self.logo_label = ctk.CTkLabel(self.left_frame, text="Backup Settings", font=ctk.CTkFont(size=24, weight="bold"))
        self.logo_label.pack(pady=(20, 10), padx=10)

        # TABVIEW
        self.tabview = ctk.CTkTabview(self.left_frame, height=350)
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

        self.domain_entry = ctk.CTkEntry(self.cred_frame, placeholder_text="Domain Name (e.g. abc.com)", height=30)
        self.domain_entry.pack(pady=5, fill="x")

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
        
        self.current_temp_dir = None

        # Right Panel (Logs)
        self.right_frame = ctk.CTkFrame(self, corner_radius=15)
        self.right_frame.grid(row=0, column=1, padx=(0, 20), pady=20, sticky="nsew")
        self.right_frame.grid_rowconfigure(1, weight=1)
        self.right_frame.grid_columnconfigure(0, weight=1)

        self.log_label = ctk.CTkLabel(self.right_frame, text="Execution Logs", font=ctk.CTkFont(size=24, weight="bold"))
        self.log_label.grid(row=0, column=0, pady=(20, 10), padx=20, sticky="w")

        self.log_textbox = ctk.CTkTextbox(self.right_frame, state="disabled", font=ctk.CTkFont(family="Consolas", size=13))
        self.log_textbox.grid(row=1, column=0, padx=20, pady=(0, 20), sticky="nsew")

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
                
                # Basic IP extraction/validation
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
        self.after(0, update)

    def start_backup_thread(self):
        threading.Thread(target=self.run_backups, daemon=True).start()

    def run_backups(self):
        username = self.username_entry.get().strip()
        password = self.password_entry.get().strip()
        secret = self.secret_entry.get().strip()
        domain = self.domain_entry.get().strip()
        write_config = self.write_config_var.get()

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

        self.start_btn.configure(state="disabled", text="RUNNING...")
        
        credentials = {
            'username': username,
            'password': password,
            'secret': secret if secret else password
        }

        if not self.current_temp_dir:
            self.current_temp_dir = tempfile.mkdtemp(prefix="switch_backups_")
        backup_dir = self.current_temp_dir

        self.append_log("System", f"Starting backups for {len(ip_list)} devices using 5 concurrent threads.")
        if write_config:
            self.append_log("System", "Configuration Save (wr) is ENABLED.")
        else:
            self.append_log("System", "Configuration Save (wr) is DISABLED.")

        with concurrent.futures.ThreadPoolExecutor(max_workers=5) as executor:
            futures = []
            for ip in ip_list:
                futures.append(executor.submit(perform_backup, ip, credentials, domain, backup_dir, self.append_log, cmd_list, write_config))
            
            concurrent.futures.wait(futures)

        self.append_log("System", "All backups completed.")
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