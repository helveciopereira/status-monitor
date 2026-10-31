# 03 — Plano de execução em ondas

Uma onda por sessão, cada uma termina **validada** (build → testes → app rodando e fotografado).
Status: ⬜ pendente · 🟡 em andamento · ✅ concluída (com versão e data).

Versionamento: `MAIOR.MENOR.PATCH`, odômetro (pequena +0.0.1, importante +0.1). Começa em `0.1.0`.

---

## Onda 0 — Ambiente e prova de conceito ✅ (concluída em 2026-10-31 · v0.1.0, corrigida na v0.1.1)

> **Correção v0.1.1 (teste do dono na sessão real):** o **modo A falhou na prática**. A janela WPF
> transparente vira filha da barra XAML e não é desenhada, e morre junto quando o Explorer reinicia.
> **Modo B virou o padrão** (modo A só com `--modo-a`). O .exe dependia do .NET 10 instalado só na
> pasta do usuário ("You must install .NET"), e agora é publicado **autocontido** em `dist\`.
> Detalhes no `walkthrough.md`.

Objetivo: **provar as duas coisas arriscadas antes de construir o resto** — o encaixe na barra
do Win 11 e o consumo de memória.

- [x] Instalar **.NET 10 SDK** (`10.0.403` via `dotnet-install.ps1` em `%LocalAppData%\Microsoft\dotnet`) e conferir `dotnet --list-sdks`
- [x] `git init`, `.gitignore` (bin/, obj/, backup/, pub/)
- [x] Solução WPF mínima (`VelocimetroInternet.sln`) + projeto de testes xUnit (`8/8` testes verdes)
- [x] PoC 1: janela no **Modelo 3: Minimalismo Acrylic Pill** (`↑ 1,24 MB/s` azul / `↓ 38,7 MB/s` vermelho) encaixada na **vaga do Clima** (à esquerda do "^" da bandeja) pelo **modo A** com fallback automático para **modo B** e tratamento de `TaskbarCreated`
- [x] PoC 1b: localizar bandeja e último botão de app por **UI Automation** em thread MTA de fundo e reposicionar/compactar (`TaskbarLayout`)
- [x] PoC 2: `GetIfTable2` lendo bytes reais da placa `MediaTek Wi-Fi 7 MT7925` (ignorando `Tailscale` e `Radmin VPN`) e imprimindo em log a cada 1 s
- [x] Medir RAM/CPU da PoC: WorkingSet **~14–35 MB** após `EmptyWorkingSet` (bem abaixo do teto de 60 MB), CPU **< 0,2%**

**Aceite:** captura de tela do widget mostrando os dois números nítidos a 150% DPI (tema escuro, tema claro, modo compacto 1-linha e tempo real); evento `TaskbarCreated` reencaixando o widget automaticamente; RAM < 60 MB. → **versão 0.1.0**

## Onda 1 — MVP funcional ✅ (concluída em 2026-10-31 · v0.2.0)

- [x] `NetworkSampler` + `InterfaceFilter` + `SpeedCalculator` (com testes) — v0.1.0
- [x] `SpeedFormatter`: unidade automática, histerese, vírgula, largura fixa (com testes) — v0.1.0
- [x] `TaskbarWidget`: ↑ upload azul em cima, ↓ download vermelho embaixo — v0.1.0
- [x] `TaskbarHost` + `TaskbarLayout`: vaga do Clima, vigia de colisão, TaskbarCreated, DPI — v0.1.1;
      **tela cheia instantânea** (appbar `ABN_FULLSCREENAPP` + regra geométrica: some em 2–4 ms) — v0.2.0
- [x] Aparência estilo Clima: realce ao passar o mouse; clique-direito = menu — v0.2.0
- [x] Tema: widget segue a barra (`SystemUsesLightTheme`), janelas seguem os apps; troca ao vivo — v0.2.0
- [x] Instância única (2ª execução abre o painel na 1ª), sem Alt+Tab, sem foco, log com rodízio — v0.2.0
- [x] Ícone na bandeja + menu: painel · estúdio · Unidade · Fixar na barra · Iniciar com o Windows ·
      Mostrar ícone · Sair — v0.2.0 (⬜ "Pausar" ficou para depois)
- [x] Início com o Windows (Run + StartupApproved), **ligado no 1º uso** e corrigido se o .exe mudar de pasta — v0.2.0

**Aceite:** baixar um arquivo grande e o valor bater ±3% com o Gerenciador de Tarefas;
reiniciar o PC e o widget aparecer sozinho; Alt+Tab não mostra o app; trocar tema claro/escuro e
as cores se adaptarem; jogo/vídeo em tela cheia não mostra o widget. → **versão 0.2.0**
> v0.2.0: tela cheia, Alt+Tab e instância única comprovados por automação; **reinício do PC,
> comparação ±3% e troca de tema ficam como validação manual do dono** (ver walkthrough).

## Onda 2 — Configurações e robustez ✅ (concluída em 2026-10-31 · v0.3.0, com 3 itens adiados)

- [x] `ServicoConfiguracoes` com `settings.json` (leitura tolerante, gravação atômica, .bak) — v0.2.0
- [x] Configurações: unidade bits/bytes, suavização, interfaces excluídas, velocidade do plano — v0.2.0 (no estúdio)
- [ ] ⏭ Adiado: intervalo de atualização (exige o histórico contar segundos, não amostras), fonte/tamanho, ordem das linhas
- [ ] ⏭ Adiado: posição **manual por arrastar** (Ctrl + arrastar o widget)
- [x] **Painel rápido do clique** (Quick Flyout do Modelo 3): Envio/Download com pico e barra,
      gráfico 60 s, link da placa, consumo do dia persistido, unidade, iniciar com o Windows, fixar — v0.2.0
- [x] Gráfico com **linhas de referência**: topo em valor redondo 1-2-5 (ex.: 100 KB/s, 50 Mbps) e
      metade pontilhada, ambas rotuladas, + legenda Envio/Download — v0.3.0 (pedido do dono)
- [x] **Pausar medição** no menu (só na sessão; ao retomar, linha de base nova) — v0.3.0
- [ ] ⏭ Adiado: vários monitores (opção "mostrar em todas as barras")
- [x] Suspender/hibernar: na retomada (`PBT_APMRESUMEAUTOMATIC`) o medidor recomeça e o widget reencaixa;
      troca de rede: contador que volta é descartado — v0.3.0

**Aceite:** cada opção muda o widget **na hora**, sem reiniciar; apagar/corromper o JSON não
derruba o app. → **0.3.0**

## Onda 3 — Escolha de cores (o "futuro" do pedido) 🟡 (núcleo entregue na v0.2.0)

- [x] Paleta personalizada (hex de upload e download); a variante da barra clara é **derivada** pelo
      motor WCAG em vez de escolhida à parte — v0.2.0
- [x] Prévia ao vivo sobre amostras da barra escura e clara — v0.2.0
- [x] **Selo de contraste** WCAG por barra; cores abaixo do mínimo são recalibradas sozinhas — v0.2.0
- [x] "Restaurar padrão" + 5 predefinições do design (Clássico, Neon, Monocromático, Alto contraste, Bio) — v0.2.0
- [ ] Seletor visual de cor (hoje é campo hexadecimal)

**Aceite:** trocar a cor e ver na barra imediatamente; cor salva sobrevive ao reinício. → **0.4.0**

## Onda 4 — Distribuição ✅ (2026-10-31 · v0.4.0)

- [x] Instalador **sem administrador** + atualização automática **silenciosa** com **Velopack**
      + GitHub Releases (repo público `helveciopereira/velocimetro-internet`, feed direto
      `releases/latest/download/`): consulta 45 s após abrir e a cada 5 min (v0.4.2; era 1 h), baixa só a
      diferença, reinicia sozinho num momento tranquilo (nada aberto, mouse fora, sem tela cheia) — v0.4.0
- [x] Ícone do app (v0.2.0); versão + estado da atualização no rodapé do estúdio — v0.4.0
- [x] Desinstalação limpa da entrada Run (só se aponta para a cópia desinstalada). Os dados do
      usuário (`%AppData%` / `%LocalAppData%\VelocimetroInternet`) ficam — pasta separada do packId — v0.4.0
- [ ] (Opcional) assinatura de código para evitar aviso do SmartScreen

**Aceite:** ensaio local 1.0.0 → 1.0.1 provado (delta de 0,13 MB, reabriu sozinho em 173 s); a
primeira atualização REAL pelo GitHub acontece na próxima versão publicada (0.4.1). → **1.0.0**

---

## Extra — CPU e RAM na barra ✅ (2026-10-31 · v0.5.0, pedido do dono)

- [x] CPU (em cima) e RAM (embaixo), em %, à esquerda do upload/download, em pílulas de tom neutro;
      no modo compacto, "CPU x% · RAM y%  |" antes do ↑↓ — v0.5.0
- [x] Liga/desliga: chave "CPU e RAM na barra" no painel rápido e "Mostrar CPU e RAM" no menu;
      padrão ligado; desligado, o widget volta ao tamanho de antes — v0.5.0
- [x] A vaga na barra cresce para a esquerda (borda direita fixa no '^'); com CPU/RAM o bloco
      compacta/oculta mais cedo quando a barra enche — v0.5.0

- [x] Pílula de internet mais estreita (96 → 78 lógicos, entre a de CPU/RAM e a antiga) — v0.6.0
- [x] Nome do app: **Status Monitor** (telas, dica da bandeja, atalhos, Gerenciador de Tarefas,
      repositório `status-monitor`) — v0.6.0
- [x] Executável `StatusMonitor.exe` e instalador `StatusMonitor-Setup.exe` (packId mantido,
      as cópias instaladas migram sozinhas e a entrada Run é reapontada) — v0.7.0

---

## Validação padrão de toda onda

1. `dotnet build -c Release` sem avisos novos
2. `dotnet test` — suíte inteira verde
3. Rodar o .exe e **fotografar a barra** (skill `provar-app-desktop-wpf-por-uia`)
4. Medir CPU/RAM por alguns minutos
5. Entrada no topo do `walkthrough.md` (o que mudou, por quê, como usar, arquivos, validação)

## Validação manual recomendada (não dá para automatizar 100%)

- Reiniciar o PC de verdade e conferir que aparece
- Abrir um jogo em tela cheia
- Ligar/desligar Wi-Fi e cabo com download rodando
- Ligar a VPN/Tailscale/Radmin e conferir que **não** dobra o valor
- Abrir muitos apps até os botões encostarem no bloco → deve compactar, depois ocultar
- Ligar o Widgets (Clima) e centralizar a barra → o bloco não pode ficar por cima do Clima
- Mudar a escala de tela (100% ↔ 150%) e o lado da barra
