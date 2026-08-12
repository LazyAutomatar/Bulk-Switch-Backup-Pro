# Bulk Switch Backup Pro

**Bulk Switch Backup Pro** is a high-performance GUI desktop application for network engineers and administrators to automate bulk configuration backups of Cisco switches, Nexus switches, and Access Points. 

Featuring an intelligent **Two-Phase CDP Traversal Engine**, the tool can discover and map your entire network topology from a single seed switch and execute multi-threaded backups across all discovered devices concurrently.

---

## Key Features

- **Two-Phase Execution Engine:**
  - **Phase 1 (Fast Topology Mapping):** Rapidly queries `show cdp neighbors detail` across seed nodes to map out the network structure without waiting for heavy backups.
  - **Phase 2 (Concurrent Bulk Backup):** Executes multi-threaded backups across all mapped devices concurrently via SSH (with automatic Telnet fallback).
- **CDP Traversal & Subnet Filtering:**
  - Filter network traversal by Subnet CIDR (e.g., `10.34.11.0/24`).
  - Filter by VLAN ID (e.g., `VLAN 100`) where the app automatically resolves the management subnet from the seed switch interface (`show interface Vlan<ID>`).
- **Cisco Access Point (AP) Support:**
  - **Include APs Toggle:** Easily include or exclude Access Points from backups.
  - **Automatic AP Hardware Detection:** Detects Cisco APs (`AIR-`, `C91`, `CW91`, `CAP`, `Aironet`, `Trans-Bridge`, `WLAN`, `Access-Point`).
  - **Subnet Bypass for APs:** APs automatically bypass switch management subnet constraints, enabling backups for APs living on dedicated wireless subnets.
  - **Custom AP Command Profile:** Runs AP-specific commands (`show run`, `show interfaces wired 0`, `show inventory`).
- **Device Profiles & Custom Commands:**
  - Pre-configured profiles for **Cisco Switch (IOS / IOS-XE)** and **Cisco Nexus (NX-OS)**.
  - Custom command editor to specify exact command sets.
  - Optional `write memory` / configuration saving prior to backup.
- **Modern User Interface:**
  - Built with `CustomTkinter` featuring dark/light mode support.
  - **Live Device Dashboard:** Displays real-time progress bars, state indicators, and connection protocol badges (`[SSH]`, `[Telnet]`, `[AP Discovered]`, `[Failed]`).
  - **Execution Logs:** Real-time timestamped event logs.
- **Export Options:**
  - Download all device backups directly as a single `.zip` archive.
  - Export raw `.txt` files to any selected local directory.
- **Standalone Executable:**
  - Bundled into a single portable Windows executable (`Bulk Backup Pro.exe`).

---

## Installation & Setup

### Option 1: Standalone Executable (Recommended)
Run `dist/Bulk Backup Pro.exe` directly on any 64-bit Windows machine. No Python installation required.

### Option 2: Run from Source
1. Clone the repository:
   ```bash
   git clone https://github.com/Majordean0303/Bulk-Switch-Backup.git
   cd Bulk-Switch-Backup
   ```
2. Install dependencies:
   ```bash
   pip install -r requirements.txt
   ```
3. Launch the application:
   ```bash
   python app.py
   ```

---

## How to Use

1. **Credentials & Devices:**
   - Enter your network **Username**, **Password**, and optional **Enable Secret**.
   - Input your starting switch IP address(es) into the **Target Devices / Seed IPs** box (one per line) or click **Import Devices from File...**.
2. **CDP Traversal (Optional):**
   - Check **Enable CDP Traversal**.
   - Select filter type (**Subnet** or **VLAN ID**) and enter the value (e.g., `10.34.11.0/24` or `100`).
   - Check **Include APs** if you wish to include Access Points in the backup run.
3. **Commands Profile:**
   - Select the device profile (**Cisco Switch**, **Cisco Nexus Switch**, or **Custom**) from the *Commands* tab.
4. **Execute & Save:**
   - Click **START BULK BACKUP**.
   - Watch Phase 1 map out the topology and Phase 2 execute concurrent backups.
   - Click **SAVE AS ZIP** or **SAVE AS FILES** to export the backups.

---

## Building the Executable

To build the standalone `.exe` using PyInstaller:

Run the included build script:
```cmd
build.bat
```
Or execute PyInstaller directly with the `.spec` file:
```cmd
python -m PyInstaller --noconfirm app.spec
```
The output will be saved in `dist/Bulk Backup Pro.exe`.

---

## Author & Maintainer
Made by **Yash Nalawde**
