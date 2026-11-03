# gerar-manifesto.ps1 — v1.9.0 (acesso amplo)
# Gera os TRÊS manifestos do winget (version, installer, defaultLocale) para a versão publicada do
# Status Monitor, em tools\winget\manifests\h\HelvecioPereira\StatusMonitor\<versão>\ — a mesma árvore
# que o repositório microsoft/winget-pkgs usa. Depois valida com `winget validate`.
#
# Uso (na raiz do projeto, DEPOIS de tools\publicar-atualizacao.ps1 ter publicado a Release):
#   pwsh -File tools\winget\gerar-manifesto.ps1              # versão = <Version> do .csproj
#   pwsh -File tools\winget\gerar-manifesto.ps1 -Versao 1.9.0
#
# O que ele faz: baixa o StatusMonitor-Setup.exe da Release v<versão> (URL fixa por tag, nunca
# "latest" — o manifesto precisa apontar para um arquivo que não muda), calcula o SHA-256, grava os
# YAMLs e roda `winget validate --manifest <pasta>`.
#
# O envio ao microsoft/winget-pkgs (PR) é AÇÃO EXTERNA e não é feito aqui: abra o PR só com o aval do
# dono. Fluxo manual: fork de microsoft/winget-pkgs → copiar a pasta manifests\h\HelvecioPereira\…
# → PR "New package: HelvecioPereira.StatusMonitor version X". A primeira aceitação costuma levar dias;
# a partir daí, cada versão nova é um PR igual (só muda PackageVersion, InstallerUrl e InstallerSha256).
#
# Por que estes valores (conferidos em 2026-11-03):
#   - Instalador: Setup do Velopack (vpk pack em tools\publicar-atualizacao.ps1), tipo "exe", instala
#     por USUÁRIO (sem administrador) em %LOCALAPPDATA%\VelocimetroInternetApp; aceita --silent.
#   - AppsAndFeaturesEntries: o Velopack registra em "Aplicativos instalados" o DisplayName = --packTitle
#     ("Status Monitor"), Publisher = --packAuthors ("Helvécio") e a chave de desinstalação = packId
#     (VelocimetroInternetApp). O winget usa isso para reconhecer que o app já está instalado e para
#     "winget upgrade" — sem isso, ele acharia que nunca instalou.
#   - Só x64: o publish é win-x64 autocontido.

[CmdletBinding()]
param(
    [string]$Versao,
    # Pule a validação quando a máquina não tem winget (ex.: CI).
    [switch]$SemValidar
)

$ErrorActionPreference = 'Stop'

$Root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$Project = Join-Path $Root 'src\VelocimetroInternet\VelocimetroInternet.csproj'
$RepoUrl = 'https://github.com/helveciopereira/status-monitor'
$PackageId = 'HelvecioPereira.StatusMonitor'

