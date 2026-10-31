# 02 — Arquitetura e escolha de tecnologia

## 1. Linguagem: comparação honesta

"Fluida" aqui tem dois sentidos: **fluida de escrever** (rápido de desenvolver e manter) e
**fluida de rodar** (leve, sem travar, sem piscar). A tabela pesa os dois:

| Opção | Escrever | Rodar (RAM / tamanho) | Acesso ao Windows (Win32, barra, DPI) | Veredito |
|---|---|---|---|---|
| **C# + .NET 10 + WPF** | ⭐⭐⭐⭐⭐ | ~40–60 MB / ~70 MB self-contained (ou ~1 MB dependente do runtime) | ⭐⭐⭐⭐ P/Invoke direto; transparência com anti-aliasing nativa | ✅ **Escolhida** |
| C# + .NET 10 + WinForms | ⭐⭐⭐⭐ | ~25–40 MB | ⭐⭐⭐⭐ | Plano B se a RAM do WPF incomodar; transparência com texto suave dá mais trabalho |
| C# + WinUI 3 | ⭐⭐⭐ | ~80+ MB | ⭐⭐ janela sem borda/ferramenta/filha é briga com o framework | ❌ inadequado para widget de barra |
| C++ / Win32 puro | ⭐⭐ | ~2–5 MB | ⭐⭐⭐⭐⭐ | Mais leve possível, mas muito mais lento de escrever e de evoluir (tela de cores, gráfico) |
| Rust (windows-rs) | ⭐⭐⭐ | ~2–5 MB | ⭐⭐⭐⭐⭐ | Excelente runtime; ergonomia de UI no Windows ainda fraca |
| Electron / Tauri / Python | ⭐⭐⭐⭐ | 80–200 MB (Electron) | ⭐⭐ | ❌ pesado/indireto demais para algo que roda 24 h |

**Por que C#/WPF vence:** é a linguagem "nativa de fato" do Windows para apps de usuário, chama
qualquer API Win32 com uma linha (`[LibraryImport]`), desenha texto com anti-aliasing perfeito
em janela transparente, escala por DPI sozinho, e a evolução prevista (tela de cores, gráfico,
histórico) é trivial nela. O custo — ~50 MB de RAM — é irrelevante num PC atual e **medido na
Onda 0** antes de seguir. Bônus: esta máquina já tem receitas testadas para WPF
(auto-update com Velopack e teste de UI por UI Automation).

**.NET 10** é a versão LTS vigente (suporte até nov/2028).

## 2. Visão geral

```
┌──────────────────────────── Processo único (WinExe, instância única) ─────────────────────────┐
│                                                                                                │
│  NetworkSampler ──(a cada 1 s)──► SpeedCalculator ──► SpeedFormatter ──► TaskbarWidget (WPF)    │
│   lê contadores      delta bytes /     EMA + histerese   "↑ 1,24 Mbps"     2 linhas, cores       │
│   das interfaces     delta tempo                                            │                   │
│        ▲                                                                    ▼                   │
│  InterfaceFilter                                                       TaskbarHost              │
│   (físicas, ativas,                                                     encaixa na barra        │
│    lista do usuário)                                                    (A: SetParent / B: topo) │
│                                                                         escuta TaskbarCreated,  │
│  SettingsService ◄──► settings.json (%AppData%\VelocimetroInternet)     DPI, tema, tela cheia   │
│        │                                                                                        │
│        ├──► ThemeWatcher (claro/escuro → troca paleta)                                          │
│        ├──► StartupManager (HKCU\...\Run)                                                       │
│        └──► TrayIcon (menu: Configurações · Pausar · Iniciar com o Windows · Sair)              │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
```

## 3. Como medir a velocidade

**Princípio:** o Windows mantém, por interface de rede, um contador acumulado de bytes recebidos e
enviados. Velocidade = (contador agora − contador anterior) ÷ (tempo decorrido real).

- **Fonte:** `GetIfTable2` (iphlpapi.dll) → `MIB_IF_ROW2.InOctets / OutOctets` (64 bits, sem
  estouro), via P/Invoke. É o que o Gerenciador de Tarefas usa; não precisa de administrador.
  *(Alternativa gerenciada: `NetworkInterface.GetIPv4Statistics()` — mais simples, mas aloca
  objetos a cada leitura e só soma IPv4 em algumas versões; fica como fallback.)*
