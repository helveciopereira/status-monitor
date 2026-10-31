# Walkthrough — Velocímetro de Internet (Windows 11 · C# WPF .NET 10)

## v0.2.0 — 2026-10-31 — Tela cheia instantânea, Onda 1 completa, painel rápido e estúdio de cores

### O que mudou
1. **Tela cheia some na hora.** Antes o widget só sumia no vigia de 1,5 s (o dono notou a demora).
   Agora o widget se registra como *appbar* só para ouvir o shell (`ABM_NEW`, sem reservar espaço)
   e recebe `ABN_FULLSCREENAPP` no instante em que um app entra/sai de tela cheia; o aviso é
   reconfirmado por uma regra geométrica (`DetectorTelaCheia`: a janela da frente cobre o monitor
   inteiro e não é o desktop/shell). Medido com `tools\medir-tela-cheia.ps1`: **some em 2–4 ms,
   volta em ~48 ms** (antes: até 1,5 s).
2. **Configurações persistidas** em `%AppData%\VelocimetroInternet\settings.json` (leitura
   tolerante: arquivo corrompido vira `.bak` e o app abre com padrões; gravação atômica).
3. **Motor de paletas do Modelo 3** (`Paletas`): 5 predefinidas + personalizada; o contraste WCAG
   é calculado sobre o fundo real da pílula e a cor é recalibrada até passar (4,5:1; 7:1 no Alto
   Contraste). O widget segue o tema da **barra**; menu, painel e estúdio seguem o tema dos **apps**,
   e trocam ao vivo (`WM_SETTINGCHANGE "ImmersiveColorSet"`).
4. **Ícone na bandeja** (Shell_NotifyIcon próprio, sem WinForms) com dica mostrando as velocidades
   e **menu** estilizado: painel rápido · estúdio · Unidade (MB/s / Mbps) · Fixar na barra ·
   Iniciar com o Windows · Mostrar ícone · Sair. O ícone volta sozinho quando o Explorer reinicia.
5. **Painel rápido** (Quick Flyout do design) ao clicar no widget/ícone: Envio e Download com pico
   de 60 s e barra de uso (contra o plano informado ou o link da placa), gráfico dos últimos 60 s,
   "Ao vivo · Wi-Fi", **consumo do dia** (em bytes reais, persistido em `consumo.json`, zera na
   virada do dia), seletor Mbps/MB/s e interruptores Iniciar com o Windows / Fixar na barra.
   Fecha ao perder o foco, com Esc e quando algo entra em tela cheia.
6. **Estúdio de cores e configurações**: prévia nas barras escura e clara com selo de contraste,
   galeria das paletas, cores personalizadas em hex, suavização, velocidade do plano, interfaces
   ignoradas (mostra o que está sendo medido), ícone da bandeja, restaurar padrão. Edita uma cópia;
   só vale ao clicar Aplicar.
7. **Iniciar com o Windows** real (`HKCU\...\Run` + `StartupApproved`), **ligado no 1º uso** (o pedido
   é "iniciar com o sistema"; se o dono desligar, não é religado) e corrigido se o .exe mudar de pasta.
8. **Instância única útil:** abrir o .exe de novo abre o painel na cópia que já roda.
9. Ícone do aplicativo (`Recursos\app.ico`, gerado por `tools\gerar-icone.ps1`), log renomeado
   para `velocimetro.log` com rodízio em 1 MB, memória aparada ao fechar painel/estúdio.

### Por quê (causas raiz)
- **Demora na tela cheia:** a única fonte era um temporizador de 1,5 s + `SHQueryUserNotificationState`.
  O shell já avisa appbars na hora; faltava se registrar.
- **2ª execução não fazia nada:** `HWND_BROADCAST` não alcança janelas WPF com `ShowInTaskbar=False`,
  que ganham um dono oculto — o broadcast só chega a janelas de topo sem dono. Corrigido com
  `FindWindow` pelo título do widget + `PostMessage` direto + `AllowSetForegroundWindow`.
- **RAM 185 MB com o estúdio aberto e nada devolvido ao fechar:** coleta + `EmptyWorkingSet` ao
  fechar painel/estúdio → volta para ~54 MB.

### Como usar
- Gerar: `pwsh -File tools\publicar.ps1 -Abrir` → `dist\VelocimetroInternet.exe`.
- Clique no widget = painel; clique direito = menu; no painel, "Cores e mais configurações".
- Argumentos de diagnóstico: `--painel` (abre o painel ao iniciar), `--compacto`, `--poc-fixo`, `--modo-a`.

