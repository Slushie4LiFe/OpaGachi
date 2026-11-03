@echo off
rem Starts OpaGachi (builds it first if needed). Pass --demo to play the walkthrough.
cd /d "%~dp0"
if not exist bin\OpaGachi.exe call build.cmd || exit /b 1
start "" bin\OpaGachi.exe %*
