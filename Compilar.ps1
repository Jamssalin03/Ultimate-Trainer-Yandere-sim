<#
.SYNOPSIS
    Compila el entrenador y el complemento, ejecuta las pruebas, inspecciona la build del juego y,
    opcionalmente, genera el paquete .zip para publicar como Release.

.PARAMETER Juego
    Carpeta del juego (la que contiene YandereSimulator.exe). Si se omite, se detecta en este orden:
    variable YANDERE_SIM_DIR, ruta guardada por el entrenador (%LOCALAPPDATA%\YandereUltimate\config.json)
    y carpetas habituales del launcher (Descargas, Escritorio, Documentos, raíz de cada unidad).

.PARAMETER Paquete
    Además genera dist\YandereSimulatorUltimateTrainer-<versión>.zip con SHA256SUMS.txt.

.PARAMETER SinPruebas
    Omite las pruebas automáticas (no recomendado).

.EXAMPLE
    .\Compilar.ps1
.EXAMPLE
    .\Compilar.ps1 -Juego "D:\Juegos\YandereSimLauncher\YandereSimulator" -Paquete
#>
[CmdletBinding()]
param(
    [string]$Juego,
    [switch]$Paquete,
    [switch]$SinPruebas
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$raiz = $PSScriptRoot
$source = Join-Path $raiz 'source'
$temp = Join-Path ([IO.Path]::GetTempPath()) 'ys-ultimate'
$compat = Get-Content -LiteralPath (Join-Path $raiz 'compatibilidad.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$version = $compat.version

function Test-Juego([string]$ruta) {
    return $ruta -and (Test-Path -LiteralPath (Join-Path $ruta 'YandereSimulator_Data/Managed/Assembly-CSharp.dll'))
}

function Find-Juego {
    $candidatas = [Collections.Generic.List[string]]::new()
    if ($env:YANDERE_SIM_DIR) { $candidatas.Add($env:YANDERE_SIM_DIR) }
    $config = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'YandereUltimate/config.json'
    if (Test-Path -LiteralPath $config) {
        try { $guardada = (Get-Content -LiteralPath $config -Raw | ConvertFrom-Json).installation; if ($guardada) { $candidatas.Add($guardada) } } catch { }
    }
    $usuario = [Environment]::GetFolderPath('UserProfile')
    foreach ($b in 'Downloads', 'Descargas', 'Desktop', 'Escritorio', 'Documents', 'Documentos', '') {
        $candidatas.Add((Join-Path $usuario (Join-Path $b 'YandereSimLauncher/YandereSimulator')))
        $candidatas.Add((Join-Path $usuario (Join-Path $b 'YandereSimulator')))
    }
    foreach ($unidad in [IO.DriveInfo]::GetDrives() | Where-Object { $_.DriveType -eq 'Fixed' -and $_.IsReady }) {
        foreach ($r in 'YandereSimLauncher/YandereSimulator', 'YandereSimulator', 'Games/YandereSimulator') {
            $candidatas.Add((Join-Path $unidad.RootDirectory.FullName $r))
        }
    }
    foreach ($c in $candidatas) { if (Test-Juego $c) { return (Resolve-Path -LiteralPath $c).Path } }
    return $null
}

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args[0]) falló (código $LASTEXITCODE)." }
}

# ---------- 1. Juego ----------
if ($Juego) {
    if (-not (Test-Juego $Juego)) { throw "La carpeta indicada no contiene YandereSimulator_Data\Managed\Assembly-CSharp.dll: $Juego" }
    $Juego = (Resolve-Path -LiteralPath $Juego).Path
} else {
    $Juego = Find-Juego
    if (-not $Juego) { throw 'No se encontró el juego. Indicá la carpeta con -Juego "C:\ruta\YandereSimulator" o definí YANDERE_SIM_DIR.' }
}
$ensamblado = Join-Path $Juego 'YandereSimulator_Data/Managed/Assembly-CSharp.dll'
$hash = (Get-FileHash -LiteralPath $ensamblado -Algorithm SHA256).Hash
$conocida = $compat.builds_conocidas | Where-Object { $_.sha256_Assembly_CSharp -eq $hash } | Select-Object -First 1
Write-Host "Juego:   $Juego"
Write-Host "SHA-256: $hash"
if ($conocida) { Write-Host "Build:   $($conocida.build) ($($conocida.fecha)) · verificada" -ForegroundColor Green }
else { Write-Warning 'Build no verificada: su huella no figura en compatibilidad.json. Revisá el informe del inspector antes de agregarla.' }