### Arquivos
Novos: `Apresentacao\{Paletas,ServicoTema,PainelRapido.xaml(.cs),EstudioCores.xaml(.cs)}.cs`,
`Config\{Configuracoes,ServicoConfiguracoes}.cs`, `Medicao\HistoricoRede.cs`,
`Sistema\{DetectorTelaCheia,IconeBandeja,InicioComWindows,TemaWindows}.cs`, `Recursos\app.ico`,
`tools\{publicar,medir-tela-cheia,gerar-icone}.ps1`, `tests\...\Onda1a3Tests.cs`.
Alterados: `App.xaml(.cs)`, `TaskbarHost.cs`, `TaskbarWidget.xaml(.cs)`, `NetworkSampler.cs`
(velocidade de link + interface principal), `SpeedFormatter.cs` (consumo, link), `NativeMethods.cs`,
`.csproj` (0.2.0, ícone), `.gitignore` (`dist/`), README, 03-PLANO.

### Validação executada
- `dotnet build -c Release`: 0 avisos, 0 erros. `dotnet test`: **44/44** (8 antigos + 36 novos:
  WCAG/paletas nas duas barras, tela cheia, settings corrompido, início com o Windows numa
  subchave de TESTE, histórico/virada do dia, formatações).
- Na sessão real (UI Automation + captura de tela): widget com valores ao vivo; 2ª execução abre o
  painel; trocar para Mbps pelo painel grava `settings.json`; estúdio abre pelo painel; escolher
  Neon + Aplicar recolore o widget na barra; chave Run gravada no 1º uso; tela cheia 2–4 ms
  (3 rodadas); RAM em repouso ~54 MB.
- As preferências alteradas pelo teste (Mbps, Neon) foram devolvidas a MB/s + Clássico.

### Validação manual recomendada (não automatizada)
1. **Reiniciar o PC** e conferir que o widget aparece sozinho.
2. Configurações → Personalização → Cores → alternar Claro/Escuro: widget, menu e painel acompanham.
3. Baixar um arquivo grande e comparar com o Gerenciador de Tarefas (±3%).
4. Vídeo do YouTube em tela cheia (F) e um jogo: o widget some na hora e volta ao sair.
5. Reiniciar o Explorer: widget e ícone da bandeja voltam (o ícone fica no "^" até ser arrastado para a barra).

## v0.1.1 — 2026-10-31 — Correção: o widget não aparecia na barra

### Sintoma relatado pelo dono
Ao abrir o `.exe`, uma janela "piscou no topo da tela e apagou" e nada ficou na barra. Depois de
reiniciar o Explorer, também não apareceu nada.