- **Tempo:** `Stopwatch` (não o relógio do sistema — ajuste de horário daria picos absurdos).
- **Filtro de interfaces** (`InterfaceFilter`): incluir `Type` Ethernet (6) e Wi-Fi (71), com
  `OperStatus = Up`, `MediaConnectState = Connected`, e flag `HardwareInterface = true`;
  excluir loopback, túneis, `vEthernet`, Tailscale/WireGuard/VPN, VirtualBox/VMware.
  Lista de "sempre incluir / sempre excluir" por nome nas configurações.
- **Robustez:** interface que some/reaparece (Wi-Fi reconecta) → contador zera; se o delta der
  negativo, descarta a amostra em vez de mostrar lixo.
- **Timer:** `PeriodicTimer` numa task de fundo; publica o valor para a UI via `Dispatcher`.
  Ao travar a tela / suspender, pausa (zero custo com o PC ocioso).

## 4. Como aparecer na barra (`TaskbarHost`)

1. `FindWindow("Shell_TrayWnd")` → janela da barra principal; `GetWindowRect` + `SHAppBarMessage`
   para saber posição/altura/borda (embaixo, em cima…).
2. Achar a **"vaga do Clima"** (ver 01 §3.1). No Win 11 a barra é XAML, então os retângulos
   confiáveis vêm da **UI Automation** (validado nesta máquina em 2026-10-31):
   - borda esquerda da bandeja = 1º elemento `AutomationId = "SystemTrayIcon"` (o botão "^");
   - borda direita dos apps = maior `X + largura` entre os `Taskbar.TaskListButtonAutomationPeer`;
   - **não** usar o retângulo de `MSTaskSwWClass`: medido aqui com 288 px, desatualizado em
     relação aos 23 botões reais (iam até 1169 px) — mente.
   Barra à esquerda → bloco encostado à esquerda da bandeja (margem 8 px lógicos).
   Barra centralizada → canto esquerdo, exceto se Widgets ligado (`TaskbarDa=1`) → junto da bandeja.
   Fallback se a UIA falhar: `FindWindowEx(Shell_TrayWnd, "TrayNotifyWnd")`.
   **Vigia de colisão:** a cada mudança na lista de apps (evento UIA `StructureChanged`, com
   checagem leve a cada 2 s de reserva), se `bordaApps + margem > xBloco` → modo compacto de
   1 linha; se ainda não couber → oculta até liberar.
3. **Modo A:** `SetParent(nossaJanela, Shell_TrayWnd)` + estilo `WS_CHILD`, coordenadas relativas.
   **Modo B (fallback):** janela `Topmost`, `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, coordenadas de
   tela; esconde em tela cheia (`SHQueryUserNotificationState`) e acompanha auto-ocultar.
4. `RegisterWindowMessage("TaskbarCreated")` → ao reiniciar o Explorer, refaz o encaixe.
5. Reposiciona em `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, `WM_SETTINGCHANGE` (barra mudou de lado,
   de alinhamento ou de tema).
6. **Aparência e comportamento "estilo Clima":** fundo transparente; ao passar o mouse, realce
   arredondado translúcido igual aos botões da barra (branco 6% no escuro / preto 5% no claro,
   raio 4 px lógicos); **clique** abre um flyout (painel suspenso acima da barra, fundo Mica/Acrylic)
   com mini gráfico; **clique-direito** abre o mesmo menu da bandeja.
7. Por que não um widget "de verdade": widgets de terceiros (Windows App SDK) só aparecem
   **dentro do painel** de Widgets, nunca na barra — descartado (ver 01 §3.1).

Tudo isso fica **isolado** em `TaskbarHost`: se uma atualização do Windows quebrar o encaixe,
o conserto acontece num único arquivo.

## 5. Configuração (`settings.json`)

Já nasce com as cores configuráveis (R9), mesmo que a tela de escolha só venha na Onda 3:

