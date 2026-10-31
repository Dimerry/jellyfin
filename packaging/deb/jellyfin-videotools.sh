#!/bin/sh
# Wrapper around the bundled Jellyfin binary: reads /etc/default/jellyfin-videotools
# and turns the variables there into command line switches.
set -e

NAME=jellyfin-videotools
DEFAULT_FILE="/etc/$NAME"

if [ -r "$DEFAULT_FILE" ]; then
    . "$DEFAULT_FILE"
fi

: "${JELLYFIN_DATA_DIR:=/var/lib/$NAME}"
: "${JELLYFIN_CONFIG_DIR:=/etc/$NAME}"
: "${JELLYFIN_CACHE_DIR:=/var/cache/$NAME}"
: "${JELLYFIN_LOG_DIR:=/var/log/$NAME}"
: "${JELLYFIN_WEB_DIR:=/opt/$NAME/jellyfin-web}"
: "${JELLYFIN_ADDITIONAL_OPTS:=}"

exec "/opt/$NAME/jellyfin" \
    --datadir "$JELLYFIN_DATA_DIR" \
    --configdir "$JELLYFIN_CONFIG_DIR" \
    --cachedir "$JELLYFIN_CACHE_DIR" \
    --logdir "$JELLYFIN_LOG_DIR" \
    --webdir "$JELLYFIN_WEB_DIR" \
    $JELLYFIN_ADDITIONAL_OPTS \
    "$@"
