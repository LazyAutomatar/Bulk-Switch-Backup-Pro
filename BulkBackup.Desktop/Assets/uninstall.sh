#!/bin/bash
set -e

if [ "$EUID" -ne 0 ]; then
    echo "Please run as root or with sudo: sudo ./uninstall.sh"
    exit 1
fi

rm -rf /opt/bulkswitchbackuppro
rm -f /usr/local/bin/bulkswitchbackuppro
rm -f /usr/share/applications/bulkswitchbackuppro.desktop
rm -f /usr/share/icons/hicolor/256x256/apps/bulkswitchbackuppro.png

echo "Bulk Switch Backup Pro has been successfully uninstalled."
