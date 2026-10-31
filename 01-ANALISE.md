# 01 — Análise do pedido

## 1. O pedido, item por item

| # | Requisito | Leitura | Dificuldade |
|---|---|---|---|
| R1 | Programa para Windows 11 | Alvo único: Win 11 x64 (vale testar no Win 10 de brinde) | — |
| R2 | **Silencioso** | Sem janela, sem console, sem som, sem notificação, fora do Alt+Tab, CPU ~0% | Baixa |
| R3 | **Inicia com o sistema** | Ao fazer login do usuário, sem pedir administrador | Baixa |
| R4 | **Fica na barra de tarefas** | Os números aparecem *na própria barra*, sempre à vista (não escondidos na bandeja) | **Alta** (ver §3) |
| R5 | Velocidade em **tempo real** | Atualização a cada 1 s (configurável 0,5–2 s) | Baixa |
| R6 | Upload **e** download **ao mesmo tempo** | Duas linhas empilhadas, legíveis ao mesmo tempo | Média (espaço) |
| R7 | Seta ↑ à esquerda do upload, ↓ à esquerda do download | Glifo antes do número | Baixa |
| R8 | Upload **azul**, download **vermelho** (seta + número) | Cor por linha | Baixa |
| R9 | **Futuro:** escolher cores | Arquitetura já preparada (cores em configuração, não fixas no código) | Baixa se planejado agora |
| R10 | Linguagem "mais fluida", C# se for a melhor | Fluida = desenvolvimento rápido **e** app leve/suave | Decisão em 02 |

## 2. Ambiguidades e como decidi (pode mudar qualquer uma)

1. **"Na barra de tarefas" = na barra mesmo, ou na bandeja (perto do relógio)?**
   ✅ **Respondido pelo dono (2026-10-31): "como se fosse a barra do clima"** — um bloco próprio
   na barra, **fora** dos quadradinhos da bandeja (volume, rede…), com o espaço que precisar.
   Um ícone de bandeja tem 16×16 px (24×24 a 150%), pequeno demais para dois números legíveis,
   então o pedido e a técnica convergem.
   → **Decisão:** os números ficam num **bloco estilo "Clima/Widgets"** (ver §3.1), numa janelinha
   própria encostada na barra; a bandeja fica com um ícone discreto para o menu (Configurações /
   Pausar / Sair), que pode ser escondido depois.

2. **Megabytes ou megabits?** As operadoras vendem em **Mbps (bits)**; o navegador mostra
   download em **MB/s (bytes)**. 100 Mbps ≈ 12,5 MB/s, e essa confusão faz parecer que "a internet
   está 8× mais lenta".
   ✅ **Respondido pelo dono (2026-10-31): padrão em `MB/s` (bytes)**, com opção de alterar para
   `Mbps` (bits) nas configurações.
   → **Decisão:** padrão **bytes (`KB/s`, `MB/s`, `GB/s`)**; troca para bits (`Kbps`, `Mbps`, `Gbps`)
   nas configurações.

3. **Qual "internet"?** O PC pode ter Wi-Fi + cabo + VPN + adaptadores virtuais (Hyper-V, WSL,
   VirtualBox, Tailscale). Somar tudo **conta o mesmo tráfego duas vezes** (VPN por cima do Wi-Fi).
   → **Decisão:** soma só interfaces físicas ativas (Ethernet/Wi-Fi) com gateway; lista de
   inclusão/exclusão manual nas configurações.
   *(Nesta máquina a bandeja mostra **Tailscale** e **Radmin VPN** ativos além do Wi-Fi —
   exatamente o caso de dupla contagem.)*

4. **Ordem das linhas:** o pedido cita upload primeiro → **upload em cima, download embaixo**
   (mesma ordem do Gerenciador de Tarefas e do TrafficMonitor). Configurável depois.

## 3. A armadilha principal: o Windows 11 tirou o "encaixe" na barra

No Windows 10 existia a API **DeskBand** (barras de ferramentas da barra de tarefas): um app
registrava um componente e o Explorer o desenhava *dentro* da barra. **O Windows 11 removeu isso**
— a barra foi reescrita e não carrega mais DeskBands nem barras de ferramentas.

Consequência: **não existe forma oficial** de pôr conteúdo dentro da barra do Win 11. Todo app
desse tipo (TrafficMonitor, NetSpeedMonitor de terceiros etc.) usa uma de duas técnicas:

| Técnica | Como funciona | Prós | Contras |
|---|---|---|---|
| **A. Janela filha da barra** (`SetParent` em `Shell_TrayWnd`) | Nossa janela vira "filha" da barra e se move com ela | Fica sempre por cima da barra, some junto com ela em tela cheia / auto-ocultar | Depende de detalhe interno do Explorer; uma atualização do Windows pode quebrar; transparência de janela filha exige cuidado (Win 8+ suporta janela filha *layered*) |
| **B. Janela solta "sempre no topo"** posicionada sobre a barra | Janela comum, sem borda, topmost, reposicionada quando a barra muda | 100% API pública, não quebra com update | Pode "brigar" com a barra pelo topo (pisca ao clicar no Iniciar), precisa se esconder sozinha em tela cheia e com auto-ocultar |

