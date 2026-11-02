# Walkthrough — Status Monitor, ex-Velocímetro de Internet (Windows 11 · C# WPF .NET 10)

## v1.5.0 — 2026-11-02 — Onda 10: Wi-Fi explicado no teste e no relatório

### O que mudou
- Cada teste de velocidade guarda **como o PC estava ligado ao roteador**: cabo ou Wi-Fi, velocidade do
  link e, no Wi-Fi, banda (2,4/5/6 GHz), sinal (%) e RSSI (dBm).
- Janela do teste: linha "Conexão: Wi-Fi 5 GHz · sinal 66 % · link 432 Mbps". Se o link é menor que o
  plano, avisa que **nenhum teste por este Wi-Fi passa dele** e manda testar por cabo. No cabo abaixo
  do plano (ex.: 100 Mbps), sugere conferir o cabo e a porta do roteador.
- Relatório (janela e PDF): frase com a **média por cabo × por Wi-Fi** e quantos testes o link não
  deixava chegar ao plano; o PDF ganha a coluna **"Link"** (com `*` e nota quando limita) e a banda do
  Wi-Fi na coluna "Conexão".

### Por quê (causa raiz)
- A varredura de 2026-11-01 achou o PC do dono testando por Wi-Fi com link de 432 Mbps num plano de
  700 Mbps: o teste não podia passar de ~62 % do plano, e o relatório só dizia "foi por Wi-Fi" — a
  operadora descartaria a reclamação, e o usuário culparia a operadora pelo limite do próprio Wi-Fi.
- No dia seguinte o mesmo Wi-Fi estava a 2.402 Mbps: a taxa varia muito. Por isso é lida DURANTE cada
  teste (logo depois do download, com o link sob carga — parado, o Wi-Fi economiza energia e mostra menos).
- Regra conservadora: só conta como "limitado pelo link" quando a taxa do link é MENOR que o plano. Taxa
  de link é teto teórico (o real fica abaixo), mas o app não desculpa a operadora sem certeza.

### Como usar
Nada a configurar: faça um teste (menu › "Testar velocidade da internet…") e veja a linha "Conexão".
No relatório, a conclusão e a coluna "Link" mostram quais testes valem para a reclamação.

### Arquivos
- Novos: `Medicao/ConexaoNoTeste.cs` (dados e textos, puro), `Sistema/InfoWiFi.cs` (WLAN API, lida por
  deslocamento), `tests/.../V150ConexaoTests.cs` (+18 testes).
- Alterados: `Medicao/RegistroQualidade.cs` (`TesteGuardado.Rede`, opcional), `Medicao/CalculoRelatorio.cs`
  (médias por tipo, limitados, frases), `Sistema/InfoRede.cs` (`ConexaoAtual`), `Sistema/NativeMethods.cs`
  (assinaturas da wlanapi), `Apresentacao/JanelaTesteVelocidade.xaml(.cs)`, `Apresentacao/RelatorioPaginas.cs`,
  `App.xaml.cs` (liga a leitura ao teste).

### Validação
- Build Release 0 avisos; **252/252 testes** (inclui o JSON exato da v1.4.0 sendo lido).
- Leitura real nesta máquina pela classe do app × `netsh wlan show interfaces`: 2.402/2.402 Mbps, sinal
  80 %, −59 dBm, 5 GHz — iguais; 28 ms; sem pedir a permissão de localização.
- Render fora da tela: janela do teste (Wi-Fi limitado, cabo 1 Gbps, cabo 100 Mbps), janela do
  relatório e PDF de 14 e 25 testes (MuPDF: A4, 2 folhas, coluna "Link" sem corte depois de alargada).
- Teste REAL pelo app instalado (1.5.0), feito em 2026-11-02 às 09:33, sem mouse: a 2ª cópia do .exe
  abriu o painel, a UI Automation acionou "Testar velocidade" e "Iniciar teste", e o menu foi aberto
  pela mensagem da bandeja (clique direito) para chegar ao "Relatório para a operadora…".
  - Resultado: ↓ 180 Mbps · ↑ 37,8 Mbps · 102 ms · 172 MB gastos, em 16 s.
  - Linha mostrada: "Conexão: Wi-Fi 5 GHz · sinal 84 % · link 2,4 Gbps." Sem aviso de limite, o que está
    certo: o link estava acima do plano de 700 Mbps.
  - Gravado em `qualidade.json` com `Rede` (2402/2402 Mbps, 84 %, −64 dBm, 5 GHz). O log tem a mesma
    descrição. O relatório de novembro mostra 2 testes, média de 176 Mbps (25 % do plano), 0 testes
    limitados e o aviso genérico de Wi-Fi.
  - Leitura do resultado: com link de 2,4 Gbps, o Wi-Fi não era o gargalo dos 180 Mbps. Com latência
    de ~100 ms nos dois testes do dia, um teste por cabo é o próximo passo para confirmar a operadora.
- Fora do escopo, anotado no plano (Onda 11): quando o link de TODOS os testes por Wi-Fi está acima do
  plano, o relatório ainda usa a frase genérica "a operadora pode atribuir a lentidão ao Wi-Fi". Podia
  dizer que o link medido estava acima do plano, que é justamente o argumento contra essa desculpa.
- Fora do escopo, anotado: com o teste por cabo, a frase de comparação da v1.2.0 ainda diz "Repita o
  teste por cabo para confirmar" quando o resultado fica abaixo de 40 %.

## v1.4.0 — 2026-11-01 — Onda 9: relatório para a operadora

### O que mudou
- **Quedas de internet registradas sozinhas**: a cada 5 s o app lê do Windows se há internet (o mesmo
  "Sem internet" do ícone de rede). Placa conectada + Windows sem internet por **1 min ou mais** = queda
  (conta para a operadora); nenhuma placa conectada = "sem rede local" (fora da conta).
- **Cada teste de velocidade concluído é guardado** (data, ↓, ↑, latência, cabo ou Wi-Fi). A janela do
  teste avisa: "guardado no relatório do mês".
- Novo item de menu **"Relatório para a operadora…"**: escolhe o mês, mostra média × plano, testes
  abaixo de 40 %, quedas, internet disponível e a conclusão; **"Gerar PDF…"** salva (Documentos, por
  padrão) e abre o PDF A4 com as tabelas de testes e de quedas, a referência às metas da Anatel e como
  reclamar (operadora → protocolo → Anatel 1331).

### Por quê / decisões
- Último item em aberto do plano; o dono escolheu "relatório sem teste sozinho" — testar em segundo
  plano gastaria a banda e a franquia (ideia já descartada no 04-IDEIAS).
- Fonte das quedas: Network List Manager por COM (`GetConnectivity`), não a API WinRT — o WinRT
  carregaria a projeção do SDK (~25 MB) no widget que fica aberto o dia todo. Medido: ~0,5 ms por
  leitura, funciona na thread do loop (MTA). Na dúvida (COM indisponível) conta como online: nunca
  inventa queda.
- Tempo observado só conta com o app medindo (passos ≤ 30 s). PC suspenso/pausado = buraco: a queda
  aberta fecha no último instante visto, para o relatório não acusar horas que ninguém mediu. A
  disponibilidade usa só o tempo com o roteador conectado.
- PDF sem biblioteca: páginas desenhadas pelo WPF (mesma fonte e acentos), gravadas como imagem sem
  perda (Flate) — texto nítido, ~400 KB por relatório. Folha branca fixa (é papel; não segue o tema
  escuro) e situação de cada teste escrita por extenso (impressão em P&B não perde a informação).
- Percentuais arredondados para BAIXO (79,9 % nunca vira "80 %") e com espaço inseparável antes do %.
- Dados em `%LOCALAPPDATA%\VelocimetroInternet\qualidade.json` (~13 meses), fora da pasta do instalador.

