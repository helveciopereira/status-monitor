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
      Mostrar ícone · Sair — v0.2.0 ("Pausar" entrou na v0.3.0, Onda 2)
- [x] Início com o Windows (Run + StartupApproved), **ligado no 1º uso** e corrigido se o .exe mudar de pasta — v0.2.0

**Aceite:** baixar um arquivo grande e o valor bater ±3% com o Gerenciador de Tarefas;
reiniciar o PC e o widget aparecer sozinho; Alt+Tab não mostra o app; trocar tema claro/escuro e
as cores se adaptarem; jogo/vídeo em tela cheia não mostra o widget. → **versão 0.2.0**
> v0.2.0: tela cheia, Alt+Tab e instância única comprovados por automação; **reinício do PC,
> comparação ±3% e troca de tema ficam como validação manual do dono** (ver walkthrough).

## Onda 2 — Configurações e robustez ✅ (concluída em 2026-10-31 · v0.3.0; itens adiados entregues na v0.8.0)

- [x] `ServicoConfiguracoes` com `settings.json` (leitura tolerante, gravação atômica, .bak) — v0.2.0
- [x] Configurações: unidade bits/bytes, suavização, interfaces excluídas, velocidade do plano — v0.2.0 (no estúdio)
- [x] Intervalo de atualização (0,5/1/2/5 s; o histórico agora conta segundos), fonte/tamanho, ordem das linhas — v0.8.0 (2026-10-31)
- [x] Posição **manual por arrastar** (Ctrl + arrastar; ímã de 16 px na vaga automática; volta sozinho
      ao automático se os apps crescerem até ele; menu "Voltar à posição automática") — v0.8.0 (2026-10-31)
- [x] **Painel rápido do clique** (Quick Flyout do Modelo 3): Envio/Download com pico e barra,
      gráfico 60 s, link da placa, consumo do dia persistido, unidade, iniciar com o Windows, fixar — v0.2.0
- [x] Gráfico com **linhas de referência**: topo em valor redondo 1-2-5 (ex.: 100 KB/s, 50 Mbps) e
      metade pontilhada, ambas rotuladas, + legenda Envio/Download — v0.3.0 (pedido do dono)
- [x] **Pausar medição** no menu (só na sessão; ao retomar, linha de base nova) — v0.3.0
- [x] Vários monitores (opção "Mostrar em todos os monitores": um widget por barra secundária,
      posição manual guardada por monitor) — v0.8.0 (2026-10-31)
- [x] Suspender/hibernar: na retomada (`PBT_APMRESUMEAUTOMATIC`) o medidor recomeça e o widget reencaixa;
      troca de rede: contador que volta é descartado — v0.3.0

**Aceite:** cada opção muda o widget **na hora**, sem reiniciar; apagar/corromper o JSON não
derruba o app. → **0.3.0**

## Onda 3 — Escolha de cores (o "futuro" do pedido) ✅ (núcleo na v0.2.0; seletor visual na v0.9.0 · 2026-10-31)

- [x] Paleta personalizada (hex de upload e download); a variante da barra clara é **derivada** pelo
      motor WCAG em vez de escolhida à parte — v0.2.0
- [x] Prévia ao vivo sobre amostras da barra escura e clara — v0.2.0
- [x] **Selo de contraste** WCAG por barra; cores abaixo do mínimo são recalibradas sozinhas — v0.2.0
- [x] "Restaurar padrão" + 5 predefinições do design (Clássico, Neon, Monocromático, Alto contraste, Bio) — v0.2.0
- [x] Seletor visual de cor: quadrado saturação × brilho + barra de matiz, por mouse e teclado, editando
      envio ou download; o campo hexadecimal continua e os dois se acompanham — v0.9.0 (2026-10-31)

**Aceite:** trocar a cor e ver na barra imediatamente; cor salva sobrevive ao reinício. → **0.4.0**

## Onda 4 — Distribuição ✅ (2026-10-31 · v0.4.0)

- [x] Instalador **sem administrador** + atualização automática **silenciosa** com **Velopack**
      + GitHub Releases (repo público `helveciopereira/velocimetro-internet`, feed direto
      `releases/latest/download/`): consulta 45 s após abrir e a cada 5 min (v0.4.2; era 1 h), baixa só a
      diferença, reinicia sozinho num momento tranquilo (nada aberto, mouse fora, sem tela cheia) — v0.4.0
- [x] Ícone do app (v0.2.0); versão + estado da atualização no rodapé do estúdio — v0.4.0
- [x] Desinstalação limpa da entrada Run (só se aponta para a cópia desinstalada). Os dados do
      usuário (`%AppData%` / `%LocalAppData%\VelocimetroInternet`) ficam — pasta separada do packId — v0.4.0
