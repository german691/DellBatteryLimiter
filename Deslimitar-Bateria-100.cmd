@echo off
setlocal
set "CCTK=C:\Program Files (x86)\Dell\Command Configure\X86_64\cctk.exe"
set "STATE_DIR=%LOCALAPPDATA%\DellBatteryLimiter"
set "STATE_FILE=%STATE_DIR%\state.txt"

:: Verificar privilegios de Administrador
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs -WindowStyle Hidden"
    exit /b
)

if not exist "%STATE_DIR%" mkdir "%STATE_DIR%"
"%CCTK%" --PrimaryBattChargeCfg=Adaptive
if %errorlevel% neq 0 (
    "%CCTK%" --PrimaryBattChargeCfg=Standard
)
if %errorlevel% equ 0 (
    > "%STATE_FILE%" echo UNLIMITED
)
exit /b
