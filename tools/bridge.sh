#!/usr/bin/env bash
# Query the kit's dev bridge in the running game. Usage: tools/bridge.sh [path], e.g.
#   tools/bridge.sh status    tools/bridge.sh "object?type=Nivalis.NotificationHudUi"
# Needs [DevBridge] Enabled = true in bgasm.nivalis.modkit.cfg.
curl -s --max-time 5 "http://127.0.0.1:5710/${1#/}" || echo "no answer: is the game running with the dev bridge enabled?"
