@echo off
setlocal

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run_checks.ps1" -Mode PreDeploy
exit /b %ERRORLEVEL%
