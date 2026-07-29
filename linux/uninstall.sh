#!/bin/sh
# Removes the ADS Gamepad Service from a Beckhoff RT Linux system. The
# appsettings.json is kept so a later install finds the configuration again.
set -e

systemctl disable --now adsgamepad.service 2> /dev/null || true
rm -f /etc/systemd/system/adsgamepad.service
systemctl daemon-reload

rm -f /etc/udev/rules.d/70-adsgamepad.rules
udevadm control --reload-rules

if [ -d /opt/ads-gamepad-service ]; then
    find /opt/ads-gamepad-service -mindepth 1 ! -name appsettings.json -delete
    echo "Program files were removed. appsettings.json was kept in /opt/ads-gamepad-service."
fi

echo "ADS Gamepad Service was removed. The adsgamepad user and gamepad group stay; remove them with userdel and groupdel if wanted."
