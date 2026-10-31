#!/bin/bash
# Build a self-contained .deb for the Jellyfin VideoTools fork.
#
#   ./packaging/deb/build.sh [output-dir]
#
# Requirements: dotnet SDK 10, dpkg-deb, a built jellyfin-web `dist` directory
# (default: ../jellyfin-web/dist, override with WEB_DIR=/path/to/dist).
#
# The package bundles the .NET runtime, so the target machine needs neither the
# SDK nor a matching runtime. ffmpeg is taken from the system (Depends: ffmpeg).
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PKG_NAME=jellyfin-videotools
OUT_DIR="${1:-$REPO/dist}"
WEB_DIR="${WEB_DIR:-$REPO/../jellyfin-web/dist}"
WORK="${WORK:-$(mktemp -d /tmp/jf-deb.XXXXXX)}"
STAGE="$WORK/root"
APP="$STAGE/opt/$PKG_NAME"
ARCH="${ARCH:-amd64}"

# Version comes from SharedVersion.cs (AssemblyVersion), same number the server reports.
VERSION="$(grep -oP 'AssemblyVersion\("\K[^"]+' "$REPO/SharedVersion.cs")"
[ -n "$VERSION" ] || { echo "cannot read version from SharedVersion.cs" >&2; exit 1; }

if [ ! -f "$WEB_DIR/index.html" ]; then
    echo "web client not found in $WEB_DIR (build it with: cd ../jellyfin-web && npm run build:production)" >&2
    exit 1
fi

mkdir -p "$APP" "$STAGE/DEBIAN" "$STAGE/usr/bin" "$STAGE/lib/systemd/system" \
    "$STAGE/etc/default" "$STAGE/usr/share/doc/$PKG_NAME" "$OUT_DIR"

echo "==> publishing $PKG_NAME $VERSION (self-contained linux-x64)"
dotnet publish "$REPO/Jellyfin.Server/Jellyfin.Server.csproj" \
    -c Release -r linux-x64 --self-contained true -p:PublishTrimmed=false \
    -o "$APP" -v quiet

# Symbols are useless in a binary package and cost ~10 MB.
find "$APP" -name '*.pdb' -delete

echo "==> adding web client from $WEB_DIR"
mkdir -p "$APP/jellyfin-web"
cp -a "$WEB_DIR/." "$APP/jellyfin-web/"

echo "==> assembling package tree"
install -m 0755 "$REPO/packaging/deb/jellyfin-videotools.sh" "$STAGE/usr/bin/$PKG_NAME"
install -m 0644 "$REPO/packaging/deb/jellyfin-videotools.service" "$STAGE/lib/systemd/system/$PKG_NAME.service"
install -m 0644 "$REPO/packaging/deb/jellyfin-videotools.default" "$STAGE/etc/default/$PKG_NAME"
install -m 0644 "$REPO/LICENSE" "$STAGE/usr/share/doc/$PKG_NAME/copyright"
for script in postinst prerm postrm; do
    install -m 0755 "$REPO/packaging/deb/$script" "$STAGE/DEBIAN/$script"
done

sed -e "s/^Version: .*/Version: $VERSION/" \
    -e "s/^Architecture: .*/Architecture: $ARCH/" \
    "$REPO/packaging/deb/control" > "$STAGE/DEBIAN/control"
printf '/etc/default/%s\n' "$PKG_NAME" > "$STAGE/DEBIAN/conffiles"

# dpkg-deb keeps the owner of the staged files; normalise to root.
chmod -R go-w "$STAGE"
chmod 0755 "$STAGE/DEBIAN"

DEB="$OUT_DIR/${PKG_NAME}_${VERSION}_${ARCH}.deb"
echo "==> building $DEB"
dpkg-deb --build --root-owner-group "$STAGE" "$DEB" >/dev/null

echo "==> done"
dpkg-deb --info "$DEB" | sed -n '1,12p'
ls -lh "$DEB"
echo "staging tree left in $WORK (remove it with: rm -rf $WORK)"
