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

## Onda 11 — Servidor do teste: fast.com ✅ (v1.6.0, 2026-11-02 · pedido do dono)

- [x] O teste mede no servidor do **fast.com** (caches da Netflix) mais próximo, que a API do site
      lista; a Cloudflare vira **reserva** (API fora, token trocado e não achado no site, ou servidor
      sem resposta na latência, antes de gastar dados). O motivo da troca vai para o log
- [x] Medido nesta casa (Claro, São Luís, Wi-Fi): o fast.com escolhe um servidor **dentro da Claro em
      São Luís**; a Cloudflare atende do **Rio** (GIG). Latência ~7 ms × ~100–118 ms; download e
      upload iguais ou maiores (3 rodadas intercaladas, ver walkthrough)
- [x] Upload com tamanho declarado (o fast.com recusa envio em partes com 406) e teto garantido por
      reserva; teto do upload 30 → 60 MB (meio segundo de medição fazia o upload oscilar 31–184 Mbps)
- [x] Relatório diz o(s) servidor(es) de verdade dos testes do mês (antes: "speed.cloudflare.com" fixo)
- [x] v1.6.1 (pedido do dono): ~500 MB de download e ~500 MB de upload por teste, até 20 s por etapa
      (o teste vai a ~1 GB e ~40 s); upload de 3 rodadas no fast.com: 168–195 Mbps (antes 31–184)

## Onda 14 — Anexos e aviso de queda ✅ v1.8.1 (2026-11-03) (era a Onda 11 · valor médio, esforço baixo)

- [x] **Exportar CSV** dos testes e das quedas do mês (anexo da reclamação; mesmo formato pt-BR do
      CSV de consumo da v1.0.0) — botão "Exportar CSV…" na janela do relatório, um arquivo com dois blocos
- [x] **Aviso opcional** "A internet voltou após 12 min" quando uma queda ≥ 1 min termina —
      **desligado por padrão** (04-IDEIAS: notificação só como opção), sai pelo ícone da bandeja
      (estúdio › chave "Avisar quando a internet voltar"; sem a chave, só o log registra)
- [x] **Wi-Fi que NÃO limitou:** se todos os testes por Wi-Fi do mês tinham link acima do plano, trocar a
      frase genérica "a operadora pode atribuir a lentidão ao Wi-Fi" por "o link do Wi-Fi era de
      2,4 Gbps, acima do plano: ele não limitou estes testes" (achado no teste real de 2026-11-02)

## Onda 12 — Latência e perda no relatório ⬜ (candidata a v1.8.0 · valor médio, esforço médio)

- [ ] O ping do app (a cada 5 s, quando ligado) passa a ser resumido por hora: mediana, pior 5 % e
      % de respostas perdidas para a internet e para o roteador. Separa "internet lenta" de "Wi-Fi ruim"
- [ ] Relatório ganha a seção de latência (só com o ping ligado). Metas da Anatel para latência e
      perda **só entram se conferidas no regulamento vigente**; sem fonte, mostra só os números

## Onda 13 — Mini gráfico nos números do widget ✅ (v1.7.0, 2026-11-02 · pedido do dono)

- [x] Gráfico de área dos últimos 60 s atrás do número, dentro de cada pílula (↑ e ↓), nos layouts
      de duas linhas e "só download" (a linha única não tem altura). Opcional, **desligado por padrão**:
      Estúdio › Na barra de tarefas › "Mini gráfico"; a amostra do estúdio já mostra o efeito
- [x] Legível nas 5 paletas e nos dois temas: o gráfico é desenhado "por subtração" (apaga o véu da
      pílula ACIMA da curva), então o contraste do número nunca cai — sobe acima da curva. Pintar na
      cor do número foi medido e descartado (sobravam 1–3 % de opacidade; com 18 % nem o branco
      passava na barra escura: 3,74:1)
- [x] Custo: < 0,1 ms por atualização na thread da interface (600 atualizações medidas, com e sem
      o gráfico, dentro do ruído); sem timer nem animação próprios

