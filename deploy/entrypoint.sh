#!/bin/sh
# 以 root 启动时修正 /data 属主，然后降权到 PUID:PGID 运行应用。
set -e

DATA_DIR="${MICA_DATA_DIR:-/data}"
PUID="${PUID:-1000}"
PGID="${PGID:-1000}"

if [ "$(id -u)" = "0" ]; then
    mkdir -p "$DATA_DIR"

    current_uid="$(stat -c '%u' "$DATA_DIR" 2>/dev/null || echo 0)"
    current_gid="$(stat -c '%g' "$DATA_DIR" 2>/dev/null || echo 0)"
    if [ "$current_uid" != "$PUID" ] || [ "$current_gid" != "$PGID" ]; then
        chown -R "$PUID:$PGID" "$DATA_DIR" 2>/dev/null || \
            echo "warning: 无法修改 $DATA_DIR 属主，请检查挂载权限" >&2
    fi

    exec setpriv --reuid="$PUID" --regid="$PGID" --clear-groups \
        env HOME=/tmp \
        dotnet /app/MicaRomManager.Web.dll
fi

exec dotnet /app/MicaRomManager.Web.dll
