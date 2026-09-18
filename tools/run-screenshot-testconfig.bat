@echo off
rem Dev helper: run the screenshot mode against a throwaway config folder (APPDATA override),
rem so the developer config with a pre-made group can be rendered without touching the real one.
set HDRBRIGHTNESS_CONFIG_DIR=%~dp0..\.testappdata
"%~dp0..\dist\HdrBrightness.exe" --screenshot "%~dp0..\shot-group.png" --full
"%~dp0..\dist\HdrBrightness.exe" --screenshot-settings "%~dp0..\shot-settings.png"
"%~dp0..\dist\HdrBrightness.exe" --screenshot-schedule "%~dp0..\shot-schedule.png"
