#!/usr/bin/env bash
# Query the kit's dev bridge in the running game. Usage:
#   tools/bridge.sh [path]                 read, e.g. status, "ui?panel=SettingsPanel&rects", "object?type=Nivalis.NotificationHudUi"
#   tools/bridge.sh cmd                    list dev commands
#   tools/bridge.sh cmd NAME [k=v ...]     run one, e.g. cmd demo style=Panel, cmd clock speed=2, cmd open what=Map
# Needs [DevBridge] Enabled = true in bgasm.nivalis.modkit.cfg; commands also need AllowCommands = true.
# GAME_DIR overrides the game folder (for the command token in BepInEx/cache).
GAME_DIR="${GAME_DIR:-/h/SteamLibrary/steamapps/common/Nivalis Nights}"
BASE="http://127.0.0.1:5710"
fail() { echo "no answer: is the game running with the dev bridge enabled?"; }

if [ "$1" = "cmd" ]; then
  shift
  if [ -z "$1" ]; then curl -s --max-time 5 "$BASE/cmd" || fail; exit; fi
  name="$1"; shift
  token_file="$GAME_DIR/BepInEx/cache/nivalismodkit-bridge.token"
  [ -f "$token_file" ] || { echo "no token at $token_file: start the game with [DevBridge] Enabled"; exit 1; }
  args=()
  for kv in "$@"; do args+=(--data-urlencode "$kv"); done   # k=v pairs, encoded into the query string by -G
  curl -s --max-time 5 -G -X POST -H "X-Kit-Token: $(cat "$token_file")" "${args[@]}" "$BASE/cmd/$name" || fail
  exit
fi

p="${1#/}"; curl -s --max-time 5 "$BASE/${p// /%20}" || fail
