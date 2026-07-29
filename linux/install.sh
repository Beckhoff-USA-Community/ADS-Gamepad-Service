#!/bin/sh
# Installs the ADS Gamepad Service on Beckhoff RT Linux. Run as root from
# this directory after publishing, see README.md. An existing
# appsettings.json is kept, so an upgrade never overwrites the configuration.
set -e

PAYLOAD="${1:-./publish}"
TARGET=/opt/ads-gamepad-service

if [ ! -f "$PAYLOAD/AdsGamepadService" ]; then
    echo "No published service found at $PAYLOAD. Run the publish step from README.md first." >&2
    exit 1
fi

# Service account and the device access group the udev rule grants
getent group gamepad > /dev/null || groupadd --system gamepad
id -u adsgamepad > /dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin --gid gamepad adsgamepad

# The TwinCAT usermode router only accepts ADS port registrations from
# members of its tcads group
if getent group tcads > /dev/null; then
    usermod -aG tcads adsgamepad
fi

# A running instance holds the executable open; copying over it fails
systemctl stop adsgamepad.service 2> /dev/null || true

mkdir -p "$TARGET"
KEEP_CONFIG=""
if [ -f "$TARGET/appsettings.json" ]; then
    KEEP_CONFIG=yes
    cp "$TARGET/appsettings.json" "$TARGET/.appsettings.keep"
fi

cp -r "$PAYLOAD"/. "$TARGET"/
if [ -n "$KEEP_CONFIG" ]; then
    # The payload carries the Windows sample; the kept configuration wins
    mv "$TARGET/.appsettings.keep" "$TARGET/appsettings.json"
    echo "Keeping the existing appsettings.json."
else
    cp ./appsettings.linux.json "$TARGET/appsettings.json"
fi
# A publish copied over from a Windows build machine loses the execute bit
chmod 0755 "$TARGET/AdsGamepadService"
chown -R adsgamepad:gamepad "$TARGET"

install -m 0644 ./70-adsgamepad.rules /etc/udev/rules.d/
udevadm control --reload-rules
# Only the hidraw nodes need reevaluation for the new group grant
udevadm trigger --subsystem-match=hidraw

install -m 0644 ./adsgamepad.service /etc/systemd/system/
systemctl daemon-reload
systemctl enable adsgamepad.service
systemctl restart adsgamepad.service

echo "ADS Gamepad Service is installed and running."
echo "Settings live in $TARGET/appsettings.json. Restart with: systemctl restart adsgamepad"
