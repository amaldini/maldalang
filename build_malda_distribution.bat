@echo off
setlocal
REM Build a downloadable MALDA CLI zip (self-contained). See scripts/build-oss-dist.ps1.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build-oss-dist.ps1" %*
exit /b %ERRORLEVEL%