## Varredura de 2026-11-03 — fluidez, estabilidade, utilidade e alcance (⬜, aguardam escolha do dono)

Pedido do dono: "melhore fluidez, estabilidade, responsividade; ideias visuais, de informação, algo
mais útil e de acesso amplo". Tudo abaixo tem evidência medida no app VIVO (PID 111848, aberto em
02/11 15:46) ou lida no código (arquivo:linha). Prioridade: 🔴 corrige algo real · 🟡 melhora
clara · 🟢 ideia.

### Problemas reais encontrados (entram antes de qualquer ideia nova)

- ✅ **v1.8.0 (2026-11-03)** — diagnóstico no `walkthrough.md` (heap gerenciado 6 MB; lixo com recurso nativo
  esperando gen2; consulta de atualização deixava handles, não memória); correção: `GuardaMemoria` (coleta
  completa a cada +16 MB, com antes/depois no log) + `BaixadorHttp` + vigia por evento. **Confirmar com 24 h
  de log** (linhas "Coleta completa"). 🔴 **Memória cresce sem parar.** `velocimetro.log` (ticks de 10 em 10 min): memória privada
  **73 MB → 230 MB em 18 h** (+5,5 MB/h, uniforme, madrugada inclusive) e um salto de +117 MB entre
  12:23 e 13:23 de 03/11 sem nada no log além de dois "tela cheia". Medido ao vivo: 339 MB privados
  e **2 769 handles** (um widget costuma ter 400–800). Em 90 s de observação não cresceu: é lento.
  Suspeitos, pelo que roda em ciclo: a busca UIA na barra a cada 1,5 s (`TaskbarHost.cs:280-284`,
  `MedirLimitesBarraPorUia` 602-693 → 57 600 `FindAll` por dia), a consulta de atualização a cada
  **5 min** (`App.xaml.cs:84`, 288 pedidos/dia ao GitHub) e o `Shell_NotifyIcon` da dica a cada
  tick (`IconeBandeja.cs:58-69`). Método: `dotnet-gcdump` em dois momentos (heap gerenciado) +
  contagem de handles por tipo; depois cortar o culpado e provar com 24 h de log. Candidata a
  **v1.8.0**, esforço médio.
- ✅ **v1.7.2 (2026-11-03)** — 🔴 **Sem rede de proteção contra exceção não tratada.** Não existe `DispatcherUnhandledException`,
  `AppDomain.UnhandledException` nem `TaskScheduler.UnobservedTaskException` (grep em `src/`, 0
  resultados). Um clique num botão que lance exceção (ex.: `JanelaTesteVelocidade.xaml.cs:43`,
  `async void`) fecha o app sem deixar NENHUMA linha no log: para o usuário "o widget sumiu".
  Correção: registrar os três em `App.OnStartup`, gravar a pilha no log, manter o app vivo quando
  for erro de janela secundária, e só encerrar quando a falha for no widget. Esforço baixo.
