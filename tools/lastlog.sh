#!/usr/bin/env bash
LOG="/h/SteamLibrary/steamapps/common/Nivalis Nights/BepInEx/LogOutput.log"
grep -E "Loading \[|Error|Exception|Manager Order Fix|Better Supplier Choice|Use Oldest First|Nivalis Manager Debug|ModKit|Kit Tester" "$LOG" \
  | grep -v "THIS WARNING IS NOT BUG" | tail -n "${1:-300}"