```jsonc
{
  "versao": 1,
  "intervaloMs": 1000,
  "unidade": "bytes",           // "bytes" (MB/s, padrão escolhido) | "bits" (Mbps)
  "suavizacao": 0.5,            // 0 = valor cru
  "ordem": "uploadEmCima",
  "posicao": "vaga-do-clima",   // | "junto-da-bandeja" | "esquerda" | "manual"
  "aoColidir": "compactar",     // | "ocultar" — quando os botões de apps chegam no bloco
  "offsetManualPx": 0,
  "iniciarComWindows": true,
  "mostrarIconeBandeja": true,
  "estiloVisual": "acrylic-pill", // Modelo 3 escolhido no Stitch
  "fonte": { "familia": "Segoe UI Variable Text", "tamanhoPt": 9 },
  "cores": {
    "escuro": { "upload": "#4CC2FF", "download": "#FF6B6B" },
    "claro":  { "upload": "#005FB8", "download": "#C42B1C" }
  },
  "interfaces": { "incluir": [], "excluir": [] }
}
```

Leitura tolerante (campo faltando = padrão; arquivo corrompido = renomeia para `.bak` e recria),
gravação atômica (escreve em `.tmp` e renomeia).

## 6. Início com o Windows

- `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `"VelocimetroInternet" = "<exe> --autostart"`.
  Sem administrador, aparece em *Configurações › Apps › Inicialização* (o usuário pode desligar por lá,
  e o app respeita — ler `StartupApproved\Run` antes de afirmar que está ativo).
- Descartado: Agendador de Tarefas (exige mais permissões e é mais opaco) e pasta Inicializar
  (atalho frágil).

## 7. Estrutura do código

```
VelocimetroInternet/
├─ src/VelocimetroInternet/
│  ├─ App.xaml(.cs)              # ciclo de vida, instância única, sem StartupUri
│  ├─ Medicao/
│  │  ├─ NetworkSampler.cs       # GetIfTable2 via P/Invoke
│  │  ├─ InterfaceFilter.cs
│  │  └─ SpeedCalculator.cs      # delta/tempo, EMA, descarte de amostra inválida
│  ├─ Apresentacao/
│  │  ├─ SpeedFormatter.cs       # unidade automática + histerese + vírgula
│  │  ├─ TaskbarWidget.xaml(.cs) # as duas linhas + realce ao passar o mouse
│  │  ├─ DetalhesFlyout.xaml(.cs)# painel do clique (estilo painel do Clima)
│  │  ├─ TaskbarLayout.cs        # lê retângulos por UIA, calcula a vaga e a colisão (puro, testável)
│  │  └─ TaskbarHost.cs          # encaixe A/B, TaskbarCreated, DPI, tela cheia
│  ├─ Sistema/
│  │  ├─ NativeMethods.cs        # todas as assinaturas Win32 num lugar só
│  │  ├─ ThemeWatcher.cs
│  │  ├─ StartupManager.cs
│  │  └─ TrayIcon.cs
│  └─ Config/
│     ├─ Settings.cs
│     └─ SettingsService.cs
├─ tests/VelocimetroInternet.Tests/   # xUnit: Calculator, Formatter, Filter, Settings
├─ walkthrough.md                     # changelog (entrada nova no topo a cada versão)
└─ VelocimetroInternet.sln
```

**Testável sem tela:** medição, cálculo, formatação, filtro e configuração são classes puras
(entram números, saem números/textos) → testes unitários. A parte visual/encaixe é provada
rodando o .exe e fotografando a barra (UI Automation + captura de tela).

## 8. Orçamento de desempenho (medido, não suposto)

| Métrica | Alvo | Como medir |
|---|---|---|
| CPU média | < 0,3% | Contador de desempenho do processo por 10 min |
| RAM (working set) | < 60 MB (WPF) | Após 1 h rodando; `EmptyWorkingSet` após iniciar |
| Tempo até aparecer | < 1 s após login | Log com timestamp |
| Alocação por tick | ~0 | `dotnet-counters` (gen0 GCs/min ≈ 0) |
| Exatidão | ±3% do Gerenciador de Tarefas | Download grande e comparação lado a lado |