- ✅ **v1.7.2 (2026-11-03)** — 🟡 **O loop de medição roda na thread da interface.** `ExecutarLoopMedicaoAsync` é chamado de
  `OnStartup` (`App.xaml.cs:217`) e nenhum `await` dele tem `ConfigureAwait(false)` (grep: 0 no
  arquivo): `GetIfTable2` (com um `PtrToStructure` de ~1,3 KB por interface, `NetworkSampler.cs:68-70`),
  o COM do NLM a cada 5 s, a soma da franquia a cada 30 s e o `File.AppendAllText` do log
  (`App.xaml.cs:1228`) executam na thread que desenha. Um disco ocupado ou uma tabela de interfaces
  grande congela o widget por aquele instante. O comentário de `HistoricoRede.cs:37` ("thread do
  pool") descreve o que se pretendia, não o que acontece. Correção: `Task.Run` para o loop, UI só
  no `InvokeAsync` que já existe. Esforço baixo; risco baixo (o `lock` do histórico já está lá).
- ✅ **v1.8.0 (2026-11-03)** — causa medida: busca UIA de 35 ms a cada 1,5 s; agora por evento + 10 s. 🟡 **1,7 % de um núcleo em repouso** (1 031 ms de CPU em 60 s, medido com `TotalProcessorTime`).
  O desenho do widget custa < 0,1 ms por atualização (harness `grafico-cpu`, v1.7.0); o resto é
  candidato: UIA a cada 1,5 s e `SHQueryUserNotificationState`. Meta razoável: < 0,3 %. Método:
  `dotnet-trace` por 60 s, ou build de teste com o vigia a 5 s para comparar. Junto com o item da
  memória (mesma causa provável).
- ✅ **v1.7.2 (2026-11-03)** — 🟡 **Consulta de atualização a cada 5 min** (`App.xaml.cs:84`): a skill do Velopack recomenda
  1 h + 30 s após acordar do suspender. 288 pedidos/dia por PC é ruído de rede num app que se
  vende como silencioso. Esforço mínimo. (Feito: 1 h; o 30 s após acordar já existia.)

### Fluidez e responsividade (depois dos itens acima)

- ✅ **v1.8.0 (2026-11-03)** — feito com SHOW/HIDE/DESTROY de janela de topo do sistema inteiro (o
  Explorer não emite WinEvent por botão da barra XAML) + primeiro plano; 300 ms + confirmação 1,5 s; segurança 10 s.
  🟡 **Vigia da barra por evento, não por relógio.** Hoje é `DispatcherTimer` de 1,5 s que refaz a
  busca UIA mesmo com a barra parada. `SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE / REORDER)` no
  processo do Explorer, com um "debounce" de 300 ms e um relógio de segurança de 10 s, reage mais
  rápido quando um app abre na barra E gasta ~0 quando nada muda. Esforço médio (o hook de
  primeiro plano já existe em `TaskbarHost`).
- 🟢 **Transição suave dos números** (interpolar o valor mostrado em ~250 ms entre ticks, só quando
  `SystemParameters.ClientAreaAnimation` estiver ligado): os números deixam de "pular". Custo:
  redesenhar ~15 vezes por tick em vez de 1 — medir com o `grafico-cpu` antes de aceitar.

### Estabilidade e dados

- 🟡 **Teste de fumaça automatizado do .exe** no fluxo de validação: abrir, achar o HWND, fotografar
  a barra, fechar (skill `provar-app-desktop-wpf-por-uia`). Hoje é passo manual (item 3 da
  "Validação padrão") e por isso é pulado quando a mudança parece pequena.
- 🟢 **Botão "Enviar diagnóstico"** no estúdio: zipa `velocimetro.log`, `settings.json` (sem nada
  sensível) e a versão, e abre a pasta — quem for pedir ajuda manda um arquivo em vez de descrever.

### Informação (o que o widget poderia dizer e ainda não diz)

- ✅ v1.8.1 (2026-11-03) **"Sem internet" é diferente de "sem rede" — e o widget não distinguia.** O loop já calcula
  `EstadoInternet.SemInternet` (placa conectada, Windows diz sem internet) para o relatório
  (`App.xaml.cs:1140-1143`), mas o widget só recebe `MostrarSemConexao()` quando NÃO há placa
  (`App.xaml.cs:1097`; `TaskbarWidget.xaml.cs`: 0 referências a `SemInternet`). Resultado: a
  internet cai, o Wi-Fi fica ligado, e o widget mostra "↓ 0 B/s" como se fosse só silêncio. Mostrar
  "sem internet" (número apagado + ⚠, cor de alerta da v1.7.1) é a informação mais útil que falta.
  Esforço baixo; combina com a Onda 14 (aviso de queda).
- 🟢 **GPU %** ao lado de CPU/RAM, via contadores de desempenho "GPU Engine" (sem administrador;
  medir custo, porque PDH é pesado) — opcional e desligado por padrão.
- 🟢 **Quedas de hoje no painel rápido** (quantas e a mais longa), lidas do `RegistroQualidade` que
  já existe; hoje só aparecem no relatório PDF.
- 🟢 **Latência no widget** como terceira pílula opcional (ms ao roteador), para quem joga/chama.

### Visual

- 🟢 **Modo "só setas com intensidade"**: no modo uma linha, trocar o número por barras de 3
  segmentos (como o sinal de celular) para barras muito cheias — menor que "só download".
- 🟢 **Cor da pílula pelo estado** no painel (sem internet = cinza; alerta de franquia = âmbar),
  reaproveitando o motor de contraste. Nada muda no padrão.

### Acesso amplo (outras pessoas instalando)

- 🟡 **Assinatura do instalador.** Setup sem assinatura = tela azul do SmartScreen ("impediu o
  início de um aplicativo não reconhecido") para todo mundo que baixar; é o maior obstáculo real
  para "acesso amplo". Caminhos: Azure Trusted Signing (assinatura por mês, pessoa física
  precisa de validação), SignPath (gratuito para código aberto no GitHub) ou publicar no
  **winget** (`winget install StatusMonitor` não passa pelo SmartScreen e o manifesto é um PR
  no repositório `microsoft/winget-pkgs`). O winget é o mais barato: esforço baixo, sem custo.
- 🟡 **Inglês.** Todos os textos estão fixos em PT-BR no XAML e no C# (≈120 `Text="…"` nos .xaml e
  ≈100 frases em `App.xaml.cs`/estúdio/relatório; grep "Idioma": 0). Um dicionário de recursos
  (`.resx` pt-BR/en-US, idioma do Windows com opção de trocar no estúdio) abre o app para quem não
  lê português. Esforço médio-alto (muitos textos), risco baixo. O relatório PDF fica em PT-BR
  (é para operadora brasileira).
- 🟢 **Atalho de teclado global** para abrir o painel (ex.: Ctrl+Alt+S; conferir se está livre com
  `RegisterHotKey` antes, erro 1409 = ocupado) — quem usa teclado ou leitor de tela não precisa achar
  o widget com o mouse. Os `AutomationProperties.Name` já estão nos controles.
- 🟢 **Pacote leve** (framework-dependent, ~3 MB) ao lado do Setup de 78 MB, para quem já tem o
  .NET 10 Desktop Runtime; o Setup atual continua sendo o padrão.

### O que JÁ está bom — não mexer

- Consumo do dia gravado a cada 60 s com arquivo temporário + `Move` atômico (`HistoricoRede.cs`
  44, 290-291); quedas gravadas ao fechar cada queda e a cada 5 min (`RegistroQualidade.cs:218,257`).
- Instância única com mensagem direta à janela (não `HWND_BROADCAST`), `App.xaml.cs:104-113, 234-246`.
- Medição sem administrador, sem telemetria, sem rede além do teste sob demanda e da atualização.
- Motor de contraste WCAG para todas as cores, inclusive o alerta da v1.7.1 e o gráfico da v1.7.0.
- Busca UIA com `CacheRequest` (uma ida ao Explorer por ciclo, não uma por propriedade).
- Rodízio do log em 1 MB, `GC`+`EmptyWorkingSet` após fechar janelas pesadas (decisão medida).

### Ordem sugerida

1. ✅ **v1.7.2** (2026-11-03) — exceções não tratadas + loop fora da UI + atualização de hora em hora (três itens de esforço baixo).
2. ✅ **v1.8.0** (2026-11-03) — diagnóstico e correção do vazamento de memória/CPU + vigia por evento (mesma área). Pendente: confirmar com 24 h de log.
3. ✅ **v1.8.1** (2026-11-03) — "sem internet" no widget + Onda 14 (aviso de queda e anexos).
4. **v1.9.0** — winget + inglês (acesso amplo).
5. Ideias 🟢 conforme o gosto do dono.

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