### Causa raiz (verificada na sessão interativa real, não no desktop isolado da v0.1.0)
1. **O modo A (`SetParent` na `Shell_TrayWnd`) fica invisível no Windows 11.** A janela WPF
   transparente (layered) vira filha da barra XAML e não é desenhada (log de 09:57:30: "Encaixe
   aplicado [ModoA]", mas nada na tela).
2. **A janela filha morre com o Explorer.** Ao reiniciar o Explorer, a barra antiga é destruída e
   leva a nossa janela junto. O processo continuou vivo, sem nada visível, e o `SetParent` seguinte
   devolveu 0 sobre um HWND morto (log de 09:58:59). Enumeração das janelas do processo: só
   restaram janelas internas ocultas do WPF.
3. **O "piscar"** vinha de `_widget.Show()` ser chamado antes do posicionamento: a janela nascia na
   posição padrão e só depois ia para a barra.
4. **O .exe dependia do .NET 10**, que só existe em `%LocalAppData%\Microsoft\dotnet`. Aberto sem
   `DOTNET_ROOT` no ambiente, mostrava "You must install or update .NET to run this application".
5. A validação da v0.1.0 rodou num desktop Win32 isolado, sem `Shell_TrayWnd`, onde o app sempre
   caía no modo B. Por isso o defeito do modo A não apareceu.

### O que mudou
- `TaskbarHost.cs`: **modo B é o padrão**. O topo é reafirmado pelo gancho `EVENT_SYSTEM_FOREGROUND`
  e pelo vigia de 1,5 s, já que a barra também é topmost e passaria por cima ao ser clicada. A janela
  **some em tela cheia** (`SHQueryUserNotificationState` 2/3/4) e ganha posição (Left/Top) antes de
  ficar visível.
- `App.xaml.cs`: removido o `Show()` antecipado. `--modo-a` passa a ser opt-in experimental
  (`--modo-b` continua aceito).
- `NativeMethods.cs`: `SetWinEventHook`, `UnhookWinEvent`, `SHQueryUserNotificationState`,
  `SWP_NOMOVE` e `SWP_NOSIZE`.
- **Distribuição autocontida:** `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true
  -p:EnableCompressionInSingleFile=true -o dist` gera um .exe único de ~62 MB que não depende de .NET
  instalado. `dist/` entra no `.gitignore`.
- Versão 0.1.0 → 0.1.1 (`VelocimetroInternet.csproj`, log de inicialização).

### Como usar
```powershell
C:\Projetos\Windows\Velocímetro internet\dist\VelocimetroInternet.exe      # abrir (pode ser por duplo clique)
Stop-Process -Name VelocimetroInternet                                     # fechar (o menu chega na Onda 1)
```

### Validação executada
- `dotnet build -c Release`: 0 avisos, 0 erros. `dotnet test`: **8/8 aprovados**.
- **Sessão real:** o `dist\VelocimetroInternet.exe` foi aberto sem `DOTNET_ROOT`. Janela visível em
  (1725,1532)-(1881,1595), na vaga do Clima à esquerda do "^", à frente da barra na ordem Z
  (8 contra 13). Captura de tela conferida, com valores reais em KB/s.
- **Explorer reiniciado de verdade** (`Stop-Process explorer`): processo vivo, `TaskbarCreated`
  recebido, widget reencaixado e visível (ordem Z 8 contra 15). Captura conferida.
- Não testado automaticamente: tela cheia (jogo/vídeo) → validação manual recomendada.

## v0.1.0 — 2026-10-31 — Onda 0: Ambiente, Design Stitch (Modelo 3 Acrylic Pill) e Prova de Conceito

### O que mudou
1. **Modelos de Design no Stitch e Escolha do Modelo 3 (`Minimalismo Acrylic Pill`):**
   - Criado o projeto Stitch `projects/3690916573458587930` com 3 propostas visuais completas salvas em [`design/`](design/README.md) (`modelo-1-fluent-mica`, `modelo-2-telemetria-hud`, `modelo-3-acrylic-pill`).
   - Adotado o **Modelo 3 (Minimalismo Acrylic Pill)** com unidade padrão em **`MB/s` (bytes)** (e suporte pronto para alternar para `Mbps` em configurações).
2. **Ambiente .NET 10 SDK e Estrutura da Solução (`VelocimetroInternet.sln`):**
   - Instalado o **.NET 10 SDK (`10.0.403`)** por usuário em `%LocalAppData%\Microsoft\dotnet` (sem exigir privilégios de administrador) e configurado `DOTNET_ROOT` + fonte `nuget.org`.
   - Criada a solução com o projeto WPF [`src/VelocimetroInternet/VelocimetroInternet.csproj`](src/VelocimetroInternet/VelocimetroInternet.csproj) (`net10.0-windows`, `PerMonitorV2` no [`app.manifest`](src/VelocimetroInternet/app.manifest)) e o projeto de testes [`tests/VelocimetroInternet.Tests/VelocimetroInternet.Tests.csproj`](tests/VelocimetroInternet.Tests/VelocimetroInternet.Tests.csproj).
3. **PoC 1 e PoC 1b — Widget Acrylic Pill + Encaixe na Vaga do Clima + UI Automation:**
   - Implementado [`TaskbarWidget.xaml`](src/VelocimetroInternet/Apresentacao/TaskbarWidget.xaml) com duas cápsulas translúcidas (`#2E4CC2FF` para upload azul `#4CC2FF` e `#2EFF6B6B` para download coral `#FF6B6B`), tipografia `Segoe UI Variable Text` com `Typography.NumeralAlignment="Tabular"`, tema claro (`#005FB8` / `#C42B1C`) e modo compacto anti-colisão de 1 linha (`↑ 1,24  |  ↓ 38,7 MB/s`).
   - Implementados [`TaskbarLayout.cs`](src/VelocimetroInternet/Apresentacao/TaskbarLayout.cs) (cálculo geométrico puro e testável da vaga do Clima e detecção de colisão) e [`TaskbarHost.cs`](src/VelocimetroInternet/Apresentacao/TaskbarHost.cs) (Modo A `SetParent` + fallback automático para Modo B `Topmost`, leitura da bandeja e dos botões de apps via UI Automation em thread MTA de fundo e escuta do evento `TaskbarCreated` quando o Explorer reinicia).
4. **PoC 2 — Leitura Real de Rede via `GetIfTable2` + Filtro Anti-Dupla Contagem:**
   - Implementados [`NativeMethods.cs`](src/VelocimetroInternet/Sistema/NativeMethods.cs), [`InterfaceFilter.cs`](src/VelocimetroInternet/Medicao/InterfaceFilter.cs), [`NetworkSampler.cs`](src/VelocimetroInternet/Medicao/NetworkSampler.cs), [`SpeedCalculator.cs`](src/VelocimetroInternet/Medicao/SpeedCalculator.cs) e [`SpeedFormatter.cs`](src/VelocimetroInternet/Apresentacao/SpeedFormatter.cs).
   - Validada a leitura real da placa física `MediaTek Wi-Fi 7 MT7925 Wireless LAN Card` com bloqueio automático de `Tailscale`, `Radmin VPN` e filtros NDIS intermediários, liberando sempre a memória nativa com `FreeMibTable` em bloco `try/finally`.

### Por quê (Causa Raiz)
- O Windows 11 removeu o suporte a `DeskBand` na barra de tarefas e o retângulo Win32 legado `MSTaskSwWClass` devolve largura desatualizada (~288 px contra 1169 px reais). Por isso, era indispensável provar na Onda 0 o acoplamento visual a 150% DPI, a medição por UI Automation, a recepção de `TaskbarCreated` e o orçamento de memória (< 60 MB) antes de construir o restante do aplicativo.

### Como usar / testar a PoC
```powershell
# Execução normal (tempo real em MB/s, acoplado na vaga do Clima):
.\src\VelocimetroInternet\bin\Release\net10.0-windows\VelocimetroInternet.exe

# Flags de diagnóstico da Onda 0:
.\src\VelocimetroInternet\bin\Release\net10.0-windows\VelocimetroInternet.exe --poc-fixo
.\src\VelocimetroInternet\bin\Release\net10.0-windows\VelocimetroInternet.exe --poc-fixo --tema-claro
.\src\VelocimetroInternet\bin\Release\net10.0-windows\VelocimetroInternet.exe --poc-fixo --compacto
.\src\VelocimetroInternet\bin\Release\net10.0-windows\VelocimetroInternet.exe --modo-b --bits
```
O log detalhado de amostragem e memória é gravado em `%LocalAppData%\VelocimetroInternet\poc-onda0.log`.

### Arquivos tocados
- `.gitignore`
- `VelocimetroInternet.sln`
- `src/VelocimetroInternet/VelocimetroInternet.csproj`
- `src/VelocimetroInternet/app.manifest`
- `src/VelocimetroInternet/App.xaml` · `src/VelocimetroInternet/App.xaml.cs`
- `src/VelocimetroInternet/Sistema/NativeMethods.cs`
- `src/VelocimetroInternet/Medicao/InterfaceFilter.cs`
- `src/VelocimetroInternet/Medicao/NetworkSampler.cs`
- `src/VelocimetroInternet/Medicao/SpeedCalculator.cs`
- `src/VelocimetroInternet/Apresentacao/SpeedFormatter.cs`
- `src/VelocimetroInternet/Apresentacao/TaskbarLayout.cs`
- `src/VelocimetroInternet/Apresentacao/TaskbarWidget.xaml` · `src/VelocimetroInternet/Apresentacao/TaskbarWidget.xaml.cs`
- `src/VelocimetroInternet/Apresentacao/TaskbarHost.cs`
- `tests/VelocimetroInternet.Tests/VelocimetroInternet.Tests.csproj`
- `tests/VelocimetroInternet.Tests/VelocimetroTests.cs`
- `design/README.md` + `design/modelo-1-fluent-mica/` + `design/modelo-2-telemetria-hud/` + `design/modelo-3-acrylic-pill/`
- `README.md` · `01-ANALISE.md` · `02-ARQUITETURA.md` · `03-PLANO-DE-ONDAS.md` · `04-IDEIAS.md`

### Validação executada
- **Build Release (`rtk dotnet build VelocimetroInternet.sln -c Release`):** 0 erros, 0 avisos.
- **Testes Unitários e de Integração (`rtk dotnet test VelocimetroInternet.sln -c Release`):** 8/8 aprovados em 158 ms (`SpeedFormatter`, histerese, `SpeedCalculator` EMA e descarte de delta negativo, `InterfaceFilter` contra `Tailscale`/`Radmin VPN`, `TaskbarLayout` em 2560×1600 a 150% DPI e `NetworkSampler` com `GetIfTable2`).
- **Inspeção Visual (`PrintWindow` a 150% DPI):** 4 estados fotografados e conferidos (`poc-estado1-escuro.png`, `poc-estado2-claro.png`, `poc-estado3-compacto.png`, `poc-estado4-tempo-real.png`).
- **Orçamento de Desempenho:** WorkingSet medido entre **15,1 MB e 38,3 MB** após `EmptyWorkingSet` (meta: `< 60 MB`); CPU média `< 0,9%` durante o boot e `< 0,2%` em regime.
- **Recuperação do Explorer (`TaskbarCreated`):** testado envio da mensagem `TaskbarCreated` ao HWND do widget, confirmando detecção e reencaixe automático no log.

