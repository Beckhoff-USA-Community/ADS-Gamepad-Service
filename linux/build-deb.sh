#!/bin/sh
# Builds the Debian package from a self contained Linux publish.
# Run on a Linux machine from the repository root:
#   dotnet publish src/AdsGamepadService -c Release -r linux-x64 --self-contained -o linux/publish
#   sh linux/build-deb.sh
# For the ARM controllers publish with -r linux-arm64 instead and name the
# architecture as the third argument:
#   sh linux/build-deb.sh linux/publish linux arm64
# The version comes from the project file, so the package can never drift
# from the release train.
set -e

PAYLOAD="${1:-linux/publish}"
OUT="${2:-linux}"
ARCH="${3:-amd64}"

if [ ! -f "$PAYLOAD/AdsGamepadService" ]; then
    echo "No published service found at $PAYLOAD. Run the publish step first." >&2
    exit 1
fi

VERSION=$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' src/AdsGamepadService/AdsGamepadService.csproj | head -n 1)
if [ -z "$VERSION" ]; then
    echo "Could not read the version from the project file." >&2
    exit 1
fi

STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT

mkdir -p "$STAGE/opt/ads-gamepad-service"
cp -r "$PAYLOAD"/. "$STAGE/opt/ads-gamepad-service/"
# The live appsettings.json is created from the Linux sample on first
# install and is never a package file, so upgrades keep an edited one.
# The publish output carries the Windows sample, drop it.
rm -f "$STAGE/opt/ads-gamepad-service/appsettings.json"
cp linux/appsettings.linux.json "$STAGE/opt/ads-gamepad-service/"
# A publish staged through a Windows machine loses the execute bit
chmod 0755 "$STAGE/opt/ads-gamepad-service/AdsGamepadService"

install -D -m 0644 linux/adsgamepad.service "$STAGE/lib/systemd/system/adsgamepad.service"
install -D -m 0644 linux/70-adsgamepad.rules "$STAGE/lib/udev/rules.d/70-adsgamepad.rules"

mkdir -p "$STAGE/DEBIAN"
sed -e "s/__VERSION__/$VERSION/" -e "s/__ARCH__/$ARCH/" linux/deb/control.in > "$STAGE/DEBIAN/control"
install -m 0755 linux/deb/preinst linux/deb/postinst linux/deb/prerm linux/deb/postrm "$STAGE/DEBIAN/"

mkdir -p "$OUT"
dpkg-deb --build --root-owner-group "$STAGE" "$OUT/ads-gamepad-service_${VERSION}_${ARCH}.deb"
