param([string]$Juego='C:\Users\jansa\Downloads\YandereSimLauncher\YandereSimulator')
$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot 'source'
$build=Join-Path $env:TEMP 'ys-ultimate\publish-source'
dotnet build "$source\YandereUltimate.sln" -c Release "-p:GameDir=$Juego"
if($LASTEXITCODE -ne 0){throw 'La compilación falló.'}
dotnet publish "$source\Desktop\Desktop.csproj" -c Release -o $build
if($LASTEXITCODE -ne 0){throw 'La publicación falló.'}
Copy-Item "$build\YandereSimulatorUltimateTrainer.exe" $PSScriptRoot -Force
Copy-Item "$env:TEMP\ys-ultimate\build\Bridge\bin\Release\net472\YandereUltimate.Bridge.dll" $PSScriptRoot -Force
Write-Host 'Compilación lista. Para actualizar la instalación, cerrá el juego y desinstalá/reinstalá el complemento desde el entrenador.'
