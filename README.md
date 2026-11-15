# Yandere Simulator Ultimate Trainer

Entrenador de escritorio para **Yandere Simulator**, desarrollado en **C# / WPF (.NET 8)** y acompañado de un complemento para el juego basado en **BepInEx y Harmony**.

El proyecto reúne una interfaz de control, un puente de comunicación local con el juego y herramientas para administrar estadísticas, recursos, NPC, tiempo y otros parámetros disponibles en la versión compatible.

> **Estado:** versión 1.1.0 ([cambios](CHANGELOG.md))  
> **Builds verificadas:** Yandere Simulator 530 y 532 · Unity 6000.2.8f1 · Mono x64 (otras builds: modo «no verificada»)  
> **Plataforma:** Windows x64

---

## Características principales

- **Estadísticas y progreso:** reputación, seducción, iluminación, estado mental, materias y puntos de estudio.
- **Recursos:** dinero y puntos de información, con opciones de edición y bloqueo cuando están disponibles.
- **NPC:** consulta de información, controles individuales, congelación, relaciones y herramientas de teletransporte.
- **Comportamiento:** controles relacionados con visión, audición, testigos, profesores y otros sistemas identificados del juego.
- **Tiempo y entorno:** congelar o ajustar el reloj y modificar parámetros de la atmósfera escolar.
- **Perfiles:** guardar configuraciones locales y reutilizarlas.
- **Conexión:** estado siempre visible (desconectado, conectando, conectado o incompatible), reconexión automática y canal local autenticado.
- **Instalación:** instalar, actualizar y desinstalar el complemento desde la aplicación, respetando otros mods.
- **Interfaz:** pestañas, buscador de NPC, deslizadores, tema oscuro y claro, atajos de teclado y botón «Restaurar todo» siempre visible.

La disponibilidad de cada control depende de la escena y de las referencias que el juego tenga cargadas. Las opciones no disponibles se muestran deshabilitadas con el motivo; no se presentan valores ficticios.

## Descarga e instalación

