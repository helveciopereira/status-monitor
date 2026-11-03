# publicar-atualizacao.ps1 — v0.4.0 (Onda 4)
# Publica uma versão nova do Status Monitor (ex-Velocímetro de Internet) nas Releases do GitHub. Todo velocímetro
# instalado pelo Setup encontra a versão em até ~1 hora (v1.7.2; era 5 min), baixa em segundo plano (só a
# diferença) e se reinicia sozinho num momento tranquilo — sem aviso, sem clique
# (ver src\VelocimetroInternet\Sistema\ServicoAtualizacao.cs e App.xaml.cs).
#
# Uso (na raiz do projeto):
#   pwsh -File tools\publicar-atualizacao.ps1
#
# A versão publicada é a do <Version> do .csproj — aumente-a ANTES (regra do odômetro).
# Sequência: compila → testa (para se algo falhar) → publica autocontido → baixa a versão
# anterior (para gerar o pacote de DIFERENÇA, de poucos KB) → empacota → envia → confere.
#
# Pré-requisitos desta máquina:
#   - .NET SDK 10 por usuário em %LOCALAPPDATA%\Microsoft\dotnet;
#   - ferramenta vpk NA MESMA VERSÃO do pacote Velopack do .csproj
#     (dotnet tool install -g vpk --version <versão>). O vpk é ferramenta .NET 8 e a máquina só
#     tem o runtime 10 → DOTNET_ROLL_FORWARD=Major (sem isso: "To install missing framework");
#   - Git com login do GitHub salvo (Gerenciador de Credenciais). O token é lido de lá na hora
#     e NUNCA é gravado em arquivo nem impresso.

$ErrorActionPreference = 'Stop'

$RepoUrl = 'https://github.com/helveciopereira/status-monitor'   # v0.6.0: renomeado de velocimetro-internet
# packId define a pasta de instalação: %LOCALAPPDATA%\VelocimetroInternetApp.
# NÃO pode ser "VelocimetroInternet": essa é a pasta de DADOS (log, consumo do dia) e o
# desinstalador do Velopack apaga a pasta de instalação INTEIRA.
$PackId = 'VelocimetroInternetApp'
$Titulo = 'Status Monitor'   # v0.6.0: nome dos atalhos e de "Aplicativos instalados" (era Velocímetro de Internet)
$Root = Split-Path $PSScriptRoot -Parent
$Project = Join-Path $Root 'src\VelocimetroInternet\VelocimetroInternet.csproj'
$Solution = Join-Path $Root 'VelocimetroInternet.sln'
$PublishDir = Join-Path $Root 'publicar-tmp'
$ReleasesDir = Join-Path $Root 'Releases'
$Icone = Join-Path $Root 'src\VelocimetroInternet\Recursos\app.ico'

# ----- Ferramentas -----
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH;$env:USERPROFILE\.dotnet\tools"
$env:DOTNET_ROLL_FORWARD = 'Major'

# Interrompe o script se o último programa externo falhou (o PowerShell não faz isso sozinho).
function Assert-Ok([string]$etapa) {
    if ($LASTEXITCODE -ne 0) { throw "Falhou: $etapa (código $LASTEXITCODE)." }
}

# ----- Versão (do .csproj) e conferência do vpk -----
[xml]$csproj = Get-Content $Project -Encoding utf8
$version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
$velopack = ($csproj.Project.ItemGroup.PackageReference | Where-Object { $_.Include -eq 'Velopack' }).Version
if (-not $version) { throw 'Não achei <Version> no .csproj.' }
$vpkInstalado = (dotnet tool list -g | Select-String '^vpk\s+(\S+)').Matches.Groups[1].Value
if ($vpkInstalado -ne $velopack) {
    throw "vpk $vpkInstalado instalado, mas o .csproj usa Velopack $velopack. Rode: dotnet tool update -g vpk --version $velopack"
}
Write-Host "== Publicando $Titulo $version (Velopack $velopack) ==" -ForegroundColor Cyan

# ----- Token do GitHub (do Gerenciador de Credenciais do Git) -----
$cred = "protocol=https`nhost=github.com`n`n" | git credential fill 2>$null
$token = ($cred | Where-Object { $_ -like 'password=*' }) -replace '^password=', ''
if (-not $token) { throw 'Sem login do GitHub salvo no Git. Faça um git push qualquer para o GitHub e tente de novo.' }
$headers = @{ Authorization = "Bearer $token"; 'User-Agent' = 'publicar-velocimetro' }

