# Registro de implementación y pruebas

Fecha de trabajo: 17 de octubre de 2026. Juego local: build 530, publicada
el 15/10/2026. Este registro distingue código compilado de pruebas jugables.

## Comprobado en esta computadora

- Instalación localizada y archivos inspeccionados. Cabecera PE del juego:
  `8664` (x64). Existe `MonoBleedingEdge` y `Managed/Assembly-CSharp.dll`;
  no usa IL2CPP en esta instalación. Unity informa `6000.2.8f1` en ejecución.
- Ensamblado inspeccionado por metadatos y descompilación local. SHA-256:
  `62926A9CC29156037C340E7BD177D88D760D03618FCDFF0C2CD66CCAB78CED09`.
- Solución C# y proyectos WPF .NET 8 / complemento .NET Framework 4.7.2:
  compilación correcta. Publicación autónoma x64 con bibliotecas nativas WPF
  dentro del ejecutable; la versión inicial que omitía esas bibliotecas se
  descartó después de observar errores de carga.
- Instalación efectiva de BepInEx 5.4.23.5 y del complemento. Se crearon
  respaldos de PlayerPrefs, ensamblado original y versión antes de instalar.
- El registro de BepInEx confirmó la carga del complemento y `Compatibilidad:
  True`. La API incompleta de identidad de Mono se sustituyó por ACL nativa.
- Canal local real: lecturas de build, motor, modo 202X, perfil 1, escena y
  valores persistentes desde el proceso del juego, con solicitudes autenticadas.
- Cambio de escena `ResolutionScene` → `NewTitleScene` con conexión recuperada.
- Prueba de reputación persistente: original `−10`, cambio a `42`, lectura
  posterior `42` y restauración posterior `−10`.
- Repetición de esa prueba desde la ventana WPF final: introducir `42`,
  pulsar Aplicar, observar `Actual: 42.0`, leer de nuevo `42` por otro cliente
  del puente, pulsar Restaurar y confirmar `−10` desde el juego.
- Interfaz real ejecutada y capturada. Se comprobaron navegación a estadísticas,
  estado conectado y deshabilitación de controles sin referencias de escena.
  Archivos: `capturas/conexion-real.jpg` y `capturas/reputacion-real-42.jpg`.
- Aperturas repetidas del juego y restablecimiento del canal con un nombre y
  credencial nuevos. El ensamblado original del juego conserva su SHA-256.
- Prueba final en `SchoolScene`, 202X, con **86 NPC reales**. La aplicación
  final volvió a conectarse después de cerrarla normalmente y abrirla de nuevo.
  Se comprobó que el puente responde mientras la ventana externa está activa.
- Reputación actual: `−10` → `42`, confirmada visualmente en el HUD escolar
  como `REPUTATION: 42.0`; restauración final a `−10`.
- Puntos de estudio: `0` → `500`, confirmados por lectura del `ClassScript`
  cargado; restauración a `0`. Queda pendiente consumirlos en la clase.
- Puntos de información: `0` → `500`, confirmados visualmente en Servicios.
  Se compró información de Sakyu Basu; apareció el mensaje de Info-chan y,
  al volver a Servicios, el saldo seguía en **500** con bloqueo activo.
  Después se restauró el saldo a `0`. La información de Sakyu obtenida por
  esa compra no se borró: es un efecto de la compra de prueba.
- Dinero: `10` → `100`, confirmado en el HUD del teléfono como `$100.00`;
  restauración a `10`.
- Biología: progreso `0` → `80`, lectura de nivel `4`, restauración a `0/0`.
- Congelación global: **86/86** NPC registrados como congelados y **86/86**
  posiciones idénticas entre dos lecturas. Tras desactivarla: **0 congelados**
  y **70 NPC con desplazamiento** en la siguiente lectura.
- Congelación del reloj: `TimeSpeed` pasó de `3.5` a `0`, y la hora permaneció
  en `7.027718607584635` entre las lecturas; restauración a `3.5`.
- «Personajes estúpidos»: activar/desactivar confirmado por el puente en
  escuela, sin errores registrados. Esto NO verifica una escena de detección
  de delitos ni la reacción posterior; esa prueba sigue pendiente.
