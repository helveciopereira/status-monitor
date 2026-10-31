# Walkthrough — Status Monitor, ex-Velocímetro de Internet (Windows 11 · C# WPF .NET 10)

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

