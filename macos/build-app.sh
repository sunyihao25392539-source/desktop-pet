#!/bin/bash
set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"
repo_dir="$(cd "$script_dir/.." && pwd)"
cd "$script_dir"

swift test
swift build -c release

build_dir="$(swift build -c release --show-bin-path)"
app_dir="$script_dir/dist/GoldenMonkeyPet.app"
archive_dir="$script_dir/dist"
rm -rf "$app_dir"
mkdir -p "$app_dir/Contents/MacOS" "$app_dir/Contents/Resources"

cp "$build_dir/GoldenMonkeyPet" "$app_dir/Contents/MacOS/GoldenMonkeyPet"
cp "$build_dir/GoldenMonkeyCodexHook" "$app_dir/Contents/MacOS/GoldenMonkeyCodexHook"
cp "$script_dir/Info.plist" "$app_dir/Contents/Info.plist"
cp -R "$repo_dir/assets" "$app_dir/Contents/Resources/assets"
chmod +x "$app_dir/Contents/MacOS/GoldenMonkeyPet" "$app_dir/Contents/MacOS/GoldenMonkeyCodexHook"

# Ad-hoc signing avoids a damaged-app warning for local testing. Public releases
# can replace this with Developer ID signing and Apple notarization.
codesign --force --deep --sign - "$app_dir"

arch="$(uname -m)"
archive="$archive_dir/GoldenMonkeyPet-macOS-$arch.zip"
rm -f "$archive"
ditto -c -k --sequesterRsrc --keepParent "$app_dir" "$archive"
echo "$archive"
