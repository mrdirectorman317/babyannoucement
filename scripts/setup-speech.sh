#!/bin/bash
# Installs local speech-to-text (whisper.cpp via Homebrew + a small English model) and points the host at it.
# Audio never leaves your Mac in this mode. Inspect before running. Optional: skip this and use the
# "Use OpenAI for speech-to-text" toggle instead (audio then goes to OpenAI).
set -euo pipefail
[[ "$(uname -s)" == Darwin ]] || { echo 'Run on the Mac that hosts SpatialMac.' >&2; exit 1; }
command -v brew >/dev/null || { echo 'Install Homebrew first: https://brew.sh' >&2; exit 1; }
BASE="$HOME/Library/Application Support/SpatialMac"
[[ -f "$BASE/host-settings.json" ]] || { echo 'Run scripts/setup-mac.sh first.' >&2; exit 1; }
brew list whisper-cpp >/dev/null 2>&1 || brew install whisper-cpp
BIN="$(command -v whisper-cli || true)"
[[ -x "$BIN" ]] || { echo 'whisper-cli not found after install; check `brew info whisper-cpp`.' >&2; exit 1; }
MODEL_DIR="$BASE/models"; mkdir -p "$MODEL_DIR"; chmod 700 "$MODEL_DIR"
MODEL="$MODEL_DIR/ggml-base.en.bin"
if [[ ! -f "$MODEL" ]]; then
  curl -L --fail --proto '=https' -o "$MODEL.part" \
    "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin"
  mv "$MODEL.part" "$MODEL"
fi
echo "Model SHA-256 (compare with the whisper.cpp model list if you want to pin it):"
shasum -a 256 "$MODEL"
python3 - "$BASE/host-settings.json" "$BIN" "$MODEL" <<'PYCODE'
import json,os,sys
path,binary,model=sys.argv[1:]
d=json.load(open(path));d['whisperExecutable']=binary;d['whisperModel']=model
json.dump(d,open(path,'w'),indent=2);open(path,'a').write('\n');os.chmod(path,0o600)
print('Configured local speech. Restart the host, then enable "Allow headset voice dictation".')
PYCODE
