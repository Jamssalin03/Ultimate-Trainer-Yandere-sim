# Yandere Simulator Ultimate Trainer

Entrenador de escritorio para **Yandere Simulator**, desarrollado en **C# / WPF (.NET 8)** y acompañado de un complemento para el juego basado en **BepInEx y Harmony**.

El proyecto reúne una interfaz de control, un puente de comunicación local con el juego y herramientas para administrar estadísticas, recursos, NPC, tiempo y otros parámetros disponibles en la versión compatible.

> **Estado:** versión 1.0.0  
> **Compatibilidad comprobada:** Yandere Simulator Build 530 · Unity 6000.2.8f1 · Mono x64  
> **Plataforma:** Windows x64

---

## Características principales

- **Estadísticas y progreso:** reputación, seducción, iluminación, estado mental, materias y puntos de estudio.
- **Recursos:** dinero y puntos de información, con opciones de edición y bloqueo cuando están disponibles.
- **NPC:** consulta de información, controles individuales, congelación, relaciones y herramientas de teletransporte.
- **Comportamiento:** controles relacionados con visión, audición, testigos, profesores y otros sistemas identificados del juego.
- **Tiempo y entorno:** congelar o ajustar el reloj y modificar parámetros de la atmósfera escolar.
- **Perfiles:** guardar configuraciones locales y reutilizarlas.
- **Conexión:** detección, conexión y reconexión mediante un canal local autenticado.
- **Instalación:** herramientas para instalar y desinstalar el complemento desde la aplicación.

La disponibilidad de cada control depende de la escena y de las referencias que el juego tenga cargadas. Las opciones no disponibles se muestran deshabilitadas; no se presentan valores ficticios.

## Descarga e instalación

1. Descargá el archivo `YandereSimulatorUltimateTrainer.rar` desde la sección de archivos de este repositorio.
2. Extraé el contenido con WinRAR o una herramienta compatible.
3. Ejecutá `YandereSimulatorUltimateTrainer.exe`.
4. Para instalar el complemento en otra copia del juego, cerrá el juego, seleccioná su ejecutable mediante **Buscar instalación** y utilizá **Instalar complemento**.
5. Abrí el juego y conectá el entrenador. Después, entrá en una partida para utilizar los controles disponibles.

**Importante:** utilizá una copia de seguridad de tus datos antes de probar modificaciones. Algunas acciones pueden afectar el estado de la partida y no todas las consecuencias del juego se pueden revertir.

## Requisitos

### Para utilizar la versión publicada

- Windows de 64 bits.
- Una instalación de Yandere Simulator compatible.
- Permisos de lectura y escritura en la carpeta del juego y en los datos locales del usuario.

La aplicación de escritorio se publica como ejecutable autónomo; no necesitás instalar Visual Studio para ejecutarla.

### Para compilar desde el código fuente

- Visual Studio con herramientas de desarrollo de escritorio .NET, o .NET SDK 8.
- Windows x64.
- Acceso a NuGet para restaurar las dependencias.
- Una instalación local del juego compatible para compilar el complemento.

## Compatibilidad

La versión actual está comprobada para esta configuración:

| Componente | Versión |
|---|---|
| Entrenador | 1.0.0 |
| Yandere Simulator | Build 530 |
| Fecha de build del juego | 15/10/2026 |
| Unity | 6000.2.8f1 |
| Arquitectura | x64 |
| Runtime del juego | Mono |
| BepInEx | 5.4.23.5 |

El complemento verifica la huella SHA-256 del ensamblado del juego. Si detecta una versión diferente, puede bloquear las modificaciones para evitar operaciones incompatibles.

**No se garantiza compatibilidad con otras builds, versiones futuras o el modo 1980 completo.** Consultá [compatibilidad.json](compatibilidad.json) para los datos técnicos y [LEEME.md](LEEME.md) para conocer los límites funcionales.

## Compilar el proyecto

El código fuente se encuentra en la carpeta `source`.

Podés abrir la solución:

`source/YandereUltimate.sln`

También podés utilizar el script incluido desde PowerShell:

```powershell
.\Compilar.ps1
```

Para indicar una instalación concreta del juego:

```powershell
.\Compilar.ps1 -Juego "C:\ruta\al\juego"
```

La compilación genera los artefactos intermedios en una carpeta temporal del sistema. Para conocer los detalles de la arquitectura, los componentes y el proceso de compilación, revisá [LEEME.md](LEEME.md).

## Estructura del repositorio

```text
.
├── Loader/             # Componentes del cargador y dependencias
├── capturas/           # Imágenes de referencia y pruebas
├── source/
│   ├── Bridge/         # Comunicación e integración con el juego
│   ├── Desktop/        # Aplicación de escritorio WPF
│   └── YandereUltimate.sln
├── Compilar.ps1        # Script de compilación
├── compatibilidad.json # Datos de compatibilidad
├── LEEME.md            # Documentación técnica y guía detallada
├── LICENCIAS.txt       # Licencias de dependencias
└── PRUEBAS.md          # Registro de pruebas
```

## Seguridad, datos y respaldos

- La comunicación entre la aplicación y el complemento utiliza un canal local autenticado; el proyecto no depende de servicios externos para esa comunicación.
- Los ajustes y perfiles se guardan en `%LOCALAPPDATA%\YandereUltimate`.
- Antes de instalar, el proyecto contempla respaldos de archivos y preferencias relevantes.
- El archivo `endpoint.json` contiene credenciales efímeras del canal local: **no lo compartas**.
- La desinstalación comprueba los hashes de los archivos añadidos y evita retirar archivos que hayan cambiado.

Leé la sección de respaldos y desinstalación de [LEEME.md](LEEME.md) antes de modificar archivos del juego o restaurar preferencias.

## Documentación

- [Guía de uso y documentación técnica](LEEME.md)
- [Compatibilidad detallada](compatibilidad.json)
- [Registro de pruebas](PRUEBAS.md)
- [Licencias de dependencias](LICENCIAS.txt)

## Aviso

Este es un proyecto independiente y no oficial. No está afiliado, respaldado ni aprobado por los desarrolladores de Yandere Simulator. Yandere Simulator y sus marcas pertenecen a sus respectivos titulares.

Las dependencias de terceros conservan sus respectivas licencias. Consultá `LICENCIAS.txt` para más información.
