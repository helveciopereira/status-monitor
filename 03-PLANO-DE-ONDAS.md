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

## Onda 1 — MVP funcional ⬜ (o pedido completo)

- [ ] `NetworkSampler` + `InterfaceFilter` + `SpeedCalculator` (com testes)
- [ ] `SpeedFormatter`: unidade automática, histerese, vírgula, largura fixa (com testes)
- [ ] `TaskbarWidget`: ↑ upload azul em cima, ↓ download vermelho embaixo
- [ ] `TaskbarHost` + `TaskbarLayout` completos: vaga do Clima, vigia de colisão (compactar →
      ocultar), TaskbarCreated, DPI, tela cheia, auto-ocultar
- [ ] Aparência estilo Clima: realce arredondado ao passar o mouse; clique-direito = menu
- [ ] `ThemeWatcher`: paleta escura/clara automática
- [ ] Instância única, sem Alt+Tab, sem foco, log de erros em `%LocalAppData%`
- [ ] `TrayIcon` com menu: Pausar · Iniciar com o Windows ✓ · Sair
- [ ] `StartupManager` (chave Run) — ligado por padrão no primeiro uso

**Aceite:** baixar um arquivo grande e o valor bater ±3% com o Gerenciador de Tarefas;
reiniciar o PC e o widget aparecer sozinho; Alt+Tab não mostra o app; trocar tema claro/escuro e
as cores se adaptarem; jogo/vídeo em tela cheia não mostra o widget. → **versão 0.2.0**

## Onda 2 — Configurações e robustez ⬜

- [ ] `SettingsService` com `settings.json` (leitura tolerante, gravação atômica)
- [ ] Janela de Configurações: unidade bits/bytes, intervalo, suavização, posição, fonte/tamanho,
      interfaces incluídas/excluídas, ordem das linhas
- [ ] Posição **manual por arrastar** (Ctrl + arrastar o widget)
- [ ] **Flyout do clique** (como o painel do Clima): mini gráfico 60 s, pico, interface ativa
- [ ] Vários monitores (opção "mostrar em todas as barras")
- [ ] Comportamento ao suspender/hibernar/trocar de rede

**Aceite:** cada opção muda o widget **na hora**, sem reiniciar; apagar/corromper o JSON não
derruba o app. → **0.3.0**

## Onda 3 — Escolha de cores (o "futuro" do pedido) ⬜

- [ ] Seletor de cor para upload e download, **separado por tema** (escuro/claro)
- [ ] Prévia ao vivo sobre uma amostra da barra
- [ ] **Aviso de contraste** se a cor escolhida ficar ilegível no fundo (< 4,5:1)
- [ ] Botão "restaurar padrão" + 4–5 predefinições (Clássico azul/vermelho, Neon, Monocromático,
      Alto contraste, Verde/laranja)

**Aceite:** trocar a cor e ver na barra imediatamente; cor salva sobrevive ao reinício. → **0.4.0**

## Onda 4 — Distribuição ⬜

- [ ] Instalador **sem administrador** + atualização automática silenciosa com **Velopack**
      + GitHub Releases (receita já existe nesta máquina: skill `atualizacao-automatica-wpf-com-velopack`)
- [ ] Ícone do app, versão no menu "Sobre"
- [ ] Desinstalação limpa (remove a chave Run e a pasta de configuração, se o usuário quiser)
- [ ] (Opcional) assinatura de código para evitar aviso do SmartScreen

**Aceite:** instalar num usuário limpo, publicar 0.4.1 e ver o app se atualizar sozinho. → **1.0.0**

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
