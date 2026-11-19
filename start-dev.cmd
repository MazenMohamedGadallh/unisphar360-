@echo off
rem One command to start both UniSphere apps:
rem   .NET API -> http://localhost:5080   React -> http://localhost:5173
rem Stop: press Ctrl+C in this window (or close it).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-dev.ps1"