### Arquivos
- Novos: `Medicao/RegistroQualidade.cs` (registro puro + `ServicoQualidade` com disco),
  `Medicao/CalculoRelatorio.cs` (resumo do mês, metas da Anatel, textos), `Sistema/ConectividadeWindows.cs`
  (NLM), `Apresentacao/PdfDeImagens.cs`, `Apresentacao/RelatorioPaginas.cs`, `Apresentacao/JanelaRelatorio.xaml(.cs)`,
  `tests/.../V140RelatorioTests.cs` (+26 testes).
- Alterados: `App.xaml.cs` (observação a cada 5 s no loop, guardar teste, menu, momento tranquilo,
  encerramento), `Apresentacao/JanelaTesteVelocidade.xaml.cs` (callback ao concluir), `Sistema/InfoRede.cs`
  (`TipoConexao`: cabo/Wi-Fi).

### Validação
- Build Release 0 avisos; **234/234 testes** (inclui a estrutura do PDF: cada entrada da xref aponta
  para o objeto certo).
- NLM real nesta máquina: "tem internet" = verdadeiro, na thread principal e em threads do pool.
- Render com dados sintéticos (14 e 25 testes, 3 quedas, 1 período sem rede): janela e PDF conferidos
  por imagem; PDF aberto por leitor independente (MuPDF): A4, 2 folhas, título com acento, cabeçalho
  da tabela repetido na folha 2. Geração: ~100 ms, 380–500 KB.
- ⚠️ Não automatizado: queda REAL (exige tirar a internet do roteador por > 1 min).

## v1.3.0 — 2026-11-01 — Onda 8: consumo por aplicativo

### O que mudou
- Nova janela **"Consumo por aplicativo"**: os programas que mais baixaram/enviaram em **5 min, 1 h, hoje
  ou este mês**, com ícone do .exe, barra proporcional ao maior, ↓ baixado e ↑ enviado, total do período e
  linha "Outros (N apps)" a partir do 10º. Refaz a leitura a cada 30 s enquanto aberta.
- Abre pelo link **"Ver por aplicativo"** no cartão de consumo do painel rápido e pelo item
  **"Consumo por aplicativo…"** do menu do clique direito.

### Por quê / decisões
- Pedido do dono (Onda 8 escolhida em 2026-11-01). O plano previa ETW (rastreio do kernel), que exige
  **administrador/UAC** — descartado. O Windows já contabiliza bytes por app em cada perfil de rede
  (`ConnectionProfile.GetAttributedNetworkUsageAsync`, os mesmos números de Configurações › Rede › Uso de
  dados): funciona em app não empacotado e sem elevação. Custo: **~1 min de atraso** (dito na janela).
- O Windows guarda um perfil **por rede Wi-Fi já usada** (30 nesta máquina) e cada consulta, mesmo vazia,
  custa 90–180 ms e é serializada pelo sistema (ler em paralelo deu os mesmos ~4,8 s). Por isso a leitura
  tem duas fases: a 1ª consulta só a rede atual (~0,2 s); em segundo plano descobre (uma vez, ~3 s) quais
  redes têm dados no mês, e as leituras seguintes consultam só essas, somando o mesmo app entre redes.
- Perfis de VPN devolvem 0 e são ignorados (mesmo filtro de placas do medidor, `InterfaceFilter`).
- O Windows identifica o app por caminho de dispositivo (`\device\harddiskvolume4\...`); vira `C:\...`
  por `QueryDosDevice` para achar o ícone e a descrição do arquivo (nome amigável, ex.: "Microsoft Edge").
- Exige o TFM `net10.0-windows10.0.19041.0` (projeção WinRT): a instalação cresce ~25 MB uma vez.
- Sem rolagem (a barra de rolagem padrão do WPF é clara e destoa do tema): no máximo 10 linhas.

### Arquivos
- Novos: `Medicao/ConsumoPorApp.cs` (contas puras + `LeitorConsumoPorApp`), `Apresentacao/IconesApps.cs`,
  `Apresentacao/JanelaConsumoApps.xaml(.cs)`, `tests/.../V130ConsumoPorAppTests.cs` (+17 testes).
- Alterados: `Sistema/NativeMethods.cs` (`QueryDosDevice`, `SHGetFileInfo`), `App.xaml.cs` (abrir a janela,
  menu, momento tranquilo da atualização), `Apresentacao/PainelRapido.xaml(.cs)` (link), os dois `.csproj` (TFM).

### Validação
- Build Release 0 avisos; **208/208 testes**.
- Render fora da tela com dados REAIS desta máquina: 1ª leitura 46 apps em 197 ms; descoberta 3,1 s;
  leituras seguintes 157–185 ms; janela com 10 linhas + "Outros (37 apps)", ícones corretos, 611 px de
  altura (cabe numa tela 1080p a 150 %).

## v1.2.2 — 2026-11-01 — Teste de velocidade: download maior (+50 %)

### O que mudou
- Teto do download do teste: **100 → 150 MB** (+50 %, pedido do dono). Aviso da janela: "até ~180 MB".
- Pedidos de download com tamanhos da lista aceita pelo servidor: **25 MB** (o do teste da Cloudflare no
  navegador); se recusar, **8 MB**; depois 1 MB. Nunca um tamanho calculado.

### Por quê
- Relato do dono: "a velocidade não está chegando no máximo da rede"; pediu +50 % no arquivo de teste.
- Medido nesta rede: o teste chegou a ~530 Mbps e **batia o teto de 100 MB em ~1,5 s**. Com 1 s de
  aquecimento descartado sobrava ~0,5 s de medição, com o TCP ainda acelerando.
- Pedidos de 12 MB (+50 % sobre os 8 MB por pedido da v1.2.1) **não existem** para o servidor: medição
  com 3 repetições por tamanho deu ≤ 10,5 MB → 200; **11 a 16,7 MB → 403 sempre**; 20 a 30 MB → 200.
- Depois de uso intenso o servidor responde **429 a pedidos de ≥ 10 MB por 15+ minutos** (8 MB ou menos
  continuam passando) — por isso a queda vai direto de 25 para 8 MB, sem o degrau de 10 MB.

### Arquivos
- `Medicao/TesteVelocidade.cs` (`TamanhosDownloadAceitos`, `CalculoTeste.PedidoDownload` /
  `PedidoDownloadAposRecusa`, teto de 150 MB), `Apresentacao/JanelaTesteVelocidade.xaml` (aviso de dados),
  `tests/.../V120ModosETesteTests.cs` (+2 testes; o servidor falso imita a faixa recusada real).

### Validação
- Build Release 0 avisos; **191/191 testes**.
- Teste real (limites padrão): com 100 MB → ↓ 529,8 Mbps (teto atingido); com 150 MB → ↓ 492,5 Mbps
  (150 MB recebidos), ↑ 157,9 Mbps. As duas rodadas usaram pedidos de 8 MB (os de 25 MB estavam em 429
  pelas medições desta sessão) — a diferença entre elas é variação normal da rede, não ganho medido.

## v1.2.1 — 2026-11-01 — Correção: o teste de velocidade não media o download

### O que mudou
- O teste agora mede o download. Cada pedido tem no máximo **8 MB**; se o servidor recusar mesmo assim, a
  conexão tenta de novo com a **metade** do tamanho (até 1 MB). Etapa que não transfere **nenhum** byte vira
  mensagem de erro na janela ("o servidor de teste recusou…"), nunca mais um "0,0 Mbps" falso.

### Por quê (causa raiz)
- Relato do dono: "só mediu latência e upload". O log mostrava `↓ 0,0 Mbps · ↑ 29,9 Mbps`.
- Cada pedido de download pedia 1/8 do teto de 100 MB = **12,5 MB**, e o speed.cloudflare.com responde
  **403** a alguns tamanhos acima de ~10 MB, de forma irregular (medido com curl: 11, 12, 12,5, 15 e 16 MB →
  403; 20, 25 e 30 MB → 200; até 10 MB → sempre 200).