if (-not $Versao) {
    # Mesma fonte de verdade do publicador: o <Version> do .csproj.
    $Versao = ([xml](Get-Content $Project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if ($Versao -notmatch '^\d+\.\d+\.\d+$') { throw "Versão inválida: '$Versao' (esperado MAIOR.MENOR.PATCH)" }

$InstallerUrl = "$RepoUrl/releases/download/v$Versao/StatusMonitor-Setup.exe"
$Pasta = Join-Path $PSScriptRoot "manifests\h\HelvecioPereira\StatusMonitor\$Versao"
New-Item -ItemType Directory -Force $Pasta | Out-Null

# ----- SHA-256 do instalador PUBLICADO (não do local: o manifesto descreve o que o usuário baixa) -----
$tmp = Join-Path ([IO.Path]::GetTempPath()) "StatusMonitor-Setup-$Versao.exe"
Write-Host "Baixando $InstallerUrl ..."
Invoke-WebRequest -Uri $InstallerUrl -OutFile $tmp -UseBasicParsing
$sha = (Get-FileHash $tmp -Algorithm SHA256).Hash.ToUpperInvariant()
$tamanhoMB = [math]::Round((Get-Item $tmp).Length / 1MB, 1)
Remove-Item $tmp -Force
Write-Host "SHA-256: $sha ($tamanhoMB MB)"

# ----- Os três YAMLs (esquema 1.6.0 do winget) -----
# Nota: o winget exige UTF-8 com BOM nos manifestos? Não — aceita sem BOM; gravamos sem BOM, LF.
$utf8 = [Text.UTF8Encoding]::new($false)

$version = @"
# Created by tools/winget/gerar-manifesto.ps1 (Status Monitor)
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.6.0.schema.json
PackageIdentifier: $PackageId
PackageVersion: $Versao
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
"@

$installer = @"
# Created by tools/winget/gerar-manifesto.ps1 (Status Monitor)
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.6.0.schema.json
PackageIdentifier: $PackageId
PackageVersion: $Versao
InstallerType: exe
Scope: user
InstallModes:
- silent
InstallerSwitches:
  Silent: --silent
  SilentWithProgress: --silent
UpgradeBehavior: install
ReleaseDate: $(Get-Date -Format 'yyyy-MM-dd')
AppsAndFeaturesEntries:
- DisplayName: Status Monitor
  Publisher: Helvécio
  ProductCode: VelocimetroInternetApp
Installers:
- Architecture: x64
  InstallerUrl: $InstallerUrl
  InstallerSha256: $sha
ManifestType: installer
ManifestVersion: 1.6.0
"@

$locale = @"
# Created by tools/winget/gerar-manifesto.ps1 (Status Monitor)
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.6.0.schema.json
PackageIdentifier: $PackageId
PackageVersion: $Versao
PackageLocale: en-US
Publisher: Helvécio Pereira
PublisherUrl: https://github.com/helveciopereira
PublisherSupportUrl: $RepoUrl/issues
PackageName: Status Monitor
PackageUrl: $RepoUrl
License: Proprietary
ShortDescription: CPU, RAM and internet speed on the Windows 11 taskbar, with a quality report for your ISP.
Description: |-
  Status Monitor is a tiny widget that sits on the Windows 11 taskbar, next to the system tray, and
  shows upload and download speed in real time, plus CPU and RAM usage. It also keeps a daily and
  monthly data-usage history, runs on-demand speed tests, detects internet outages and builds a monthly
  quality report (PDF/CSV) you can send to your internet provider. Installs per user, no administrator
  needed, and updates itself silently. Interface in English and Brazilian Portuguese.
Moniker: statusmonitor
Tags:
- bandwidth
- cpu
- internet
- monitor
- network
- ram
- speed
- taskbar
- widget
ReleaseNotesUrl: $RepoUrl/releases/tag/v$Versao
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"@

# Nomes de arquivo exigidos pelo winget-pkgs: <Id>.yaml, <Id>.installer.yaml, <Id>.locale.<locale>.yaml
[IO.File]::WriteAllText((Join-Path $Pasta "$PackageId.yaml"), ($version -replace "`r`n", "`n") + "`n", $utf8)
[IO.File]::WriteAllText((Join-Path $Pasta "$PackageId.installer.yaml"), ($installer -replace "`r`n", "`n") + "`n", $utf8)
[IO.File]::WriteAllText((Join-Path $Pasta "$PackageId.locale.en-US.yaml"), ($locale -replace "`r`n", "`n") + "`n", $utf8)
Write-Host "Manifestos gravados em $Pasta"

# ----- Validação local -----
if (-not $SemValidar) {
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        Write-Host "winget validate ..."
        $saida = winget validate --manifest $Pasta 2>&1
        $saida | ForEach-Object { Write-Host "  $_" }
        if ($LASTEXITCODE -ne 0) { throw "winget validate falhou (código $LASTEXITCODE)" }
    } else {
        Write-Warning 'winget não encontrado — validação pulada (use -SemValidar para silenciar).'
    }
}

Write-Host ''
Write-Host "Próximo passo (só com aval do dono): PR em https://github.com/microsoft/winget-pkgs com a pasta"
Write-Host "  manifests\h\HelvecioPereira\StatusMonitor\$Versao\ (3 arquivos). Depois de aceito:"
Write-Host "  winget install $PackageId"