- [ ] (Opcional) assinatura de código para evitar aviso do SmartScreen — ⛔ **bloqueado em 2026-10-31:**
      exige um certificado comprado pelo dono (OV/EV de uma autoridade certificadora, ou Azure Trusted
      Signing, ~US$ 10/mês) em nome dele. Com o certificado em mãos: `vpk pack --signParams` (signtool) ou
      `--azureTrustedSignFile`. Certificado autoassinado NÃO resolve (o SmartScreen continua avisando).

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

## Onda 5 — Informação extra sem ocupar espaço ✅ (2026-10-31 · v1.0.0)

Itens "alto valor, baixo esforço" do `04-IDEIAS.md`:

- [x] **Dica ao passar o mouse** no widget: interface + IP local, consumo de hoje e do mês (com % da
      franquia), picos dos últimos 60 s e os atalhos escondidos — v1.0.0
- [x] **Consumo do mês** (soma persistida por dia, ~13 meses em `consumo-diario.json`; o `consumo.json`
      antigo é migrado) — no painel ("Este mês") e no estúdio — v1.0.0
- [x] **Alerta de franquia**: campo "Franquia mensal (GB)" no estúdio; notificação UMA vez aos 80 % e UMA
      aos 100 % por mês (respeita o "não perturbe"); barra da franquia no painel — v1.0.0
- [x] **Histórico com exportação CSV** (no lugar do SQLite da ideia original: um JSON de ~400 linhas não
      justifica a dependência) — botão "Exportar CSV…" no estúdio, pt-BR (";" e vírgula decimal) — v1.0.0
- [x] **Cor por intensidade** (opcional, desligada por padrão): números mais fortes perto da velocidade
      do plano/link — v1.0.0
- [x] **Duplo clique abre o Gerenciador de Tarefas** (também no menu do clique direito) — v1.0.0
- [x] Indicador de sem conexão — já existia desde a v0.2.0 ("—" no widget, "Sem conexão" no painel)

## Onda 6 — Painel ampliado e latência ✅ (v1.1.0, 2026-10-31)

- [x] Gráfico do painel com **1 min / 5 min / 1 h** e média de cada janela (5 min = 1 ponto a cada 5 s;
      1 h = 1 ponto por minuto; pontos calculados pelos bytes reais; janela lembrada nas configurações)
- [x] **Latência (ping)** opcional — roteador e internet, desligada por padrão (o app continua sem
      acessar a internet a menos que o usuário ligue)

Fica para decisão do dono (não entra sem conversa): teste de velocidade sob demanda (precisa de
servidor externo), consumo por aplicativo (exige administrador/ETW) e relatório para a operadora.
→ Decidido em 2026-11-01: teste de velocidade, modos de exibição e consumo por app (Ondas 7 e 8).
→ Decidido em 2026-11-01: relatório para a operadora SEM teste automático (Onda 9); plano do dono: 700 Mbps.

## Onda 7 — Modos de exibição e teste de velocidade ✅ (v1.2.0, 2026-11-01)

- [x] **Modos de exibição** do widget: duas linhas (padrão), **uma linha** (`↑ 1,18 | ↓ 36,9 MB/s`, mais
      largo e baixo) e **só download** (uma pílula, ocupa o mesmo espaço) — estúdio › Aparência, com prévia;
      a vaga automática e o Ctrl + arrastar usam a largura do modo
- [x] **Teste de velocidade sob demanda** (Cloudflare `speed.cloudflare.com`): latência (mediana), download
      e upload com 4 conexões, 10 s por etapa, tetos de 100 MB / 30 MB, aquecimento descartado; resultado em
      Mbps comparado com o plano (faixas da Anatel). Botão no painel e no menu; nunca roda sozinho
- [x] v1.2.1: correção — o download dava 0 (servidor recusava pedidos de 12,5 MB); pedidos de até 8 MB,
      nova tentativa com metade do tamanho e erro visível em vez de "0 Mbps"
- [x] v1.2.2: download do teste 100 → 150 MB (+50 %) e pedidos de 25 MB (cai para 8 MB se o servidor limitar)

## Onda 8 — Consumo por aplicativo ✅ (v1.3.0, 2026-11-01)

- [x] "Quem está usando minha banda?" — janela com os 10 apps que mais trafegaram (5 min, 1 h, hoje, este
      mês), ícone, barra proporcional, ↓/↑ e "Outros". Abre pelo painel ("Ver por aplicativo") e pelo menu
- [x] **Sem administrador**: em vez de ETW, usa a contabilidade de uso de dados do próprio Windows
      (`ConnectionProfile.GetAttributedNetworkUsageAsync`, a mesma de Configurações › Rede › Uso de dados).
      Custo: ~1 min de atraso nos números e +~25 MB no disco (projeção WinRT do SDK do Windows)

## Onda 9 — Relatório para a operadora ✅ (v1.4.0, 2026-11-01)

- [x] Decisão do dono: **nunca testar sozinho**. O relatório junta os testes que o usuário fez (cada teste
      concluído é guardado, com cabo/Wi-Fi) e as quedas de internet lidas do Windows (Network List Manager:
      o mesmo "Sem internet" do ícone de rede — zero tráfego extra, ~0,5 ms por leitura a cada 5 s)
