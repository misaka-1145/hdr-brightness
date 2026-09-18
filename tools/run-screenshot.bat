@echo off
rem Dev helper: render the main window to a PNG from a normal user process.
"%~dp0..\dist\HdrBrightness.exe" --screenshot "%~dp0..\shot.png"
"%~dp0..\dist\HdrBrightness.exe" --screenshot "%~dp0..\shot-demo.png" --demo
