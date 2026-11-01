# Status Monitor (ex-Velocímetro de Internet) — planejamento

> **v0.6.0:** o app passou a se chamar **Status Monitor** (repositório renomeado de
> `velocimetro-internet` para `status-monitor`; o endereço antigo redireciona).
> **v0.7.0:** o executável passou a se chamar **`StatusMonitor.exe`** e o instalador
> **`StatusMonitor-Setup.exe`**.
> **v0.8.0:** intervalo de atualização, fonte/tamanho do texto, ordem das linhas, **Ctrl + arrastar**
> para mudar o widget de lugar e opção **"Mostrar em todos os monitores"** (no estúdio, cartão "Na barra de tarefas").
> **v1.0.0:** dica ao passar o mouse (IP, consumo de hoje/mês), **consumo do mês**, **alerta de franquia**,
> exportação do histórico por dia em **CSV**, cor por intensidade (opcional) e **duplo clique → Gerenciador de Tarefas**.
> **v1.1.0:** gráfico do painel com **1 min / 5 min / 1 h** e a média de cada janela; **latência** opcional
> (ping ao roteador e a 1.1.1.1, desligada por padrão — estúdio › Medição).
> **v1.2.0:** **modos de exibição** do widget (duas linhas, uma linha, só download — estúdio › Aparência) e
> **teste de velocidade sob demanda** (botão no painel e no menu; servidor da Cloudflare; compara com o plano).
>
> **v0.9.0:** **seletor visual de cor** (quadrado + barra de matiz) na paleta Personalizada.

Monitor silencioso de velocidade de rede para Windows 11: fica **na barra de tarefas**, inicia
com o sistema e mostra **upload e download ao mesmo tempo**, cada um com sua seta e sua cor.

```
 CPU  5%   ↑ 1,24 MB/s     ← azul  (upload)
 RAM 70%   ↓ 38,7 MB/s     ← vermelho (download)
```

Desde a v0.5.0 mostra também o **uso de CPU e RAM do computador, em %**, à esquerda da internet
(liga/desliga em "CPU e RAM na barra", no painel rápido, ou no menu do clique direito).

> Status: **Ondas 0 a 8 concluídas** (falta só a assinatura de código opcional, que depende de certificado comprado) · versão **1.3.0** · 2026-11-01
>
> **Instalar:** baixe e execute [`StatusMonitor-Setup.exe`](https://github.com/helveciopereira/status-monitor/releases/latest/download/StatusMonitor-Setup.exe)
> (sem administrador; instala em `%LOCALAPPDATA%\VelocimetroInternetApp`). Depois disso ele **se atualiza sozinho**, em silêncio.
> **Publicar versão nova:** aumente `<Version>` no `.csproj` e rode `pwsh -File tools\publicar-atualizacao.ps1`.
>
> **Como usar:** clique no widget = painel
> rápido; clique direito (widget ou ícone perto do relógio) = menu; "Cores e mais configurações" = estúdio.
> Design escolhido: **Modelo 3 — Minimalismo Acrylic Pill** ([`design/modelo-3-acrylic-pill/`](design/modelo-3-acrylic-pill/)) · Unidade padrão: **`MB/s`** (configurável para `Mbps`)

**Onde fica:** num bloco próprio na barra, **como o botão do Clima** — fora dos ícones da bandeja,
à esquerda do "^". Verificado nesta máquina: há ~628 px livres ali; o bloco usa ~129 (só internet) ou ~225 (com CPU/RAM) pixels a 150%. Não existe
API oficial para ocupar o lugar do Clima, então o app **imita** o visual e o comportamento dele
(detalhes em [01-ANALISE.md §3.1](01-ANALISE.md)).

## Documentos

| Arquivo | O que tem |
|---|---|
| [01-ANALISE.md](01-ANALISE.md) | Análise do pedido: o que está claro, o que é ambíguo, armadilhas do Windows 11 e as decisões tomadas |
| [02-ARQUITETURA.md](02-ARQUITETURA.md) | Linguagem, framework, como medir a rede, como "entrar" na barra, estrutura do código |
| [03-PLANO-DE-ONDAS.md](03-PLANO-DE-ONDAS.md) | Roteiro de execução em ondas (PoC → MVP → cores → distribuição), com critérios de aceite |
| [04-IDEIAS.md](04-IDEIAS.md) | Backlog de ideias futuras, priorizadas por valor × esforço |

## Decisão em uma linha

**C# + .NET 10 (LTS) + WPF**, janela própria sem borda "acoplada" à barra de tarefas (o Windows 11
não permite mais plugin nativo de barra), medição via contadores das interfaces de rede a cada 1 s,
início automático pela chave `Run` do usuário (sem administrador), ícone na bandeja para
configurações. Detalhes e alternativas descartadas em [02-ARQUITETURA.md](02-ARQUITETURA.md).

## Histórico do plano

- **0.0.2** (2026-10-31) — posição definida "como o Clima"; espaço medido por UI Automation;
  vigia de colisão com os botões de apps; flyout ao clicar sobe para a Onda 2; Radmin VPN no filtro.
- **0.0.1** (2026-10-31) — planejamento inicial.

## Pré-requisito encontrado nesta máquina

⚠️ **O .NET SDK não está instalado** — só runtimes (6.0, 8.0, 9.0). Para compilar é preciso o
**SDK do .NET 10**: `winget install Microsoft.DotNet.SDK.10` (ou instalação por usuário via
`dotnet-install.ps1`, sem administrador). Isso é o passo 0 da Onda 0.
