# DellBatteryLimiter

Utilidad ultraligera (~13 KB) para la bandeja del sistema (System Tray) de Windows que permite alternar el límite de carga de batería en notebooks Dell con un solo clic, sin bloatware ni servicios pesados.

Utiliza el controlador oficial de Dell (`cctk.exe` de *Dell Command | Configure*) para aplicar los umbrales directamente en el chip de carga (Embedded Controller / BIOS).

## Características

- 🟢 **Modo Limitado (50% - 80%)**: Corta la carga al 80% y no vuelve a cargar hasta que baje del 50%. Ideal para usar la laptop enchufada a corriente continua.
- 🔴 **Modo Deslimitado (100%)**: Carga completa estándar para cuando necesitas salir de viaje.
- **Sin interfaz visual**: Vive exclusivamente en el área de notificaciones (tray).
  - **Clic izquierdo**: Alterna al instante entre 🟢 y 🔴.
  - **Clic derecho**: Menú con opciones explícitas, opción de iniciar con Windows y salir.
- **Scripts independientes**: Incluye scripts `.cmd` que se pueden ejecutar o anclar al Menú Inicio.
- **Cero dependencias pesadas**: Escrito en C# nativo y compilable con el compilador que ya incluye Windows (`csc.exe`), sin requerir Visual Studio.

## Requisitos

- Laptop Dell compatible con Dell Command | Configure.
- `Dell Command | Configure` instalado (`winget install -e --id Dell.CommandConfigure`).

## Compilación

Para compilar desde la consola usando el compilador nativo de Windows:

```cmd
build.bat
```

o manualmente:

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /codepage:65001 /target:winexe /optimize+ /out:DellBatteryTray.exe /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll DellBatteryTray.cs
```
