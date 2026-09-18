@echo off
rem Dev helper: run the CLI debug dump from a normal user process (launched by
rem explorer.exe) so the sandbox job/token is not involved. ASCII-only by design.
"%~dp0..\dist\hdrbright.exe" debug > "%~dp0..\desktop-debug-user.txt" 2>&1
