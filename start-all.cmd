@echo off
rem One command to start the FULL UniSphere stack:
rem   Python RAG -> http://127.0.0.1:8000
rem   .NET API   -> http://localhost:5080   React -> http://localhost:5173
rem Stop: press Ctrl+C in this window (or close it).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-all.ps1"
