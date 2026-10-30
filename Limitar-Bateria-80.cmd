@echo off
setlocal
set "CCTK=C:\Program Files (x86)\Dell\Command Configure\X86_64\cctk.exe"
set "STATE_DIR=%LOCALAPPDATA%\DellBatteryLimiter"
set "STATE_FILE=%STATE_DIR%\state.txt"
set "RANGE=%~1"
if "%RANGE%"=="" set "RANGE=50-80"

:: Verificar privilegios de Administrador
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -ArgumentList '%RANGE%' -Verb RunAs -WindowStyle Hidden"
    exit /b
)

if not exist "%STATE_DIR%" mkdir "%STATE_DIR%"
"%CCTK%" --PrimaryBattChargeCfg=Custom:%RANGE%
if %errorlevel% equ 0 (
    > "%STATE_FILE%" echo LIMITED:%RANGE%
)
exit /b