- O 403 virava exceção dentro da tarefa de cada conexão, e ninguém aguardava essas tarefas com `await`:
  o erro sumia e a etapa terminava com 0 bytes.
- A validação da v1.2.0 usou tetos reduzidos (20 MB → pedidos de 2,5 MB) e por isso não pegou o defeito.

### Arquivos
- `Medicao/TesteVelocidade.cs` (`TamanhoMaximoPedido`, `CalculoTeste.TamanhoDoPedido/PedidoAposRecusa`,
  nova tentativa por conexão, `await` das conexões, erro quando a etapa zera).
- `tests/.../V120ModosETesteTests.cs`: +3 testes, entre eles um servidor falso em memória que recusa com 403
  acima de 10 MB (reproduz o defeito) e outro que recusa tudo (tem de dar erro, não 0).

### Validação
- Build Release 0 avisos; **189/189 testes**.
- Teste real com os limites PADRÃO do app: ↓ 13,2 Mbps (15,4 MB recebidos), ↑ 12,0 Mbps, 80 relatos de
  progresso. A rede estava lenta na hora: o curl baixou 8 MB a 13–30 Mbps no mesmo minuto.

## v1.2.0 — 2026-11-01 — Onda 7: modos de exibição e teste de velocidade

### O que mudou
- **Modos de exibição** (estúdio › Aparência › "Modo de exibição", com prévia de cada um):
  - **Duas linhas** — o de sempre;
  - **Uma linha** — `CPU 12% · RAM 48% | ↑ 1,18 | ↓ 36,9 MB/s`, o formato compacto como preferido;
  - **Só download** — uma pílula centralizada, com a unidade (`↓ 36,9 MB/s`).
  A vaga automática, a compactação por falta de espaço e o Ctrl + arrastar usam a largura do modo.
- **Teste de velocidade sob demanda**: botão "Testar velocidade" no rodapé do painel e item "Testar
  velocidade da internet…" no menu do clique direito. A janela avisa antes o que vai acontecer (servidor da
  Cloudflare, ~25 s, até ~130 MB). Mede latência (mediana de 7 pings HTTP), download e upload (4 conexões,
  10 s cada, tetos de 100 MB / 30 MB), em **Mbps**. No fim compara com a velocidade contratada: ≥ 80 %
  "dentro do esperado", 40–80 % "acima do mínimo instantâneo da Anatel", < 40 % "ABAIXO do mínimo".
  O botão vira "Cancelar" durante o teste; fechar a janela cancela. Resultado vai para o log.

### Por quê
- Pedido do dono (2026-11-01). Uma linha só cabe melhor em barra pequena; "só download" é o número que a
  maioria olha. Os modos reaproveitam o estado compacto que já existia (sem layout novo para manter).
- O medidor mostra o que está passando, não o que o link aguenta; o teste responde "estou recebendo o que
  pago?". Fica sob demanda porque gasta banda e franquia — nunca roda em segundo plano. O 1º segundo de
  cada etapa é descartado (aceleração do TCP) para não subestimar; a latência usa mediana para que um pico
  isolado não distorça.

### Como usar
- Modo: clique direito › "Cores e mais configurações" › Aparência › Modo de exibição › Salvar.
- Teste: clique no widget › "Testar velocidade" › "Iniciar teste". Informe a velocidade contratada no
  estúdio para ver a comparação com o plano.

### Arquivos
- Novos: `Medicao/TesteVelocidade.cs` (`TesteVelocidade`, `CalculoTeste`), `Apresentacao/JanelaTesteVelocidade.xaml(.cs)`,
  `tests/.../V120ModosETesteTests.cs` (26 testes).
- Alterados: `TaskbarLayout.cs` (`ModoExibicaoWidget`, `ModosExibicao`, `Preferido`, largura do só download),
  `TaskbarHost.cs` (`ModoExibicao`), `TaskbarWidget.xaml.cs` (`DefinirModoExibicao`), `Configuracoes.cs`
  (`ModoExibicao`), `EstudioCores.xaml(.cs)` (grupo e prévias), `PainelRapido.xaml(.cs)` (botão do teste),
  `App.xaml.cs` (menu, janela do teste, modo aplicado nos widgets de todos os monitores; a janela do teste
  aberta adia a atualização automática).

### Validação
- Build Release 0 avisos; **186/186 testes**.
- Render fora da tela dos 3 modos (com e sem CPU/RAM) e da janela do teste.
- Teste real contra speed.cloudflare.com pela classe do app (tetos reduzidos, ~25 MB): latência 108 ms,
  ↓ 58,7 Mbps, ↑ 26,3 Mbps, 20 relatos de progresso.

## v1.1.0 — 2026-10-31 — Onda 6: painel ampliado e latência

### O que mudou
- **Gráfico do painel com três janelas**: seletor **1 min · 5 min · 1 h** acima do gráfico. 1 min é o
  gráfico de sempre; 5 min tem um ponto a cada 5 s; 1 h, um ponto por minuto. O canto da legenda mostra a
  **média da janela** e os cartões passam a dizer "Pico em 5 min / 1 h". A escolha fica gravada
  (`JanelaGrafico` no settings.json).
- **Latência opcional** (estúdio › Medição › "Medir latência", **desligada por padrão**): ping ICMP a cada
  5 s ao roteador (gateway da interface medida) e a 1.1.1.1, limite de 1 s. Linha "Latência" no painel e
  na dica do widget ("Roteador 3 ms · Internet 18 ms"; sem resposta = "—").

### Por quê
- 60 s mostram o instante, não a tendência ("o download ficou lento na última meia hora?"). Os pontos das
  janelas longas são **bytes do período ÷ segundos** — não a média das velocidades suavizadas, que
  esconderia rajadas e mudaria com a suavização escolhida. Tempo sem medição (pausa, sem rede) não vira
  zero falso: o gráfico só junta os períodos medidos.
- Internet "lenta" muitas vezes é latência, não banda; roteador baixo + internet alta aponta a operadora.
  Fica desligada porque é o único recurso que manda pacotes para fora do PC.

### Como usar
- Clique no widget → seletor acima do gráfico. As janelas longas enchem com o tempo (só memória: reabrir o
  app recomeça; "Juntando dados…" até haver 2 pontos).
- Latência: "Cores e mais configurações" › Medição › "Medir latência" › Salvar.

### Arquivos
- Novos: `Medicao/JanelaAgregada.cs` (anel de 60 baldes, `SerieGrafico`, `JanelasGrafico`),
  `Medicao/MedidorLatencia.cs`, `tests/.../V110JanelasELatenciaTests.cs` (23 testes).
- Alterados: `HistoricoRede.cs` (`CapturarJanela`), `PainelRapido.xaml(.cs)`, `DicaWidget.cs`,
  `EstudioCores.xaml(.cs)`, `Configuracoes.cs` (`MedirLatencia`, `JanelaGrafico`), `App.xaml.cs`,
  csproj de testes (exclui `backup\**`: o hook de backup criou cópias dos testes dentro da pasta e
  quebrou a compilação com CS0111).

### Validação
- Build Release 0 avisos; **160/160 testes**.
- Render fora da tela do painel nas três janelas, da dica e do estúdio (2 h sintéticas de tráfego).
- Ping real pela classe do app nesta máquina: "Roteador 12 ms · Internet 72 ms", 2ª rodada 5 s depois;
  desligar zera o estado.

## v1.0.0 — 2026-10-31 — Onda 5: informação extra sem ocupar espaço

### O que mudou
- **Dica ao parar o mouse no widget** (0,7 s): interface + IP local, consumo de hoje e do mês (com % da
  franquia), picos dos últimos 60 s e os atalhos escondidos. Montada só na hora de abrir.
