#!/usr/bin/env bash
# Builds the Linux release: a self-contained linux-x64 Bonxie with its game player, skua.swf and plugins,
# packed as Build/Bonxie-<version>-linux-x64.tar.gz (one Bonxie/ folder; extract it and run ./Bonxie).
#
#   ./package-linux.sh            (version from Directory.Build.props)
#   ./package-linux.sh 2.2.0
#
# Set SkuaPlayerLocal=<folder with the Mugetsu binary> to use a local player instead of downloading it.
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"
version="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -n 1)}"
out="$root/Build/linux-x64"
manager_out="$root/Build/manager-linux-x64"
player_local="${SkuaPlayerLocal:-${SKUA_PLAYER_LOCAL:-}}"

rm -rf "$out" "$manager_out"
publish_args=(-c Release -r linux-x64 --self-contained true)
if [[ -n "$player_local" ]]; then
    if [[ ! -d "$player_local" || ! -x "$player_local/Mugetsu" ]]; then
        echo "Error: SkuaPlayerLocal must point to a folder containing an executable Mugetsu." >&2
        exit 1
    fi
    publish_args+=("-p:SkuaPlayerLocal=$player_local")
    echo "Using local Mugetsu from $player_local"
fi
dotnet publish "$root/Skua.App.Avalonia/Skua.App.Avalonia.csproj" "${publish_args[@]}" -o "$out"
dotnet publish "$root/Skua.Manager.Avalonia/Skua.Manager.Avalonia.csproj" "${publish_args[@]}" -o "$manager_out"

# Plugins next to the app are copied into the data folder at startup (see PluginManager).
mkdir -p "$out/plugins"
for plugin in Skua.Plugin.DailyTracker Skua.Plugin.ButlerV5 Skua.Plugin.Cosmetics; do
    dotnet build "$root/$plugin/$plugin.csproj" -c Release
done
cp "$root/Skua.Plugin.DailyTracker/bin/Release/net10.0/Skua.Plugin.DailyTracker.dll" \
   "$root/Skua.Plugin.ButlerV5/bin/Release/net10.0/ButlerV5.dll" \
   "$root/Skua.Plugin.Cosmetics/bin/Release/net10.0/Skua.Plugin.Cosmetics.dll" \
   "$out/plugins/"

chmod +x "$out/Bonxie" "$out/player/Mugetsu"
if [[ -f "$out/player-x86-64-v2/Mugetsu" ]]; then chmod +x "$out/player-x86-64-v2/Mugetsu"; fi
archive="$root/Build/Bonxie-$version-linux-x64.tar.gz"
tar -C "$root/Build" --transform 's,^linux-x64,Bonxie,' --owner=0 --group=0 --numeric-owner -czf "$archive" linux-x64
echo "Built $archive"

# Build standalone AppImages for both the client and Manager, then bundle them
# into a folder, ZIP, and tarball for release distribution.
find_appimagetool() {
    if command -v appimagetool >/dev/null 2>&1; then
        command -v appimagetool
        return
    fi
    local cached="$root/Build/tools/appimagetool"
    if [[ ! -x "$cached" ]]; then
        mkdir -p "$(dirname "$cached")"
        curl -fsSL "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage" -o "$cached"
        chmod +x "$cached"
    fi
    echo "$cached"
}

appimagetool_cmd="$(find_appimagetool)"
skua_appdir="$root/Build/Bonxie.AppDir"
manager_appdir="$root/Build/Bonxie-Manager.AppDir"
rm -rf "$skua_appdir" "$manager_appdir"
cp -a "$out" "$skua_appdir"
cp -a "$manager_out" "$manager_appdir"

cat > "$skua_appdir/AppRun" <<'EOF'
#!/usr/bin/env bash
set -e
APPDIR="$(dirname "$(readlink -f "$0")")"
cd "$APPDIR"
export LD_LIBRARY_PATH="$APPDIR:$APPDIR/runtimes/linux-x64/native:${LD_LIBRARY_PATH:-}"
exec "$APPDIR/Bonxie" "$@"
EOF
cat > "$manager_appdir/AppRun" <<'EOF'
#!/usr/bin/env bash
set -e
APPDIR="$(dirname "$(readlink -f "$0")")"
cd "$APPDIR"
export LD_LIBRARY_PATH="$APPDIR:$APPDIR/runtimes/linux-x64/native:${LD_LIBRARY_PATH:-}"
exec "$APPDIR/Bonxie.Manager" "$@"
EOF
chmod +x "$skua_appdir/AppRun" "$manager_appdir/AppRun"
cp "$root/Skua.Shared.Avalonia/Assets/SkuaImage.png" "$skua_appdir/bonxie.png"
cp "$root/Skua.Shared.Avalonia/Assets/SkuaImage.png" "$manager_appdir/bonxie-manager.png"
cat > "$skua_appdir/bonxie.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=Bonxie
Comment=AQW client and bot
Exec=AppRun %U
Icon=bonxie
Terminal=false
Categories=Game;
EOF
cat > "$manager_appdir/bonxie-manager.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=Bonxie Manager
Comment=Bonxie account and script manager
Exec=AppRun %U
Icon=bonxie-manager
Terminal=false
Categories=Game;Utility;
EOF

# dotnet output may retain owner-only execute bits (0744); normalize before
# appimagetool validates the AppDir.
chmod -R a+rX "$skua_appdir" "$manager_appdir"
APPIMAGE_EXTRACT_AND_RUN="${APPIMAGE_EXTRACT_AND_RUN:-1}" ARCH=x86_64 "$appimagetool_cmd" -n "$skua_appdir" "$root/Build/Bonxie-x86_64.AppImage"
APPIMAGE_EXTRACT_AND_RUN="${APPIMAGE_EXTRACT_AND_RUN:-1}" ARCH=x86_64 "$appimagetool_cmd" -n "$manager_appdir" "$root/Build/Bonxie-Manager-x86_64.AppImage"
chmod +x "$root/Build/Bonxie-x86_64.AppImage" "$root/Build/Bonxie-Manager-x86_64.AppImage"
rm -rf "$skua_appdir" "$manager_appdir"

bundle="$root/Build/Bonxie-linux-AppImage"
rm -rf "$bundle"
mkdir -p "$bundle"
cp "$root/Build/Bonxie-x86_64.AppImage" "$root/Build/Bonxie-Manager-x86_64.AppImage" "$bundle/"
rm -f "$root/Build/Bonxie-linux-AppImage.zip" "$root/Build/Bonxie-linux-AppImage.tar.gz"
(cd "$root/Build" && zip -q -r Bonxie-linux-AppImage.zip Bonxie-linux-AppImage && tar -czf Bonxie-linux-AppImage.tar.gz Bonxie-linux-AppImage)
echo "Built $root/Build/Bonxie-x86_64.AppImage"
echo "Built $root/Build/Bonxie-Manager-x86_64.AppImage"
echo "Built $root/Build/Bonxie-linux-AppImage.{zip,tar.gz}"
