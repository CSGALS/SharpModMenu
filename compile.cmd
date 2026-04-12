@echo off
setlocal

set "ROOT=%~dp0"
set "PS_SCRIPT=%ROOT%compile.ps1"

if not exist "%PS_SCRIPT%" (
	echo [ERROR] Missing script: "%PS_SCRIPT%"
	exit /b 1
)

where pwsh >nul 2>&1
if %ERRORLEVEL% EQU 0 (
	pwsh -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
) else (
	powershell -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
)

set "EXIT_CODE=%ERRORLEVEL%"
endlocal & exit /b %EXIT_CODE%
