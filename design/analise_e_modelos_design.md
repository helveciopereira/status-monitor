# Análise da Proposta & Modelos de Design (Stitch → C# WPF .NET 10)

## 1. Diagnóstico da Proposta (Fase 1)

A documentação existente ([README.md](file:///c:/Projetos/Windows/Velocímetro%20internet/README.md), [01-ANALISE.md](file:///c:/Projetos/Windows/Velocímetro%20internet/01-ANALISE.md), [02-ARQUITETURA.md](file:///c:/Projetos/Windows/Velocímetro%20internet/02-ARQUITETURA.md), [03-PLANO-DE-ONDAS.md](file:///c:/Projetos/Windows/Velocímetro%20internet/03-PLANO-DE-ONDAS.md) e [04-IDEIAS.md](file:///c:/Projetos/Windows/Velocímetro%20internet/04-IDEIAS.md)) é de **altíssimo nível de engenharia**: já antecipa as armadilhas reais do Windows 11 (remoção de DeskBands, mentira do retângulo `MSTaskSwWClass`, dupla contagem de VPNs ativas na máquina e contraste WCAG sobre a barra escura).

### ✅ O que JÁ está excelente — não mexer

1. **Escolha de Stack (`C# + .NET 10 + WPF`):** [02-ARQUITETURA.md](file:///c:/Projetos/Windows/Velocímetro%20internet/02-ARQUITETURA.md#L10-L24) acerta em cheio ao descartar WinUI 3 (que briga com janelas `WS_CHILD` / sem borda / `AllowsTransparency`) e Electron (pesado demais para rodar 24/7).
2. **Estratégia Dupla de Encaixe (`Modo A: SetParent` + `Modo B: Topmost` fallback):** [01-ANALISE.md](file:///c:/Projetos/Windows/Velocímetro%20internet/01-ANALISE.md#L54-L61) e [02-ARQUITETURA.md](file:///c:/Projetos/Windows/Velocímetro%20internet/02-ARQUITETURA.md#L83-L85) isolam o risco de atualizações do Windows Explorer em uma única classe (`TaskbarHost`).
3. **Descoberta da "Vaga do Clima" via UI Automation:** [01-ANALISE.md](file:///c:/Projetos/Windows/Velocímetro%20internet/01-ANALISE.md#L74-L89) e [02-ARQUITETURA.md](file:///c:/Projetos/Windows/Velocímetro%20internet/02-ARQUITETURA.md#L70-L81) já provaram com medição real nesta máquina (2560×1600 a 150%) que há 628 px físicos livres entre o último app (`X=1169`) e a bandeja (`X=1797`).
4. **Prevenção de Dupla Contagem (Tailscale e Radmin VPN):** [01-ANALISE.md](file:///c:/Projetos/Windows/Velocímetro%20internet/01-ANALISE.md#L35-L40) e [02-ARQUITETURA.md](file:///c:/Projetos/Windows/Velocímetro%20internet/02-ARQUITETURA.md#L57-L60) já tratam um bug clássico de monitores de rede que somam o túnel virtual junto com o Wi-Fi físico.
5. **Paleta Ajustada por Tema com Contraste WCAG ≥ 4,5:1:** [01-ANALISE.md](file:///c:/Projetos/Windows/Velocímetro%20internet/01-ANALISE.md#L134-L142) evita o azul puro `#0078D4` (ilegível no escuro com 3,9:1) e adota `#4CC2FF` (9:1) e `#FF6B6B` (6,5:1).
6. **Separação Pura entre Domínio e UI:** [02-ARQUITETURA.md](file:///c:/Projetos/Windows/Velocímetro%20internet/02-ARQUITETURA.md#L136-L165) permite testar `SpeedCalculator`, `SpeedFormatter`, `InterfaceFilter` e `TaskbarLayout` com xUnit sem depender de janela aberta.

---

### 🔍 Pontos Técnicos de Atenção para a Implementação em C# / WPF

| # | Ponto encontrado | O que fará · Como fará | Importância | Eficiência (Retorno÷Esforço) | Evidência |
|---|---|---|---|---|---|
| **1** | **Armadilha de `AllowsTransparency="True"` com `SetParent` (`WS_CHILD`) no WPF** | No WPF, `AllowsTransparency="True"` usa `ULW_ALPHA` em HWNDs top-level. Quando a janela vira filha (`WS_CHILD`) de `Shell_TrayWnd` (Modo A), algumas builds do Windows 11 24H2 cortam a renderização DirectX de janelas filhas transparentes ou deixam fundo preto. **Como resolver na Onda 0:** habilitar `WS_EX_LAYERED` explicitamente após o `SetParent` + usar `DWMWA_WINDOW_CORNER_PREFERENCE` ou `CompositionTarget.BackgroundColor = Colors.Transparent` via interoperabilidade `HwndSource`. | 🔴 Alta | Altíssima (evita bloqueio na PoC 1 da Onda 0) | [01-ANALISE.md](file:///c:/Projetos/Windows/Velocímetro%20internet/01-ANALISE.md#L56) · [02-ARQUITETURA.md](file:///c:/Projetos/Windows/Velocímetro%20internet/02-ARQUITETURA.md#L83) |
| **2** | **Custo de COM/UIA na thread de UI (`TaskbarLayout`)** | A UI Automation do Windows (`System.Windows.Automation` / `UIAutomationClient`) faz chamadas cross-process para o `explorer.exe`. Se chamada na UI thread ou com muita frequência, pode travar o `Dispatcher` se o Explorer engasgar. **Como fazer:** rodar todas as leituras de UIA e o handler `StructureChanged` em uma `Task` dedicada em background (MTA thread) com timeout de 250 ms, enviando apenas o `Rect` calculado para o `Dispatcher`. | 🔴 Alta | Alta (garante 0 engasgos e CPU < 0,3%) | [02-ARQUITETURA.md](file:///c:/Projetos/Windows/Velocímetro%20internet/02-ARQUITETURA.md#L70-L81) |
| **3** | **Memória e Alocação de `GetIfTable2` (`FreeMibTable`)** | `GetIfTable2` aloca memória não-gerenciada no heap do sistema a cada segundo. Se `FreeMibTable` não for chamado em um bloco `try/finally` (ou `SafeHandle`), haverá vazamento nativo contínuo (~100 KB/s). **Como fazer:** encapsular o ponteiro `IntPtr` de `MIB_IF_TABLE2` em um `SafeHandle` customizado ou `try/finally` estrito em `NetworkSampler.cs`. | 🔴 Alta | Altíssima (5 linhas de código evitam memory leak 24/7) | [02-ARQUITETURA.md](file:///c:/Projetos/Windows/Velocímetro%20internet/02-ARQUITETURA.md#L52-L55) |
| **4** | **Clique-através vs. Clique interativo na barra** | [01-ANALISE.md](file:///c:/Projetos/Windows/Velocímetro%20internet/01-ANALISE.md#L164) pede `WS_EX_NOACTIVATE` para não roubar foco ao clicar, e [02-ARQUITETURA.md](file:///c:/Projetos/Windows/Velocímetro%20internet/02-ARQUITETURA.md#L90-L91) define que o clique abre o `DetalhesFlyout`. Em WPF, abrir um `Popup` ou `Window` de Flyout com fechamento ao clicar fora ("light-dismiss") exige capturar `WM_ACTIVATE` / `Deactivated` na janela do Flyout sem tirar o estado de tela cheia de outros apps. | 🟡 Média | Alta | [01-ANALISE.md](file:///c:/Projetos/Windows/Velocímetro%20internet/01-ANALISE.md#L103-L104) · [02-ARQUITETURA.md](file:///c:/Projetos/Windows/Velocímetro%20internet/02-ARQUITETURA.md#L90-L91) |
| **5** | **Unidade no `README.md` vs `01-ANALISE.md`** | O [README.md](file:///c:/Projetos/Windows/Velocímetro%20internet/README.md#L7-L8) mostra o exemplo inicial em `MB/s`, enquanto [01-ANALISE.md](file:///c:/Projetos/Windows/Velocímetro%20internet/01-ANALISE.md#L32-L33) e [02-ARQUITETURA.md](file:///c:/Projetos/Windows/Velocímetro%20internet/02-ARQUITETURA.md#L106) definem `Mbps` (bits) como padrão configurado. Vale manter ambos sincronizados conforme sua resposta à pergunta #1 de [04-IDEIAS.md](file:///c:/Projetos/Windows/Velocímetro%20internet/04-IDEIAS.md#L47). | 🟢 Baixa | Alta | [README.md](file:///c:/Projetos/Windows/Velocímetro%20internet/README.md#L7-L8) · [01-ANALISE.md](file:///c:/Projetos/Windows/Velocímetro%20internet/01-ANALISE.md#L29-L33) |

---

## 2. Modelos de Design Gerados no Stitch (Para Avaliação)

Todos os 3 modelos foram gerados no projeto Stitch **`Velocímetro de Internet — Windows 11 (C# WPF)`** (`ID: 3690916573458587930`) e salvos integralmente (imagens PNG em alta definição + código HTML/CSS de referência + guia de tradução XAML) na pasta separada [`design/`](file:///c:/Projetos/Windows/Velocímetro%20internet/design/README.md).

````carousel
![Modelo 1 — Win11 Fluent Mica Nativo (Estilo Clima Oficial)](C:/Users/Helvécio/.gemini/antigravity/brain/f74f43b1-5a99-41b9-8fb2-f0884b4cd619/modelo-1-fluent-mica.png)
<!-- slide -->
![Modelo 2 — Telemetria Técnica & HUD de Precisão (Estilo Task Manager / Dev)](C:/Users/Helvécio/.gemini/antigravity/brain/f74f43b1-5a99-41b9-8fb2-f0884b4cd619/modelo-2-telemetria-hud.png)
<!-- slide -->
![Modelo 3 — Minimalismo Acrylic Pill & Semântica Visual](C:/Users/Helvécio/.gemini/antigravity/brain/f74f43b1-5a99-41b9-8fb2-f0884b4cd619/modelo-3-acrylic-pill.png)
````

### Detalhamento de Cada Modelo e Aplicação em C# / WPF

#### 🏆 Modelo 1: Win11 Fluent Mica Nativo (Estilo "Clima" Oficial) — *(Recomendado para a Onda 1)*
- **Arquivos salvos:** [`modelo-1-fluent-mica.png`](file:///c:/Projetos/Windows/Velocímetro%20internet/design/modelo-1-fluent-mica/modelo-1-fluent-mica.png) · [`modelo-1-fluent-mica.html`](file:///c:/Projetos/Windows/Velocímetro%20internet/design/modelo-1-fluent-mica/modelo-1-fluent-mica.html)
- **Proposta Visual:** Segue à risca a decisão de [01-ANALISE.md §3.1](file:///c:/Projetos/Windows/Velocímetro%20internet/01-ANALISE.md#L63-L106). Na barra de tarefas, o widget é 100% transparente em repouso e exibe apenas as duas linhas nítidas em `Segoe UI Variable Text` (`Typography.NumeralAlignment="Tabular"`): `↑ 1,24 Mbps` (`#4CC2FF`) e `↓ 38,7 Mbps` (`#FF6B6B`). Ao passar o mouse, surge exatamente o retângulo arredondado de `4px` com branco `6%` (`#0FFFFFFF`) do botão Clima/Widgets.
- **Flyout e Configurações:** Superfície Mica escura (`#202020`, borda `#33FFFFFF` de `1px`, raio `8px`) com status da placa física ativa (`Wi-Fi 6`), aviso explícito das virtuais ignoradas (`Tailscale`, `Radmin VPN`), picos de sessão e gráfico suave de 60 segundos.
- **Aplicação em C# WPF:** É o mais leve e direto em XAML (`Grid` de 2 linhas + 2 `TextBlock` + `Border` de hover). Custo de renderização próximo de zero.

#### ⚡ Modelo 2: Telemetria Técnica & HUD de Precisão (Estilo Task Manager / Dev)
- **Arquivos salvos:** [`modelo-2-telemetria-hud.png`](file:///c:/Projetos/Windows/Velocímetro%20internet/design/modelo-2-telemetria-hud/modelo-2-telemetria-hud.png) · [`modelo-2-telemetria-hud.html`](file:///c:/Projetos/Windows/Velocímetro%20internet/design/modelo-2-telemetria-hud/modelo-2-telemetria-hud.html)
- **Proposta Visual:** Voltado para quem quer bater o olho e saber não só o número, mas a **carga percentual da linha**. Usa fonte monoespaçada técnica (`Cascadia Mono`) e traz **4 variações de `DataTemplate` para o Widget da Barra**:
  - *(V1)* Tabular Clássico;
  - *(V2)* Com **micro-barras verticais de 3px** ao lado de cada linha indicando a saturação da banda;
  - *(V3)* Com **mini-sparkline de fundo** atrás dos números na própria barra;
  - *(V4)* Modo Compacto 1-Linha.
- **Flyout e Configurações:** Inspira-se no Gerenciador de Tarefas do Windows 11, adicionando card de **Latência/Ping (`11 ms · 1.1.1.1`)**, chaveamento de escala Linear/Logarítmica no gráfico de 60s e tabela de auditoria `GetIfTable2` mostrando o bloqueio de `Tailscale` e `Radmin VPN`.
- **Aplicação em C# WPF:** Facilmente implementável trocando o `DataTemplate` do `TaskbarWidget.xaml` (inclusive podemos oferecer V1, V2 e V3 como opção de estilo na Onda 2/3!).

#### 🎨 Modelo 3: Minimalismo Acrylic Pill & Semântica Visual
- **Arquivos salvos:** [`modelo-3-acrylic-pill.png`](file:///c:/Projetos/Windows/Velocímetro%20internet/design/modelo-3-acrylic-pill/modelo-3-acrylic-pill.png) · [`modelo-3-acrylic-pill.html`](file:///c:/Projetos/Windows/Velocímetro%20internet/design/modelo-3-acrylic-pill/modelo-3-acrylic-pill.html)
- **Proposta Visual:** Envolve cada linha da barra de tarefas em uma **micro-pílula translúcida** com absorção da própria cor (`rgba(76,194,255,0.18)` para upload e `rgba(255,107,107,0.18)` para download). Destaca-se mais da barra de tarefas e facilita a leitura periférica rápida.
- **Flyout e Configurações:** Flyout compacto (360px) estilo *Quick Settings* do Windows 11 com barras de progresso nos cards de Upload/Download, pílula de consumo acumulado do dia (`Hoje: ↑ 1,2 GB · ↓ 14,8 GB`) e switch rápido `Mbps | MB/s` direto no popup. A janela principal ilustra o **Simulador de Contraste WCAG (Escuro vs. Claro)**, as **3 etapas de colisão (`TaskbarLayout`)** e as **5 paletas predefinidas da Onda 3**.
- **Aplicação em C# WPF:** Basta envolver cada `TextBlock` do `TaskbarWidget.xaml` em um `Border` com `CornerRadius="4"` e `SolidColorBrush` com canal alfa `0x2E` (18%).

---

## 3. Snippets XAML Prontos Salvos em `design/README.md`

No arquivo [`design/README.md`](file:///c:/Projetos/Windows/Velocímetro%20internet/design/README.md) já deixei prontos os blocos XAML de `ResourceDictionary` (paletas Escura e Clara calibradas) e os 3 `TaskbarWidget.xaml` correspondentes a cada modelo para copiarmos direto na **Onda 0 / Onda 1**.