# ---------- 2. Compilación (sin advertencias) ----------
Invoke-Dotnet build (Join-Path $source 'YandereUltimate.sln') -c Release -warnaserror "-p:GameDir=$Juego" -nologo
$puente = Join-Path $temp 'build/Bridge/bin/Release/net472/YandereUltimate.Bridge.dll'
$inspector = Join-Path $temp 'build/Inspector/bin/Release/net8.0/YandereUltimate.Inspector.dll'

# ---------- 3. Pruebas automáticas ----------
if (-not $SinPruebas) {
    $env:YS_JUEGO = $Juego
    $env:YS_PUENTE = $puente
    Invoke-Dotnet test (Join-Path $source 'Pruebas/Pruebas.csproj') -c Release --no-build -nologo --blame-hang-timeout 120s
}

# ---------- 4. Inspección de referencias contra la build instalada ----------
$informe = Join-Path $temp 'inspeccion.json'
& dotnet $inspector $Juego $puente --json $informe
switch ($LASTEXITCODE) {
    0 { }
    2 { throw 'El complemento usa referencias que no existen en esta build (ver informe arriba). Corregí Juego.cs/Parches.cs y el catálogo.' }
    3 { throw 'Hay referencias del complemento sin declarar en source/Comun/Catalogo.cs.' }
    default { throw "El inspector falló (código $LASTEXITCODE)." }
}

# ---------- 5. Publicación ----------
$publicado = Join-Path $temp 'publish'
Invoke-Dotnet publish (Join-Path $source 'Desktop/Desktop.csproj') -c Release -o $publicado -nologo
Copy-Item (Join-Path $publicado 'YandereSimulatorUltimateTrainer.exe') $raiz -Force
Copy-Item $puente $raiz -Force

# ---------- 6. Paquete para Release ----------
if ($Paquete) {
    $nombre = "YandereSimulatorUltimateTrainer-$version"
    $dist = Join-Path $raiz 'dist'
    $carpeta = Join-Path $dist $nombre
    if (Test-Path -LiteralPath $carpeta) { Remove-Item -LiteralPath $carpeta -Recurse -Force }
    New-Item -ItemType Directory -Path $carpeta | Out-Null
    Copy-Item (Join-Path $publicado 'YandereSimulatorUltimateTrainer.exe'), $puente -Destination $carpeta
    Copy-Item (Join-Path $raiz 'Loader') -Destination (Join-Path $carpeta 'Loader') -Recurse -Force
    foreach ($a in 'compatibilidad.json', 'README.md', 'LEEME.md', 'CHANGELOG.md', 'LICENCIAS.txt') { Copy-Item (Join-Path $raiz $a) $carpeta }
    $sumas = Get-ChildItem -LiteralPath $carpeta -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
        $rel = [IO.Path]::GetRelativePath($carpeta, $_.FullName).Replace('\', '/')
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $rel
    }
    Set-Content -LiteralPath (Join-Path $carpeta 'SHA256SUMS.txt') -Value $sumas -Encoding utf8
    $zip = Join-Path $dist "$nombre.zip"
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    # -Force incluye archivos que empiezan con punto (p. ej. Loader/.doorstop_version).
    Get-ChildItem -LiteralPath $carpeta -Force | Compress-Archive -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "Paquete: $zip" -ForegroundColor Green
    Write-Host "SHA-256: $((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash)"
}

Write-Host "Compilación $version lista." -ForegroundColor Green
Write-Host 'Para actualizar una instalación: cerrá el juego y usá Ajustes › Actualizar complemento en el entrenador.'
