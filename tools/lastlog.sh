#!/usr/bin/env bash
LOG="/h/SteamLibrary/steamapps/common/Nivalis Nights/BepInEx/LogOutput.log"
grep -E "Loading \[|Error|Exception|Manager Order Fix|Use Oldest First|Nivalis Manager Debug|ModKit" "$LOG" \
  | grep -v "THIS WARNING IS NOT BUG" | tail -n "${1:-300}"