- **Consumo do mês**: o consumo passa a ser guardado **por dia** (~13 meses, `consumo-diario.json`). Painel
  ganhou a linha "Este mês"; o estúdio mostra o total do mês ao lado da franquia.
- **Franquia mensal (GB)** no estúdio, cartão novo "Consumo e franquia": notificação do Windows **uma vez aos
  80 % e uma vez aos 100 %** do mês (envio + download, GB decimal como a operadora), respeitando o "não
  perturbe"; barra da franquia no painel (fica na cor de download acima de 80 %).
- **Exportar CSV…** do histórico por dia (pt-BR: `;` e vírgula decimal, UTF-8 com BOM para o Excel).
- **Cor por intensidade** (opcional, desligada): opacidade dos números de 0,5 (parado) a 1 (na velocidade do
  plano; sem plano, do link), em curva de raiz quadrada.
- **Duplo clique no widget → Gerenciador de Tarefas**; também no menu do clique direito.

### Por quê
Itens "alto valor, baixo esforço" do `04-IDEIAS.md`. Até a v0.9.0 o consumo só existia para HOJE e era
zerado na virada do dia (`consumo.json` guardava um único dia) — sem histórico não havia como ter mês,
franquia ou CSV. Por isso a base é o `RegistroConsumo` (puro, testável) e as três ideias vêm dele.

### Decisões
- **SQLite trocado por JSON**: ~400 linhas por ano não justificam uma dependência nativa.
- **Migração sem perda**: na leitura, o `consumo.json` antigo é sempre conferido e fica o **maior** valor de
  cada campo daquele dia (nunca soma). Cobre a 0.9.0 voltando a rodar antes de a atualização chegar.
- **Duplo clique contado à mão** (`GetDoubleClickTime` + `SM_CXDOUBLECLK`): o 1º clique abre o painel, que
  rouba o foco — o `ClickCount` do WPF não é confiável aí. O 1º clique continua instantâneo (esperar o tempo do
  duplo deixaria o clique comum lento); o 2º fecha o painel e abre o Gerenciador.
- **Aviso de franquia só pela bandeja**: sem ícone visível não há de onde a notificação sair → vai para o log, e
  o painel mostra o uso de qualquer forma. O "já avisei" fica gravado com o consumo (reiniciar não repete).
- Barra da franquia em **colunas proporcionais** (`usado*`/`resto*`): a 1ª versão usava a largura medida do
  trilho, que é 0 enquanto o bloco estava recolhido — a barra saía vazia na 1ª abertura (visto no render).

### Como usar
Estúdio › **Consumo e franquia**: digite a franquia em GB (vazio = sem franquia) e **Aplicar**; "Exportar CSV…"
salva o histórico. Estúdio › Medição › **Cor por intensidade**. Na barra: pare o mouse no widget para a dica;
duplo clique para o Gerenciador de Tarefas.

### Validação
- Build Release 0 avisos; **suíte inteira 137/137** (22 testes novos em `V100ConsumoEDicaTests`).
- Ao vivo no PC do dono (settings salvo e restaurado por hash, entrada Run conferida): com franquia de 4,5 GB e
  3,9 GB já usados no dia, o log registrou `Franquia 80 %: aviso mostrado.` no 1º tick; `consumo-diario.json`
  criado pela migração; cor por intensidade visível na captura da barra (download a 6 KB/s apagado).
- Dica, painel e estúdio **desenhados fora da tela** com o consumo real (o dono estava usando o PC — o mouse não
  foi movido): "Mês … (86 % da franquia)", barra a 86 % em vermelho, "Este mês até agora: 3,6 GB · 86 %".
- **Validação manual recomendada:** parar o mouse no widget (dica), duplo clique (Gerenciador abre, painel
  fecha), Exportar CSV e abrir no Excel.

### Arquivos tocados
`Medicao/RegistroConsumo.cs` (novo), `Medicao/HistoricoRede.cs`, `Apresentacao/DicaWidget.cs` (novo, inclui
`RegraDuploClique`), `Sistema/InfoRede.cs` (novo), `Sistema/IconeBandeja.cs`, `Sistema/NativeMethods.cs`,
`Apresentacao/TaskbarWidget.xaml.cs`, `Apresentacao/PainelRapido.xaml(.cs)`, `Apresentacao/EstudioCores.xaml(.cs)`,
`Config/Configuracoes.cs`, `App.xaml` (estilo de ToolTip no tema), `App.xaml.cs`, csproj (1.0.0),
`tests/.../V100ConsumoEDicaTests.cs` (novo), `03-PLANO-DE-ONDAS.md`, `04-IDEIAS.md`, `README.md`.

## v0.9.0 — 2026-10-31 — Seletor visual de cor (fecha a Onda 3)

### O que mudou
- Na paleta **Personalizada** do estúdio, abaixo dos campos hexadecimais: **"Ajustar no seletor"** com um
  segmento "↑ Envio / ↓ Download" (cada um com a amostra da cor atual), um **quadrado saturação × brilho** e
  uma **barra de matiz**. Mouse (clicar/arrastar) e teclado (quadrado: ←/→ saturação, ↑/↓ brilho; barra: ←/→
  matiz; Shift = passo maior).
- `CorHsv` (novo): conversão RGB ⇄ HSV pura e testável. `SeletorCor` (novo): controle montado em código.

### Por quê
Último item aberto da Onda 3 ("hoje é campo hexadecimal"): escolher cor digitando hex exige saber hex.

### Decisões
- **O campo hex continua sendo a fonte da verdade.** O seletor só escreve no campo do alvo; o `TextChanged`
  que já existia valida, atualiza o rascunho, a prévia e o "Alterações não aplicadas" — um caminho só.
  Atribuir `SeletorCor.Cor` por código não dispara `CorAlterada`, então não há laço campo ⇄ seletor.
- O seletor guarda o **matiz à parte**: ao arrastar até o branco/preto/cinza (onde o RGB não tem matiz) e
  voltar, a cor continua na mesma família, em vez de pular para o vermelho.
- Ida e volta RGB → HSV → RGB é **exata** (testado numa varredura do cubo RGB e em todas as cores das paletas):
  abrir o seletor nunca altera 1 bit de uma cor já salva.

### Onda 4 — assinatura de código: ⛔ bloqueada (não executável sem o dono)
Exige certificado comprado em nome do dono (OV/EV ou Azure Trusted Signing). Detalhes e o comando de
integração (`vpk pack --signParams` / `--azureTrustedSignFile`) registrados no `03-PLANO-DE-ONDAS.md`.

### Arquivos tocados
`Apresentacao/CorHsv.cs` (novo), `Apresentacao/SeletorCor.cs` (novo), `Apresentacao/EstudioCores.xaml(.cs)`,
`VelocimetroInternet.csproj` (0.9.0), `tests/.../Onda3SeletorCorTests.cs` (novo), `03-PLANO-DE-ONDAS.md`, `README.md`.

### Validação executada
- Build Release: 0 avisos, 0 erros. Testes: **115/115** (10 novos: cores conhecidas, ida e volta exata,
  limites/NaN, e o contrato do controle — atribuir não dispara evento, cinza guarda o matiz — em thread STA).
- Estúdio desenhado fora da tela (`RenderTargetBitmap`) com a paleta Personalizada: marcador em #FF6B6B
  (S 0,58 / V 1, matiz 0°). Teclas simuladas pelo próprio tratador: 10× ↓ no quadrado → `#CC5656` (V 0,8 —
  0xFF × 0,8 = 0xCC) e "Alterações não aplicadas"; 6× → na barra → `#CC9156` (matiz +30°); a prévia da barra,
  a amostra do segmento e o quadrado acompanharam; a cor de envio ficou intacta.
