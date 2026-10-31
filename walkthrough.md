# Walkthrough — Velocímetro de Internet (Windows 11 · C# WPF .NET 10)

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
