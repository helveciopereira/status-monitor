# Gera o executável AUTOCONTIDO (não depende de .NET instalado) em dist\StatusMonitor.exe.
# Fecha antes qualquer instância aberta — o .exe em uso fica travado e o publish falha com
# "Access to the path ... is denied".
#   pwsh -File tools\publicar.ps1 [-Abrir]
param([switch]$Abrir)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot -Parent

# SDK do .NET 10 instalado por usuário (sem administrador) — ver walkthrough v0.1.0
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"

Get-Process VelocimetroInternet -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

dotnet publish "$raiz\src\VelocimetroInternet\VelocimetroInternet.csproj" -c Release -r win-x64 `
    --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -o "$raiz\dist" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou (código $LASTEXITCODE)" }

$exe = Get-Item "$raiz\dist\StatusMonitor.exe"
"Publicado: $($exe.FullName) ({0:N1} MB) versão {1}" -f ($exe.Length / 1MB), $exe.VersionInfo.FileVersion

if ($Abrir) {
    # Abre SEM DOTNET_ROOT no ambiente, como num duplo clique do usuário
    Remove-Item Env:DOTNET_ROOT
    Start-Process $exe.FullName
}
