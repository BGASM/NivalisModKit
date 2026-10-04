@echo off
rem Run a Nivalis ModKit dev command: kit demo style=Panel   (kit alone lists them). See kit.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0kit.ps1" %*
