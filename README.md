# Velocímetro de Internet — planejamento

Monitor silencioso de velocidade de rede para Windows 11: fica **na barra de tarefas**, inicia
com o sistema e mostra **upload e download ao mesmo tempo**, cada um com sua seta e sua cor.

```
 ↑ 1,24 MB/s     ← azul  (upload)
 ↓ 38,7 MB/s     ← vermelho (download)
```

> Status: **Ondas 0 e 1 concluídas; painel rápido (Onda 2) e estúdio de cores (Onda 3) entregues em boa parte** · versão **0.2.0** · 2026-10-31
>
> **Como usar:** rode `dist\VelocimetroInternet.exe` (gerado por `tools\publicar.ps1`). Clique no widget = painel
> rápido; clique direito (widget ou ícone perto do relógio) = menu; "Cores e mais configurações" = estúdio.
> Design escolhido: **Modelo 3 — Minimalismo Acrylic Pill** ([`design/modelo-3-acrylic-pill/`](design/modelo-3-acrylic-pill/)) · Unidade padrão: **`MB/s`** (configurável para `Mbps`)

**Onde fica:** num bloco próprio na barra, **como o botão do Clima** — fora dos ícones da bandeja,
à esquerda do "^". Verificado nesta máquina: há ~628 px livres ali; o bloco usa ~150. Não existe
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