- [x] Quedas separadas por causa: **sem internet com o roteador conectado** (conta para a operadora) e
      **sem rede local** (Wi-Fi desligado/cabo solto — fora da conta). Só ≥ 1 min; PC suspenso/pausado não
      conta como tempo observado (a queda aberta fecha no último instante visto)
- [x] Janela "Relatório para a operadora…" (menu): mês a mês, média × plano, testes abaixo de 40 %,
      quedas, disponibilidade e conclusão; **PDF A4** sem biblioteca (páginas desenhadas pelo WPF, imagem
      sem perda), com tabela de testes e de quedas, referência às metas da Anatel e "como reclamar"

## Varredura de 2026-11-01 — novas ondas candidatas (⬜, aguardam escolha do dono)

Achado que motivou a Onda 10, medido agora (`netsh wlan show interfaces`): este PC testa por
**Wi-Fi 5 GHz, sinal 66 % (−67 dBm), link de recepção 432 Mbps**, com plano de **700 Mbps**. Nenhum
teste por este Wi-Fi passa de ~62 % do plano, por melhor que esteja a operadora. Hoje o relatório
diz só "foi por Wi-Fi"; a operadora usa isso para descartar a reclamação.
→ Correção em 2026-11-02: no dia seguinte o mesmo Wi-Fi estava a **2.402 Mbps, sinal 80 %** (placa
Wi-Fi 7). A taxa do link VARIA muito; por isso a v1.5.0 a lê DURANTE cada teste, não uma vez só.

## Onda 10 — Wi-Fi explicado no teste e no relatório ✅ (v1.5.0, 2026-11-02)

- [x] Cada teste guarda a **velocidade do link** (cabo: a da porta; Wi-Fi: recepção e transmissão) e,
      no Wi-Fi, **banda, sinal e RSSI** (WLAN API do Windows, sem administrador). Lido logo depois da
      fase de download, com o link sob carga. Conferido contra o `netsh`: todos os campos iguais, 28 ms.
      Nesta máquina não precisou da permissão de localização; se o Windows negar, fica a velocidade da placa
- [x] Janela do teste: "Conexão: Wi-Fi 5 GHz · sinal 66 % · link 432 Mbps" e, se o link é menor que o
      plano, o aviso de que o limite é do Wi-Fi (ou do cabo/porta) e o que fazer
- [x] Relatório: média **por cabo × por Wi-Fi**, quantos testes o link não deixava chegar ao plano, e
      coluna "Link" (com `*`) na tabela do PDF
- [x] Testes da v1.4.0 (sem o campo) continuam lidos (JSON versão 1); para eles vale o aviso genérico
- [x] Conferido no app instalado em 2026-11-02 com um teste real: "Wi-Fi 5 GHz · sinal 84 % · link
      2,4 Gbps", gravado no `qualidade.json` e contado no relatório do mês

## Onda 11 — Anexos e aviso de queda ⬜ (candidata a v1.5.1 · valor médio, esforço baixo)

- [ ] **Exportar CSV** dos testes e das quedas do mês (anexo da reclamação; mesmo formato pt-BR do
      CSV de consumo da v1.0.0)
- [ ] **Aviso opcional** "A internet voltou após 12 min" quando uma queda ≥ 1 min termina —
      **desligado por padrão** (04-IDEIAS: notificação só como opção), sai pelo ícone da bandeja
- [ ] **Wi-Fi que NÃO limitou:** se todos os testes por Wi-Fi do mês tinham link acima do plano, trocar a
      frase genérica "a operadora pode atribuir a lentidão ao Wi-Fi" por "o link do Wi-Fi era de
      2,4 Gbps, acima do plano: ele não limitou estes testes" (achado no teste real de 2026-11-02)

## Onda 12 — Latência e perda no relatório ⬜ (candidata a v1.6.0 · valor médio, esforço médio)

- [ ] O ping do app (a cada 5 s, quando ligado) passa a ser resumido por hora: mediana, pior 5 % e
      % de respostas perdidas para a internet e para o roteador. Separa "internet lenta" de "Wi-Fi ruim"
- [ ] Relatório ganha a seção de latência (só com o ping ligado). Metas da Anatel para latência e
      perda **só entram se conferidas no regulamento vigente**; sem fonte, mostra só os números

## Onda 13 — Mini gráfico nos números do widget ⬜ (backlog antigo · valor médio, esforço médio)

- [ ] Sparkline dos últimos 60 s atrás dos números (04-IDEIAS, "Alto valor, esforço médio"),
      opcional, sem custo de CPU perceptível; passar pelo Impeccable (legível em barra clara e escura)

## Dívida técnica anotada (sem onda própria)

- `App.xaml.cs` tem 1.259 linhas (menu, franquia, relatório, atualização, loop): dividir em
  arquivos `partial` por assunto quando uma onda já for mexer nele, copiando a lógica literalmente.

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
