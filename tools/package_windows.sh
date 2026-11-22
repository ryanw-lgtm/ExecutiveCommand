#!/usr/bin/env bash
# Builds the downloadable Windows package: game export + bundled AI engine + license notices.
# The AI model is NOT bundled (2.5 GB); the game downloads and verifies it on first launch.
# Usage: tools/package_windows.sh <godot binary> <output zip>
set -euo pipefail
GODOT=${1:?godot binary}; ZIP=${2:?output zip}
ROOT=$(cd "$(dirname "$0")/.." && pwd)
OUT="$ROOT/build/windows"
rm -rf "$OUT" && mkdir -p "$OUT"
"$GODOT" --headless --path "$ROOT/game" --export-release "Windows Desktop" "$OUT/ExecutiveCommand.exe"
mkdir -p "$OUT/runtime/llama" "$OUT/licenses"
cp "$ROOT/runtime/llama-win/llama-server.exe" "$OUT/runtime/llama/"
cp "$ROOT/runtime/llama-win/LICENSE-llama.cpp.txt" "$OUT/licenses/"
cp "$ROOT/THIRD_PARTY_NOTICES.md" "$OUT/licenses/"
cp "$ROOT/game/assets/fonts/OFL-IBM-Plex.txt" "$OUT/licenses/"
cp "$ROOT/game/assets/icons/LICENSE-Phosphor.txt" "$OUT/licenses/"
cat > "$OUT/licenses/MODEL-NOTICE.txt" <<'NOTE'
The AI model is downloaded on first launch from Hugging Face:
  Qwen3-4B-Instruct-2507 (Q4_K_M GGUF by unsloth, revision a06e946bb6b655725eafa393f4a9745d460374c9)
  Licensed under the Apache License 2.0 by the Qwen team (Alibaba Cloud).
  SHA-256 3605803b982cb64aead44f6c1b2ae36e3acdb41d8e46c8a94c6533bc4c67e597 is verified before use.
NOTE
rm -f "$ZIP"
(cd "$ROOT/build" && zip -qr -9 "$ZIP" windows)
unzip -tq "$ZIP"
