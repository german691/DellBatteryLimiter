@echo off
set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
"%CSC%" /nologo /codepage:65001 /target:winexe /win32manifest:app.manifest /optimize+ /out:"DellBatteryTray.exe" /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "DellBatteryTray.cs"
if %errorlevel% equ 0 (
    echo Compilado exitosamente: DellBatteryTray.exe
) else (
    echo Error al compilar.
)