- **Validação manual recomendada:** estúdio › Personalizada › arrastar no quadrado e na barra com o mouse,
  trocar o alvo para "↓ Download", digitar um hex e ver o marcador pular; Aplicar e conferir a barra.

## v0.8.0 — 2026-10-31 — Itens adiados da Onda 2: intervalo, fonte/tamanho, ordem, Ctrl+arrastar, vários monitores

### O que mudou
- **Intervalo de atualização** 0,5 s / 1 s / 2 s / 5 s (estúdio › Medição). O laço de medição troca o
  `PeriodicTimer.Period` sem reiniciar; o histórico passou a ter **capacidade variável**
  (`HistoricoRede.CapacidadePara` = 60 s ÷ intervalo) e a gravar o consumo a cada **60 s somados**, não a cada
  60 registros. O gráfico do painel distribui os pontos pela capacidade, então continua mostrando 60 s.
- **Tamanho do texto** (Pequeno 9,5 / Normal 10,5 / Grande 11,5) e **fonte** (Segoe UI Variable, Segoe UI,
  Cascadia Mono) — novo cartão "Na barra de tarefas" do estúdio. O widget usa recursos dinâmicos `Widget.*`;
  a largura reservada na barra cresce pelo **fator medido** (`AparenciaWidget.FatorLargura`, que mede o pior
  caso "↓ 88,8 Mbps" com `FormattedText`). No padrão o fator é exatamente 1 — geometria idêntica à v0.7.0.
- **Ordem das linhas**: "↑ Envio" ou "↓ Download" em cima (também na faixa compacta de 1 linha).
- **Ctrl + arrastar** o widget pela barra: durante o arrasto ele passa por cima de tudo, limitado à barra;
  ao soltar, se cobriria botões é recusado e volta; perto da vaga automática (16 px lógicos) "gruda" nela
  (= automático). A posição é gravada em px lógicos desde a borda da barra, **por monitor**
  (`PosicoesManuais`: `principal` ou `monitor:X,Y`). Se depois os apps crescerem até ele, usa a vaga
  automática até liberar (registrado no log uma vez). Menu do clique direito: **"Voltar à posição automática"**.
- **Vários monitores**: opção "Mostrar em todos os monitores" cria um widget em cada `Shell_SecondaryTrayWnd`
  (`GerenciadorBarrasSecundarias`, conferência a cada 3 s: barra nova ganha widget, barra que sumiu perde).
  A medição da barra por UIA virou **uma só consulta** (`FindAll` com `OrCondition` + `CacheRequest`) e serve
  às duas: na secundária, os botões fixos (Iniciar etc.) e o relógio entram como zonas ocupadas.