1. Descargá `YandereSimulatorUltimateTrainer-1.1.0.zip` desde [Releases](https://github.com/Jamssalin03/Ultimate-Trainer-Yandere-sim/releases).
2. Opcional: comprobá la huella del archivo con `Get-FileHash` y `SHA256SUMS.txt` (ver [falsos positivos](#falsos-positivos-del-antivirus)).
3. Extraé el `.zip` en una carpeta propia (no dentro de la carpeta del juego) y ejecutá `YandereSimulatorUltimateTrainer.exe`.
4. Con el juego cerrado, abrí **Ajustes**, comprobá la carpeta del juego (se detecta automáticamente o con **Buscar…**) y pulsá **Instalar complemento**. Si venís de la 1.0.0, usá **Actualizar complemento**.
5. Abrí el juego: el entrenador se conecta solo. Entrá en una partida para usar los controles disponibles.

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

| Componente | Versión |
|---|---|
| Entrenador | 1.1.0 |
| Yandere Simulator | Builds 530 (15/10/2026) y 532 (31/10/2026) |
| Unity | 6000.2.8f1 |
| Arquitectura / runtime | x64 · Mono |
| BepInEx | 5.4.23.5 |

El complemento calcula la huella SHA-256 de `Assembly-CSharp.dll` y la busca en la lista de builds conocidas de [compatibilidad.json](compatibilidad.json):

- **Build verificada:** todas las funciones disponibles.
- **Build no verificada:** el complemento comprueba con AccessTools cada tipo, campo y método que usa cada función y activa sólo las que existen. El menú indica cuáles quedaron desactivadas y por qué (**Ajustes › Compatibilidad**).
- **Incompatible:** ninguna función disponible, o el complemento instalado es de otra versión del protocolo; las modificaciones quedan bloqueadas.

**No se garantiza compatibilidad con builds futuras ni con el modo 1980 completo.** Consultá [LEEME.md](LEEME.md) para los límites funcionales.

## Compilar el proyecto

El código fuente se encuentra en la carpeta `source`.

Podés abrir la solución:

`source/YandereUltimate.sln`

También podés utilizar el script incluido desde PowerShell. Detecta la instalación del juego (variable `YANDERE_SIM_DIR`, ruta guardada por el entrenador o carpetas habituales del launcher), compila sin advertencias, ejecuta las pruebas e inspecciona las referencias contra la build instalada:

```powershell
.\Compilar.ps1
```

Para indicar una instalación concreta y generar el `.zip` de Release en `dist\`:

```powershell
.\Compilar.ps1 -Juego "C:\ruta\al\juego" -Paquete
```

La compilación genera los artefactos intermedios en una carpeta temporal del sistema. Para conocer los detalles de la arquitectura, los componentes y el proceso de compilación, revisá [LEEME.md](LEEME.md).

## Estructura del repositorio

```text
.
├── Loader/             # Componentes del cargador y dependencias
├── capturas/           # Imágenes de referencia y pruebas
├── source/
│   ├── Bridge/         # Complemento BepInEx: canal local, acceso al juego, parches
│   ├── Comun/          # Lista blanca del protocolo y catálogo de funciones (compartido)
│   ├── Desktop/        # Aplicación de escritorio WPF
│   ├── Inspector/      # Comparación de referencias contra una build (Mono.Cecil)
│   ├── Pruebas/        # Pruebas automáticas (xUnit)
│   ├── Simulador/      # Simulador del juego para probar la interfaz (no se distribuye)
│   └── YandereUltimate.sln
├── CHANGELOG.md        # Registro de cambios
├── Compilar.ps1        # Compilación, pruebas y paquete
├── compatibilidad.json # Builds conocidas y datos del proyecto
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

## Falsos positivos del antivirus

Algunos antivirus (incluido Windows Defender con la protección en la nube) marcan los entrenadores de juegos como «HackTool», «Trainer» o «Riskware». No es un error de este proyecto en particular: la detección se basa en el comportamiento típico de estas herramientas.

**Por qué ocurre:**

- El paquete incluye `winhttp.dll` de UnityDoorstop, que el juego carga al iniciar para arrancar BepInEx (técnica de *DLL proxy*).
- El complemento modifica el comportamiento del juego en memoria con Harmony.
- El ejecutable es autónomo (.NET empaquetado en un único archivo autoextraíble), nuevo y sin firma digital, por lo que tiene poca «reputación».

**Qué hace y qué no hace:** no lee ni escribe memoria de otros procesos, no se conecta a Internet, no pide permisos de administrador, no cambia ajustes del antivirus y se comunica con el juego sólo por un canal local restringido a tu usuario. Todo el código está en este repositorio.

**Cómo verificar tu descarga:**

1. Descargá el `.zip` sólo desde [Releases](https://github.com/Jamssalin03/Ultimate-Trainer-Yandere-sim/releases) de este repositorio.
2. Comprobá la huella: `Get-FileHash .\YandereSimulatorUltimateTrainer-1.1.0.zip -Algorithm SHA256` debe coincidir con la publicada en la Release; dentro del paquete, `SHA256SUMS.txt` lista la huella de cada archivo.
3. Si preferís no confiar en binarios, compilalo vos con `Compilar.ps1`.
4. Si tu antivirus lo bloquea, podés enviarlo como falso positivo (por ejemplo, en el portal de envíos de Microsoft). No desactives el antivirus en general: como mucho, agregá una exclusión limitada a la carpeta del entrenador, y sólo si verificaste la huella.

## Documentación

- [Guía de uso y documentación técnica](LEEME.md)
- [Registro de cambios](CHANGELOG.md)
- [Compatibilidad detallada](compatibilidad.json)
- [Registro de pruebas](PRUEBAS.md)
- [Licencias de dependencias](LICENCIAS.txt)

## Aviso

Este es un proyecto independiente y no oficial. No está afiliado, respaldado ni aprobado por los desarrolladores de Yandere Simulator. Yandere Simulator y sus marcas pertenecen a sus respectivos titulares.

Las dependencias de terceros conservan sus respectivas licencias. Consultá `LICENCIAS.txt` para más información.
