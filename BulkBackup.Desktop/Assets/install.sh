#!/bin/bash
set -e

APP_NAME="Bulk Switch Backup Pro"
BINARY_NAME="BulkSwitchBackupPro"
INSTALL_DIR="/opt/bulkswitchbackuppro"
BIN_LINK="/usr/local/bin/bulkswitchbackuppro"
DESKTOP_FILE="/usr/share/applications/bulkswitchbackuppro.desktop"
ICON_DIR="/usr/share/icons/hicolor/256x256/apps"

echo "=========================================================="
echo " Installing $APP_NAME..."
echo "=========================================================="

if [ "$EUID" -ne 0 ]; then
    echo "Please run as root or with sudo: sudo ./install.sh"
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

mkdir -p "$INSTALL_DIR"
cp -R "$SCRIPT_DIR/"* "$INSTALL_DIR/"
chmod +x "$INSTALL_DIR/$BINARY_NAME"

ln -sf "$INSTALL_DIR/$BINARY_NAME" "$BIN_LINK"

mkdir -p "$ICON_DIR"
if [ -f "$INSTALL_DIR/app.png" ]; then
    cp "$INSTALL_DIR/app.png" "$ICON_DIR/bulkswitchbackuppro.png"
fi

cat <<EOF > "$DESKTOP_FILE"
[Desktop Entry]
Name=$APP_NAME
Comment=Enterprise Multi-Threaded Network Switch Backup & Automation
Exec=$BIN_LINK
Icon=bulkswitchbackuppro
Terminal=false
Type=Application
Categories=Network;System;Utility;
StartupNotify=true
EOF

chmod 644 "$DESKTOP_FILE"

echo "Installation complete! You can launch '$APP_NAME' from your applications menu or run 'bulkswitchbackuppro' from terminal."