### Por quê
Eram os três itens adiados da Onda 2 (plano `03-PLANO-DE-ONDAS.md`), pedidos agora pelo dono ("faça as ondas de
2 a 4"). O intervalo tinha sido adiado porque o histórico contava amostras (60 amostras = 60 s só a 1 s);
a causa raiz foi resolvida contando segundos.

### Como usar
Clique no widget › estúdio de cores › cartão **Na barra de tarefas** (tamanho, fonte, linha de cima, todos os
monitores) e **Medição › Atualizar a cada**. Para mudar o lugar: **segure Ctrl e arraste** o widget. Para
desfazer: clique direito › **Voltar à posição automática**.

### O que NÃO mudou de propósito
- Quem atualiza da 0.7.0 não vê nada diferente: os padrões novos (1 s, Normal, Segoe UI Variable, envio em
  cima, só a barra principal, sem posição manual) reproduzem o comportamento anterior.
- O estúdio, ao "Restaurar padrão" ou "Aplicar", **preserva** as posições manuais (podem mudar arrastando com
  o estúdio aberto; têm botão próprio no menu).

### Arquivos tocados
`Config/Configuracoes.cs`, `Apresentacao/AparenciaWidget.cs` (novo), `Apresentacao/GerenciadorBarrasSecundarias.cs` (novo),
`Apresentacao/TaskbarLayout.cs`, `Apresentacao/TaskbarHost.cs`, `Apresentacao/TaskbarWidget.xaml(.cs)`,
`Apresentacao/EstudioCores.xaml(.cs)`, `Apresentacao/PainelRapido.xaml.cs`, `Medicao/HistoricoRede.cs`, `App.xaml.cs`,
`VelocimetroInternet.csproj` (0.8.0), `tests/.../Onda2ExtrasTests.cs` (novo), `03-PLANO-DE-ONDAS.md`, `README.md`.

### Validação executada
- Build Release: 0 avisos, 0 erros. Testes: **105/105** (24 novos em `Onda2ExtrasTests`: padrões e normalização,
  cópia profunda, larguras com fator 1 = v0.7.0, posição manual válida/sobre botões/limitada/barra secundária,
  limite do arrasto, capacidade do histórico, troca de intervalo sem perder consumo, fator da aparência).
- No PC real (com `settings.json` do dono salvo e restaurado por hash, entrada Run conferida):
  - Grande + download em cima + todos os monitores: widget na barra principal (244×63 px a 150%) e na
    secundária (163×42 px a 100%), fotografados por `PrintWindow`; log com tick a cada 2 s.
  - Cascadia + pequeno + posições salvas: principal em X = 923 lógicos → 1384 px físicos (exato);
    secundária com X = 5 (em cima do Iniciar) recusada e na vaga automática, com a linha de log esperada.
  - Estúdio desenhado fora da tela (`RenderTargetBitmap`): segmentos, interruptor e amostra refletem o arquivo.
- **Validação manual recomendada** (a tela estava desligada e o Windows recusou mouse sintético — `SetCursorPos`
  devolveu falso): segurar Ctrl e arrastar o widget para a esquerda, soltar sobre um espaço livre (fica),
  soltar sobre os ícones (volta), soltar perto do lugar original (gruda); depois clique direito ›
  "Voltar à posição automática".

## v0.7.0 — 2026-10-31 — Executável e instalador com o nome novo (`StatusMonitor.exe`)

### O que mudou
- **Executável:** `VelocimetroInternet.exe` → **`StatusMonitor.exe`** (`<AssemblyName>StatusMonitor</AssemblyName>`
  no `.csproj`; o pacote Velopack usa `-e StatusMonitor.exe`).
- **Instalador e portátil na Release:** `VelocimetroInternetApp-win-Setup.exe` → **`StatusMonitor-Setup.exe`** e
  `…-Portable.zip` → **`StatusMonitor-Portable.zip`** (o `tools\publicar-atualizacao.ps1` renomeia depois do
  `vpk pack` e reescreve `assets.win.json`, que é a lista que o `vpk upload` envia).
- `tools\publicar.ps1` (cópia avulsa em `dist\`), testes e comentários com o nome novo; README com o link novo.

### Por quê
Pedido do dono: depois da troca de nome (v0.6.0), o arquivo também deve se chamar Status Monitor.
Na v0.6.0 o nome do .exe tinha sido mantido por cautela; o ensaio abaixo provou que a troca é segura.

### Como a migração acontece sozinha
O packId continua `VelocimetroInternetApp`, então a cópia 0.6.0 instalada recebe a 0.7.0 como atualização
normal: o Velopack troca a pasta `current\`, reabre pelo novo `StatusMonitor.exe` e regrava os atalhos
apontando para ele; ao abrir, o `InicioComWindows` reaponta a entrada Run (já existente) para o novo caminho.

### O que NÃO mudou de propósito
- packId (`VelocimetroInternetApp`): os pacotes `.nupkg`, a pasta de instalação `%LOCALAPPDATA%\VelocimetroInternetApp`
  e o `releases.win.json` mantêm o nome antigo — trocar seria outro app e as cópias instaladas parariam de atualizar.
- Pastas de dados, nome do valor Run, trava de instância única, namespace (mesmos motivos da v0.6.0).
- ⚠️ O link antigo `…/latest/download/VelocimetroInternetApp-win-Setup.exe` deixa de funcionar (404); use `StatusMonitor-Setup.exe`.

### Arquivos tocados
`src/VelocimetroInternet/VelocimetroInternet.csproj`, `src/VelocimetroInternet/Sistema/InicioComWindows.cs` (comentário),
`tools/publicar-atualizacao.ps1`, `tools/publicar.ps1`, `tests/VelocimetroInternet.Tests/Onda4Tests.cs`,
`README.md`, `03-PLANO-DE-ONDAS.md`, `walkthrough.md`.

### Validação
- Build 0 avisos / 0 erros; testes 81/81.
- **Ensaio local de ponta a ponta** (packId de teste `VelocimetroTeste`): 1.0.0 publicada com o nome antigo
  (`-p:AssemblyName=VelocimetroInternet`, `-e VelocimetroInternet.exe`) aberta em segundo plano; 1.0.1 com o nome novo
  aplicada sozinha em 117 s ("Package version 1.0.1 applied successfully"); processo reaberto =
  `current\StatusMonitor.exe`; `current\` só com `StatusMonitor.exe`; entrada Run reapontada para
  `…\current\StatusMonitor.exe --autostart`; atalho do menu Iniciar apontando para `StatusMonitor.exe`.
  Depois: cópia de teste desinstalada, entrada Run e `settings.json` do usuário restaurados (hash conferido).

## v0.6.0 — 2026-10-31 — Novo nome "Status Monitor" + pílula de internet mais estreita

### O que mudou
- **Nome:** o app passou a se chamar **Status Monitor** em tudo o que o usuário vê: cabeçalho e
  título do painel rápido, título do estúdio, dica do ícone da bandeja, nomes lidos por leitor de
  tela, nome mostrado pelo Windows (Gerenciador de Tarefas / aba Inicializar: `Product`,
  `AssemblyTitle`, `Description` no `.csproj`), atalhos e "Aplicativos instalados" (`--packTitle`
  do Velopack), cabeçalho do log, README.
- **Repositório:** `helveciopereira/velocimetro-internet` → **`helveciopereira/status-monitor`**
  (PATCH na API do GitHub, com descrição nova); remote local atualizado; feed do auto update e
  script de publicação apontam para o novo endereço.
- **Largura:** a pílula ↑/↓ passou de 96 para **78** lógicos (pedido: "entre o tamanho da CPU/RAM e o
  atual"). Widget: 129 px só internet / 225 px com CPU/RAM a 150% (antes 156 / 252).

### Por quê
Pedido do dono: o app deixou de ser só de internet (v0.5.0 trouxe CPU/RAM) e muda de nome,
mantendo o repositório (renomeado). A pílula de internet tinha sobra.

### O que NÃO mudou de propósito (e por quê)
- `packId` do Velopack (`VelocimetroInternetApp`) e, portanto, o nome do instalador
  `VelocimetroInternetApp-win-Setup.exe`: um packId novo é OUTRO app para o Velopack — as cópias
  instaladas não receberiam a atualização e cada PC precisaria reinstalar.
- Arquivo `VelocimetroInternet.exe`, pastas de dados (`%AppData%`/`%LocalAppData%\VelocimetroInternet`),
  valor da entrada Run, nome da trava de instância única e título técnico da janela do widget:
  trocar perderia as configurações e o consumo do dia, desligaria o "Iniciar com o Windows" ou
  permitiria duas cópias abertas na transição. O Windows já exibe "Status Monitor" pelo `Product`.
- Namespace/pastas do código e a pasta local do projeto.
- Histórico deste walkthrough (entradas antigas intocadas).

### Como a cópia 0.5.0 instalada acha a 0.6.0
Ela consulta o endereço ANTIGO; o GitHub responde 301 para `status-monitor` (conferido com curl,
inclusive com os parâmetros `?arch=…&localVersion=…` que o Velopack anexa) e o feed abre normal.

### Arquivos
`TaskbarLayout.cs` (86 = 8 + 78), `TaskbarWidget.xaml` (largura inicial, nome acessível),
`PainelRapido.xaml`, `EstudioCores.xaml`, `IconeBandeja.cs`, `App.xaml.cs`, `ServicoAtualizacao.cs`
(URL), `.csproj` (0.6.0 + Product/AssemblyTitle/Description), `tools\publicar-atualizacao.ps1`
(RepoUrl, Titulo), `Onda4Tests.cs` (URL), `Onda5Tests.cs` (limiares + teste da largura), README,
03-PLANO, walkthrough.

### Validação executada
- Pior texto da pílula medido com a fonte do widget (WPF FormattedText): "99,9 Mbps" ≈ 50 px →
  cabe em 78 (seta 5 + padding 12 + borda 1,5 + folga).
- Build Release 0 avisos; suíte inteira **81/81**.
- Fotos da barra real: com CPU/RAM 225 px, só internet 129 px, compacto 366 px encostado no '^';
  painel com o cabeçalho "Status Monitor". Configuração do usuário restaurada (hash conferido).

## v0.5.0 — 2026-10-31 — CPU e RAM (%) na barra, à esquerda da internet

## v0.5.0 — 2026-10-31 — CPU e RAM (%) na barra, à esquerda da internet

### O que mudou
- O widget ganhou uma coluna à esquerda do upload/download: **CPU** em cima e **RAM** embaixo,
  em porcentagem inteira (`CPU 5%` / `RAM 70%`), em pílulas de tom neutro (branco na barra
  escura, preto na clara), para não disputar com as cores da paleta.
- Modo compacto (barra cheia): `CPU 5% · RAM 70%  |  ↑ 1,2  |  ↓ 38,7 MB/s`.
- Liga/desliga: chave **"CPU e RAM na barra"** no painel rápido e **"Mostrar CPU e RAM"** no
  menu do clique direito. Padrão: ligado. Desligado, o widget fica exatamente como antes (156 px).
- Dica do ícone da bandeja ganha a linha `CPU x% · RAM y%`; o log registra CPU/RAM nos ticks.
- Correção de quebra: o argumento de diagnóstico `--compacto` alargava o widget sem mover o X,
  invadindo o '^' da bandeja; agora mantém a borda direita (só afeta testes).

### Por quê
Pedido do dono: "começou com a ideia da internet, mas quero agora colocar consumo de CPU e RAM,
tudo em porcentagem… à esquerda do upload ficaria CPU e abaixo da CPU ficaria a RAM", com opção
de mostrar ou não nas configurações.

### Como funciona
- **CPU:** `GetSystemTimes` a cada segundo; % = (Δkernel + Δuser − Δocioso) ÷ (Δkernel + Δuser)
  (o tempo de kernel do Windows já inclui o ocioso). Escolhido no lugar do `PerformanceCounter`,
  que carrega a PDH (dezenas de MB e ~1 s na 1ª leitura) num widget que mira ~50 MB.
  Pode ficar alguns pontos abaixo do Gerenciador de Tarefas em carga: o Win 11 mostra
  "% Utilidade" (considera turbo); aqui é o "% Tempo do processador" clássico.
- **RAM:** `GlobalMemoryStatusEx`; % = (total − disponível) ÷ total = "Em uso" do Gerenciador.
- **Barra:** largura lógica +64 (normal) / +112 (compacto); cresce para a esquerda, borda
  direita fixa no '^'. Com CPU/RAM o bloco compacta e oculta mais cedo quando a barra enche.

### Arquivos
`Medicao/MedidorSistema.cs` (novo), `Sistema/NativeMethods.cs` (GetSystemTimes,
GlobalMemoryStatusEx), `Config/Configuracoes.cs` (`MostrarCpuRam`), `Apresentacao/TaskbarWidget.xaml/.cs`,
`Apresentacao/TaskbarLayout.cs`, `Apresentacao/TaskbarHost.cs` (`MostrarSistema`, `--compacto`),
`Apresentacao/PainelRapido.xaml/.cs`, `Apresentacao/EstudioCores.xaml.cs` (preserva a escolha no
Salvar/Restaurar), `App.xaml.cs` (loop, menu, bandeja, log), `tests/.../Onda5Tests.cs` (novo),
`.csproj` (0.5.0), README, 03-PLANO, walkthrough.

### Validação executada
- Build Release 0 avisos/0 erros; suíte inteira **80/80** (14 testes novos: conta da CPU com
  kernel incluindo ocioso, intervalo vazio/contador voltando → sem número, RAM, arredondamento,
  leitura real desta máquina, largura/estado na barra, padrão e clone da configuração).
- Na barra real (2560×1600 a 150%), fotos ampliadas: normal 252 px com `CPU 5%`/`RAM 70%`
  alinhados às pílulas ↑/↓; compacto 366 px encostado à esquerda do '^'; desligado 156 px
  (idêntico ao anterior). RAM conferida com o Windows (CIM 70,3% × widget 70%).
- Chave do painel clicada com o mouse de verdade: desligou → 156 px e `MostrarCpuRam=false`
  salvo; religou → 252 px e `true`. (O `Toggle()` da UI Automation NÃO dispara o evento Click do
  WPF — no teste só mudou o desenho da chave; por isso o clique real.) Configuração do usuário
  restaurada (hash conferido).

## v0.4.3 — 2026-10-31 — Rótulo "auto update" no rodapé do estúdio

### O que mudou
No rodapé do estúdio de cores, o estado da atualização passou de "atualiza sozinho" para
**"auto update"** (ex.: `Versão 0.4.3 · auto update · consulta 31/10 12:40 — em dia`). Na cópia
sem instalador: "(sem auto update)".

### Por quê
Pedido do dono, depois de confirmar que a atualização automática funcionou: rótulo mais curto.

### Como usar
Nada a fazer: chega sozinho em até ~5 min nos PCs instalados.

### Arquivos
`App.xaml.cs` (`TextoVersaoRodape`), comentários em `EstudioCores.xaml/.cs`, `.csproj` (0.4.3),
README, walkthrough.

### Validação executada
Build + testes inteiros + publicação pelo `tools\publicar-atualizacao.ps1` : build e suíte inteira verdes (o script para se falhar); Release v0.4.3 publicada, delta de 5 arquivos alterados de 402.

## v0.4.2 — 2026-10-31 — Consulta de atualização a cada 5 min (era 1 h)

### O que mudou
O velocímetro instalado procura versão nova **a cada 5 minutos** (antes: de hora em hora). A 1ª
consulta continua 45 s após abrir, e 30 s após acordar da suspensão.

### Por quê
Pedido do dono: a versão nova deve chegar logo. É barato: o feed direto
`releases/latest/download/` não tem o limite de 60 consultas/h da API do GitHub, e cada consulta
baixa só o `releases.win.json` (~1 KB) — 12 por hora por PC é desprezível. O intervalo só muda nos
PCs depois que eles recebem esta versão (quem está na 0.4.0/0.4.1 ainda espera até 1 h).

### Como usar
Nada a fazer. Publicou → em até ~5 min (+ o próximo momento tranquilo) todos estão na versão nova.

### Arquivos
`App.xaml.cs` (`IntervaloConsulta`), `.csproj` (0.4.2), `tools\publicar-atualizacao.ps1` (comentário),
03-PLANO, README, walkthrough.

### Validação executada
- Build 0 avisos, testes 66/66 (pelo script de publicação); Releases v0.4.1 e v0.4.2 publicadas
  (pacote de diferença: 5 arquivos alterados de 402).
- **Atualização REAL pelo GitHub, aceite da Onda 4 ✅:** o velocímetro instalado na 0.4.0 foi
  reaberto às 11:56:16 (para não esperar a consulta de 1 h que a 0.4.0 ainda tem) → consultou o feed
  e baixou a 0.4.2 às 11:57:17 → "Momento tranquilo: aplicando" às 11:58:17 → "Package version 0.4.2
  applied successfully" → reaberto sozinho às 11:58:25 ("Reaberto pelo atualizador: agora na versão
  0.4.2"). Sem janela, sem clique. A 0.4.1 foi pulada (o feed só oferece a última).
- ⚠️ **Observação não reproduzida:** duas capturas aos ~17 s e ~29 s depois do reinício mostraram
  o lugar do widget vazio, embora a janela estivesse lá (ao 77 s aparecia normal). O log não registra
  ocultação. Duas tentativas de reproduzir NÃO repetiram o vazio: (1) abrir com janela inicial oculta
  (`-WindowStyle Hidden`); (2) o ciclo completo do atualizador no ensaio local, medindo a cada 3 s por
  75 s (visível, sem cloak, texto desenhado desde o segundo 0). Fica para observar nas próximas versões.

## v0.4.1 — 2026-10-31 — Teste da atualização automática REAL pelo GitHub

### O que mudou
Nenhum código. Versão publicada só para provar, de ponta a ponta e pelo GitHub, que o velocímetro
instalado na 0.4.0 se atualiza sozinho (aceite da Onda 4, que até aqui só tinha o ensaio local).

### Por quê
O ensaio da v0.4.0 usou um feed numa pasta local. Faltava provar o caminho real: Release no GitHub →
feed `releases/latest/download/` → pacote de diferença → aplicação silenciosa no PC do usuário.

### Como usar
Nada a fazer: quem tem a 0.4.0 instalada recebe a 0.4.1 sozinho em até ~1 h.

### Arquivos
`.csproj` (0.4.1), README, walkthrough.

### Validação executada
Ver a seção "Resultado" logo abaixo (preenchida após a publicação).

## v0.4.0 — 2026-10-31 — Onda 4: instalador e atualização automática silenciosa (Velopack + GitHub)

### O que mudou
1. **Instalador sem administrador** (`VelocimetroInternetApp-win-Setup.exe`, nas Releases de
   https://github.com/helveciopereira/velocimetro-internet). Instala em
   `%LOCALAPPDATA%\VelocimetroInternetApp\current\`.
2. **Atualização automática silenciosa** (pedido do dono: "baixar, instalar e rodar
   automaticamente e de forma silenciosa"):
   - consulta 45 s depois de abrir e depois **de hora em hora** (e 30 s depois de acordar da suspensão);
   - baixa em segundo plano **só a diferença** (no ensaio: 0,13 MB em vez de 64 MB);
   - aplica e **reabre sozinho** no 1º "momento tranquilo" (conferido a cada minuto): painel, estúdio
     e menu fechados, mouse fora do widget, nada em tela cheia, medição não pausada. O widget
     some por ~2 s e volta na versão nova — sem janela, sem aviso, sem clique;
   - "Sair" com versão baixada: instala ao fechar, sem reabrir.
3. **Rodapé do estúdio** mostra a versão e o estado ("Versão 0.4.0 · atualiza sozinho · consulta 31/10 14:05 — em dia").
4. **"Iniciar com o Windows" mais esperto:** a cópia INSTALADA assume a entrada Run (é assim que a
   instalação substitui o antigo `dist\`); uma cópia solta (bin\, dist\) só corrige a entrada se o
   .exe registrado sumiu — antes, qualquer execução de teste roubava a entrada. Na desinstalação a
   entrada é apagada só se aponta para a cópia desinstalada.
5. **`tools\publicar-atualizacao.ps1`**: compila, roda os testes, publica, gera pacote completo +
   diferença, envia a Release e confere que o feed direto já mostra a versão.

### Por quê (decisões e causas)
- **Feed direto `releases/latest/download/` em vez da API do GitHub:** a API aceita só 60
  consultas/h por IP sem login; em rede compartilhada esgota e a atualização falharia calada
  (lição do app Lembretes). Repositório público = nenhuma senha dentro do .exe.
- **packId `VelocimetroInternetApp`, não `VelocimetroInternet`:** o desinstalador apaga a pasta de
  instalação INTEIRA, e `%LOCALAPPDATA%\VelocimetroInternet` guarda o log e o consumo do dia.
- **Publicação em pasta, sem arquivo único:** um .exe único comprimido muda quase inteiro a cada
  versão; em pasta, o pacote de diferença fica em KB.
- **"Momento tranquilo" por Win32 (WindowFromPoint), não `IsMouseOver`/`IsActive` do WPF:** aberto
  por processo em segundo plano (o próprio reinício), o WPF prende esses estados e a atualização
  nunca seria aplicada (defeito real do Lembretes v1.5.0).
- **`vpk` exige `DOTNET_ROLL_FORWARD=Major`** nesta máquina: é ferramenta .NET 8 e só há o runtime
  10 ("To install missing framework ... 8.0.0"). Já embutido no script.

### Como usar
- Usuário: executar o Setup uma vez. Pronto — atualizações chegam sozinhas.
- Publicar versão nova: aumentar `<Version>` no `.csproj`, entrada no walkthrough, commit, e
  `pwsh -File tools\publicar-atualizacao.ps1`.
- Ensaiar sem publicar: variável `VELOCIMETRO_UPDATE_SOURCE=<pasta do feed>` (ver ensaio abaixo).

### Arquivos
`Program.cs` (novo: Velopack antes do WPF), `Sistema\ServicoAtualizacao.cs` (novo: serviço +
`RegraMomentoTranquilo`), `App.xaml.cs` (relógio da atualização, Sair, retomada, entrada Run),
`Sistema\InicioComWindows.cs` (`CaminhoRegistrado`, `DeveReapontar`, `DesligarSeApontaPara`),
`Sistema\NativeMethods.cs` (`WindowFromPoint`, `GetAncestor`), `Apresentacao\TaskbarHost.cs`
(`EmTelaCheia`), `Apresentacao\EstudioCores.xaml(.cs)` (rodapé de versão), `.csproj` (0.4.0,
Velopack 1.2.161, `StartupObject`), `tools\publicar-atualizacao.ps1` (novo), `.gitignore`,
`tests\Onda4Tests.cs` (novo, +13 testes), README, 03-PLANO.

### Validação executada
- Build Release 0 avisos/0 erros; testes **66/66**.
- **Ensaio local do ciclo completo** (packId de teste `VelocimetroTeste`, feed numa pasta):
  Setup `--silent` instalou a 1.0.0 → app aberto em segundo plano → 1.0.1 empacotada no feed →
  consulta aos 49 s baixou o delta de **0,13 MB** → no momento tranquilo "Package version 1.0.1
  applied successfully" → **reabriu sozinho na 1.0.1** (PID novo, 173 s no total; log do app:
  "Reaberto pelo atualizador: agora na versão 1.0.1"). Depois: desinstalado, entrada Run e
  `settings.json` conferidos iguais aos de antes (hash), pasta de dados intacta.
- **Publicação real:** Release `v0.4.0` no GitHub (Setup 71,5 MB, pacote completo 64 MB);
  `releases/latest` = v0.4.0 e o feed direto lista a 0.4.0. A conferência final do script falhou
  por erro MEU (o GitHub serve o .json como octet-stream → `Content` em bytes); corrigido no script.
- **Migração deste PC:** Setup baixado do GitHub, `--silent` → `%LOCALAPPDATA%\VelocimetroInternetApp\current`
  na 0.4.0; ao abrir, a entrada Run foi reapontada do `dist\` para a instalação; 1ª consulta real
  aos 46 s: "releases.win.json from …/releases/latest/download/…" → "em dia"; captura da barra com
  o widget medindo (↑ 7,56 KB/s · ↓ 32,3 KB/s). O Setup criou atalhos no Menu Iniciar e na Área de Trabalho.

### Validação manual recomendada
1. Abrir o estúdio e conferir o rodapé "Versão 0.4.0 · atualiza sozinho".
2. Na próxima versão publicada (0.4.1), deixar o PC ligado ~1 h: o widget pisca uma vez e o rodapé passa a mostrar 0.4.1.

## v0.3.0 — 2026-10-31 — Onda 2: referência no gráfico, pausar medição e retomada após suspensão

### O que mudou
1. **Gráfico "Últimos 60 segundos" com referência visual** (pedido do dono). O topo do gráfico agora
   é um valor **redondo** da série 1-2-5 logo acima do pico do minuto (pico 94 KB/s → topo
   **100 KB/s**; 690 KB/s → **1 MB/s**; 38,7 Mbps → **50 Mbps**), marcado por uma linha tracejada com
   rótulo; a **metade** ganha uma linha pontilhada também rotulada (50 KB/s, 512 KB/s, 25 Mbps).
   Abaixo, legenda das cores (━ Envio · ━ Download). O texto "Topo do gráfico: 690 KB/s" saiu.
2. **Pausar medição** no menu do clique direito: o widget mostra "pausado"; ao retomar, linha de base
   nova (sem pico falso somando o tempo parado). Vale só na sessão — reabrir o app volta a medir.
3. **Retomada após suspender/hibernar:** ao receber `WM_POWERBROADCAST/PBT_APMRESUMEAUTOMATIC` o
   medidor recomeça (a média móvel não carrega o valor de antes de dormir) e o widget reencaixa.
4. **Blindagem do .csproj ampliada** para `**\backup\**` (ver causa abaixo).

### Por quê
- Com o topo igual ao próprio pico ("690 KB/s"), a curva sempre batia no teto e não dava noção de
  grandeza. Com números redondos dá para ler de relance "uns 70% de 1 MB/s". A série 1-2-5 garante
  que o topo fica entre 1× e 2,5× o pico (a curva ocupa sempre ≥ 40% da altura — testado).
- A metade de 1 KB/s saía "0,50 KB/s" porque o formatador da barra nunca desce de KB; o rótulo do
  gráfico desce uma unidade por conta própria ("512 B/s", "500 Kbps").
- **Build quebrou com erros de "definição duplicada":** o hook de backup automático criou
  `src\VelocimetroInternet\Apresentacao\backup\` (o diretório de trabalho do agente estava ali) e a
  blindagem só cobria `backup\` na raiz do projeto. Cópias movidas para `backup\v0.2.0-src-*` na raiz
  do repositório (ignorada pelo git) e o padrão virou `**\backup\**`.

### Como usar
Clique no widget → painel → gráfico com as duas linhas de referência. Clique direito → "Pausar medição".

### Arquivos
`SpeedFormatter.cs` (`EscalaRedonda`), `PainelRapido.xaml(.cs)` (guias, rótulos, legenda),
`App.xaml.cs` (pausa, retomada), `TaskbarWidget.xaml.cs` (`MostrarPausado`), `NativeMethods.cs`
(`WM_POWERBROADCAST`), `.csproj` (0.3.0, blindagem), `Onda1a3Tests.cs` (+9 testes), README, 03-PLANO.

### Validação executada
- Build Release 0 avisos/0 erros; testes **53/53** (8 casos de escala em bytes e bits, piso em repouso,
  e a garantia 1×–2,5× do pico).
- Na sessão real: painel aberto pela 2ª execução, captura com topo "100 KB/s", meio "50 KB/s" e
  legenda, pico do minuto 94 KB/s dentro da escala.

### Validação manual recomendada
1. Clique direito → Pausar medição: widget mostra "pausado"; desmarcar volta a medir sem pico falso.
2. Suspender o PC e voltar: números normais no 1º segundo, widget no lugar.
3. Baixar algo grande com o painel aberto: o topo sobe de degrau (ex.: 10 → 20 → 50 MB/s).

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