- Restauración global final: reputación `−10`, dinero `10`, estudio `0`,
  información `0`, Biología `0/0`, reloj `3.5`, cero interruptores activos,
  cero bloqueos y cero NPC congelados.

## Problemas encontrados y correcciones

1. `WindowsIdentity.User` no está implementado por Mono de esta Unity. Se
   reemplazó por Win32 para SID, ACL de archivo y creación de canal local.
2. La publicación WPF necesitaba incluir bibliotecas nativas; se activó
   `IncludeNativeLibrariesForSelfExtract` y se probó la app final desde outputs.
3. Al cargar escuela, `YandereScript.Start` desactiva `runInBackground`.
   Se identificó esa llamada en el IL y se añadió un postfix Harmony que lo
   reactiva, además de mantenerlo durante Update. Posteriormente se comprobó
   conexión en escuela y funcionamiento al cambiar a la ventana del entrenador.
4. Una instancia de desarrollo no completó el cierre normal. Se retiró la
   liberación síncrona del canal del callback principal de salida. Fue necesario
   terminar los procesos de prueba bloqueados. No se modificaron estadísticas
   de escuela durante ese intento. El cierre normal y reapertura de la app
   final sí se comprobó en escuela; el cierre del juego con el puente final
   y la estabilidad prolongada continúan pendientes.

## Implementado; pendiente de prueba jugable completa

| Función | Implementación real | Verificación pendiente |
|---|---|---|
| Reputación actual | `ReputationScript.Reputation`, etiqueta y marcador | HUD 42 y restauración comprobados; repetir en 1980 |
| Puntos académicos | `ClassScript.StudyPoints`, `StartingPoints`, `UpdateLabel` | Asignar 500 comprobado; falta consumirlos en clase |
| Materias | Campos y grados de `ClassScript`, propiedades de `ClassGlobals`, `Subject` y barras | Comprobar beneficios individuales |
| Puntos de información | `InventoryScript.PantyShots`, `PlayerGlobals`, etiquetas de Services | Compra de información comprobada; faltan otros servicios |
| Puntos que no disminuyen | Bloqueo mínimo y parche de `ServicesScript.Purchase` | Saldo 500 tras compra comprobado; probar otras rutas de compra |
| Dinero | Inventario, valor persistente y `UpdateMoney()` | HUD comprobado; falta comprar objetos |
| Personajes estúpidos | Parches de visión, audición, testigos, persecución, proximidad e intervención | Arma frente a estudiante y profesor; activar/desactivar y repetir |
| Controles separados de IA | Condiciones por categoría, Senpai y NPC individual | Verificar cada grupo sin afectar a los demás |
| Congelar NPC | Script, navegación, animaciones y posición; estado original registrado | Global comprobado; faltan grupos individuales y escenas especiales |
| Relaciones y velocidad de NPC | Métodos reales de StudentGlobals y campos de StudentScript | Comprobar efectos en conversación y rutina |
| Teletransporte | Destino del juego o suelo próximo al NPC seleccionado | Validación de colisiones en distintos pisos |
| Protagonista | Sanity, salud, caminar/correr y penalizaciones pendientes | Combate y movimiento en partida |
| Tiempo | `ClockScript` y límites de hora; velocidad y congelación | Congelación/restauración comprobadas; faltan saltos y eventos |
| Escuela | `SchoolGlobals.SchoolAtmosphere` y gestor de estudiantes | Comprobar cambios perceptivos en escuela |
| Perfiles | Guardado JSON, carga explícita y aplicación de valores disponibles | Recorrido completo después de reiniciar la app |
| Desinstalación | Manifiesto, comprobación de hashes y retirada selectiva | Ensayo completo tras terminar las pruebas |
| 1980 | Detección del modo y exclusión de puntos de Info-chan | Sesión completa de ese modo |

No se afirma haber probado la detección de delitos o la utilización de puntos
de clase sin observarlas. Las funciones pendientes son experimentales hasta
completar esas pruebas; las verificaciones parciales no equivalen a comprobar
todos los casos del juego.