# Nunca sobrescreve uma versão já publicada (os velocímetros instalados já podem tê-la baixado).
$apiRepo = $RepoUrl -replace 'https://github.com/', 'https://api.github.com/repos/'
try {
    Invoke-RestMethod "$apiRepo/releases/tags/v$version" -Headers $headers | Out-Null
    throw "A versão v$version já existe nas Releases. Aumente o <Version> do .csproj."
} catch [Microsoft.PowerShell.Commands.HttpResponseException] {
    if ($_.Exception.Response.StatusCode.value__ -ne 404) { throw }   # 404 = ainda não existe: segue
}

# ----- Portão de qualidade: compila e roda a suíte inteira -----
dotnet build $Solution -c Release --nologo -v q; Assert-Ok 'build'
dotnet test $Solution -c Release --no-build --nologo; Assert-Ok 'testes'

# ----- Publica autocontido em PASTA (leva o .NET junto; sem arquivo único: assim o pacote de
#       diferença entre versões fica em KB — um .exe único comprimido mudaria quase inteiro) -----
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
dotnet publish (Split-Path $Project) -c Release -r win-x64 --self-contained true -o $PublishDir --nologo -v q
Assert-Ok 'publish'

# ----- Baixa a última versão publicada: com ela o vpk gera o pacote de DIFERENÇA -----
New-Item -ItemType Directory -Force $ReleasesDir | Out-Null
vpk download github --repoUrl $RepoUrl --token $token -o $ReleasesDir
if ($LASTEXITCODE -ne 0) { Write-Host 'Sem versão anterior para baixar (primeira publicação?). Segue só com o pacote completo.' -ForegroundColor Yellow }

# ----- Empacota: instalador (Setup.exe), pacote completo e de diferença -----
# Sem --runtime o vpk avisa "defaulting to x86": é só a marcação do pacote; o app é x64
# autocontido e o ciclo foi provado ASSIM. Não trocar entre versões já publicadas sem ensaio.
vpk pack -u $PackId -v $version -p $PublishDir -e StatusMonitor.exe `
    --packTitle $Titulo --packAuthors 'Helvécio' -i $Icone -o $ReleasesDir
Assert-Ok 'vpk pack'

# ----- v0.7.0: nomes de arquivo "StatusMonitor" no instalador e no portátil -----
# O vpk dá a eles o nome do packId (VelocimetroInternetApp-win-Setup.exe), e o packId NÃO pode mudar
# (seria outro app: as cópias instaladas parariam de atualizar). Eles não são citados no feed
# (releases.win.json só lista os .nupkg), então renomeamos aqui e corrigimos o assets.win.json,
# que é a lista que o "vpk upload" envia.
$nomesNovos = @{ 'Installer' = 'StatusMonitor-Setup.exe'; 'Portable' = 'StatusMonitor-Portable.zip' }
$assetsJson = Join-Path $ReleasesDir 'assets.win.json'
$assets = Get-Content $assetsJson -Raw | ConvertFrom-Json
foreach ($a in $assets) {
    if ($nomesNovos.ContainsKey($a.Type)) {
        Move-Item (Join-Path $ReleasesDir $a.RelativeFileName) (Join-Path $ReleasesDir $nomesNovos[$a.Type]) -Force
        $a.RelativeFileName = $nomesNovos[$a.Type]
    }
}
# Sem BOM e numa linha, como o vpk grava (o leitor dele é JSON estrito)
[IO.File]::WriteAllText($assetsJson, (ConvertTo-Json @($assets) -Compress), (New-Object Text.UTF8Encoding($false)))

# ----- Envia e publica a Release (sem rascunho: os velocímetros já enxergam) -----
vpk upload github --repoUrl $RepoUrl --token $token -o $ReleasesDir `
    --publish --releaseName "$Titulo $version" --tag "v$version"
Assert-Ok 'vpk upload'

# ----- Confere que chegou (publicar não é entregar) -----
$latest = Invoke-RestMethod "$apiRepo/releases/latest" -Headers $headers
if ($latest.tag_name -ne "v$version") { throw "A última Release é $($latest.tag_name), não v$version." }
$feed = Invoke-WebRequest "$RepoUrl/releases/latest/download/releases.win.json" -UseBasicParsing
# O GitHub serve o .json como application/octet-stream → Content vem em BYTES: decodificar
$feedTexto = if ($feed.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($feed.Content) } else { [string]$feed.Content }
if ($feedTexto -notmatch [regex]::Escape("`"$version`"")) { throw 'O feed direto (latest/download/releases.win.json) ainda não mostra a versão nova.' }
Write-Host "OK: v$version publicada em $($latest.html_url)" -ForegroundColor Green
Write-Host "Instalador para quem ainda não tem: $RepoUrl/releases/latest/download/StatusMonitor-Setup.exe"