→ **Decisão:** implementar **A como padrão, B como plano de reserva automático** (se o `SetParent`
falhar ou a barra não for achada, cai em B). As duas compartilham o mesmo desenho; só muda o "dono"
da janela. Isso é isolado num único componente (`TaskbarHost`) — ver 02.

### 3.1 Verificação: dá para ficar "como o Clima"? — **Sim, visualmente. Não, oficialmente.**

**O que é o Clima:** é o botão **Widgets** do próprio Explorer (ícone do tempo + temperatura +
condição). Ele é desenhado *por dentro* da barra, e o Explorer **reserva** o espaço dele.

**Existe forma oficial de um app de terceiros ocupar aquele lugar?** **Não.** Até a data deste
plano, o Windows 11 deixa terceiros criarem *widgets* (Windows App SDK, "widget providers"), mas
eles aparecem **dentro do painel** que abre ao clicar no Clima — **não** na barra. O conteúdo do
botão da barra é exclusivo da Microsoft. Portanto o caminho é **imitar o Clima**: uma janela nossa
encostada na barra (técnica A/B acima), com o mesmo aspecto e comportamento.

**Medição real nesta máquina** (UI Automation sobre `Shell_TrayWnd`, 2026-10-31, tela 2560×1600 a 150%):

| Elemento | Posição X (px físicos) | Observação |
|---|---|---|
| Barra | 0 → 2560, altura **72 px** (= 48 px lógicos) | embaixo |
| Iniciar + 23 apps fixados/abertos | 0 → **1169** | barra alinhada à esquerda (`TaskbarAl=0`) |
| **Espaço livre** | **1169 → 1797 = 628 px** (~418 px lógicos) | onde o bloco entra |
| Bandeja ("^", Tailscale, NVIDIA…, rede, volume, bateria, relógio) | 1797 → 2560 | |
| Widgets/Clima | **desligado** (`TaskbarDa=0`) | não há conflito |

- O bloco precisa de ~100 px lógicos = **~150 px físicos** → cabe com folga (sobram ~470 px).
- **É exatamente onde o Clima ficaria:** com a barra alinhada à esquerda, o Windows põe o botão
  Widgets **logo à esquerda do "^"** da bandeja. Nosso bloco ocupa essa mesma vaga.
- Com barra **centralizada**, o Clima vai para o **canto esquerdo**; nesse caso o padrão do nosso
  bloco também passa a ser o canto esquerdo (a não ser que o Widgets esteja ligado — aí fica junto
  da bandeja para não brigar com ele).

**Como fica (imitando o Clima):**

```
 … [Paint][Antigravity][Claude]                   ┌──────────────┐ ^  🔒 🟩 … 📶 🔊 🔋  09:04
                                                  │ ↑  1,24 Mbps │                    31/10/2026
                                                  │ ↓  38,7 Mbps │
                                                  └──────────────┘
                                                   ↑ realce arredondado só ao passar o mouse
```

- Fundo **transparente**; ao passar o mouse aparece o mesmo **realce arredondado** translúcido
  dos botões da barra (como no Clima).
- **Clique** abre um painel suspenso (flyout) com mini gráfico e detalhes, assim como o Clima abre
  o painel de widgets (ideia antes futura, agora natural — ver 04). **Clique-direito** = menu.
- Mesma fonte do sistema (Segoe UI Variable), mesmo tamanho do texto do relógio.

**O que o Clima tem e nós não teremos (limitação honesta):** o Explorer **não reserva espaço**
para o nosso bloco. Se você abrir apps a ponto dos botões chegarem até ele (hoje seria preciso
**~10 apps a mais**), os botões passariam **por baixo** do bloco. Mitigação no `TaskbarHost`:
vigiar a borda direita do último botão de app e, se encostar, (1) mudar para **modo compacto**
de uma linha (`↑1,2 ↓38`) e, se ainda não couber, (2) esconder até liberar espaço.
O TrafficMonitor, app conhecido que faz isso, tem a mesma limitação.

### Casos de borda que o encaixe tem que tratar (lista de teste)
- **Onde pôr:** ver §3.1 — padrão "vaga do Clima" (à esquerda da bandeja com barra à esquerda;
  canto esquerdo com barra centralizada). Também configurável: manual (Ctrl + arrastar).
- **Colisão com os botões de apps:** modo compacto → esconder (ver §3.1).
- **Explorer reinicia** (trava, atualização, `taskkill explorer`): a barra é recriada e a nossa
  janela some. Escutar a mensagem `TaskbarCreated` e reencaixar.
- **Vários monitores:** barra principal por padrão; opção "em todas as barras" (`Shell_SecondaryTrayWnd`).
- **DPI/escala:** esta tela é 2560×1600 a **150%** (144 DPI). App precisa ser *Per-Monitor V2*
  (WPF faz isso com uma linha no manifesto) para não ficar borrado.
- **Tela cheia (jogo/vídeo):** não pode aparecer por cima. Com a técnica A é automático; com B,
  detectar e esconder.
- **Auto-ocultar a barra:** acompanhar (A) ou esconder (B).
- **Tema claro/escuro:** esta máquina está em **escuro** (`SystemUsesLightTheme=0`); ver §4.

## 4. Análise das cores pedidas (azul/vermelho)

- **Boa escolha de par:** azul × vermelho é distinguível para os daltonismos comuns
  (protanopia/deuteranopia confundem **vermelho × verde**, não vermelho × azul). Além disso as
  **setas** já diferenciam sem depender de cor — acessível de verdade.
- **Cuidado com o fundo escuro:** azul "puro" (#0078D4, o azul do Windows) sobre a barra escura
  (~#1C1C1C) tem contraste ≈ **3,9:1** — abaixo de 4,5:1, fica apagado em letra pequena.
  → Usar **tons ajustados por tema**:

  | | Barra escura (padrão aqui) | Barra clara |
  |---|---|---|
  | Upload (azul) | `#4CC2FF` (≈ 9:1) | `#005FB8` (≈ 6:1) |
  | Download (vermelho) | `#FF6B6B` (≈ 6,5:1) | `#C42B1C` (≈ 5,5:1) |

  O app lê o tema do Windows e troca sozinho; ao mudar o tema, atualiza na hora.
- **"Escolher cores no futuro"** já entra na arquitetura agora: cores ficam no arquivo de
  configuração (`settings.json`) como **dois pares** (escuro/claro). A tela de escolha é só
  uma janela a mais (Onda 3). Fazer assim não custa nada hoje e evita refazer depois.

## 5. Legibilidade em espaço apertado

A barra do Win 11 tem 48 px lógicos de altura → cabem **duas linhas de ~9–10 pt**.

- **Largura fixa** para os números não "dançarem" a cada segundo: fonte com algarismos de
  largura igual (**Segoe UI Variable** com `tabular-nums`, ou **Cascadia Mono/Consolas**) e
  sempre 3 algarismos significativos: `9,87` · `98,7` · `987` · depois troca a unidade.
- **Unidade automática:** `bps → Kbps → Mbps → Gbps`, com histerese (só troca de unidade se o
  valor ficar fora da faixa por 2 leituras) para não piscar entre `999 Kbps` e `1,00 Mbps`.
- **Suavização:** média móvel exponencial leve (α≈0,5) — o tráfego real é aos solavancos e o
  número cru fica ilegível. Configurável (0 = cru).
- **Decimal em português:** vírgula (`38,7 Mbps`), seguindo a cultura do Windows.
- Largura estimada: ~95 px lógicos (`↓ 999 Mbps`).

## 6. "Silencioso" de verdade — checklist

- [ ] Sem console (`WinExe`), sem janela principal, **fora do Alt+Tab** (`WS_EX_TOOLWINDOW`)
- [ ] Não rouba foco ao clicar nem ao atualizar (`WS_EX_NOACTIVATE`)
- [ ] **Instância única** (mutex nomeado) — abrir de novo não duplica
- [ ] CPU média **< 0,3%**, memória de trabalho **< 60 MB**, 0 alocação relevante por segundo
- [ ] Nenhuma notificação, som, popup de erro — erros vão para um log em `%LocalAppData%`
- [ ] **Nenhum acesso à internet** pelo próprio app (sem telemetria; nada de "testar velocidade"
      em segundo plano gastando a banda que ele mede)
- [ ] Não pede administrador (nem para instalar, nem para iniciar com o Windows)

## 7. Riscos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Muitos apps abertos fazem os botões chegarem ao bloco (sem espaço reservado) | Baixa aqui (~470 px de folga) | Médio | Modo compacto de 1 linha → esconder (§3.1) |
| Update do Windows muda a barra e quebra o encaixe A | Média | Alto | Fallback automático para B; detecção ao iniciar |
| Dupla contagem com VPN/virtuais | Alta | Médio | Filtro de interfaces físicas + lista manual |
| Antivírus/SmartScreen desconfia do .exe não assinado | Média | Médio | Instalador Velopack; assinatura de código opcional (Onda 4) |
| Número "dançando" / ilegível | Média | Médio | Largura fixa, histerese, suavização (§5) |
| Consumo de memória do WPF acima do esperado | Baixa | Baixo | Medir na Onda 0; plano B WinForms+GDI (ver 02) |
