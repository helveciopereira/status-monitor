# 04 — Ideias para depois do MVP

Priorizadas por **valor × esforço**. Nenhuma entra antes da Onda 1 estar pronta — o produto
principal é "dois números certos, bonitos e leves na barra".

## Alto valor, baixo esforço (candidatas à Onda 5)

> ✅ **Todas entregues na Onda 5 (v1.0.0, 2026-10-31)** — ver `03-PLANO-DE-ONDAS.md`.

| Ideia | Por quê |
|---|---|
| **Tooltip ao passar o mouse**: interface ativa, IP local, total baixado/enviado hoje, pico da sessão | Informação extra sem ocupar espaço |
| **Consumo do dia / do mês** (soma persistida) | Útil em internet com franquia (4G/5G, satélite) |
| **Alerta de franquia**: avisa ao chegar em X GB no mês | Complementa o item acima |
| **Indicador de sem conexão**: números ficam cinza e aparece "—" quando a rede cai | Mostra na hora que a internet caiu |
| **Cor por intensidade** (opcional): o número fica mais forte quanto mais perto da velocidade contratada | Leitura de relance, sem precisar ler o número |
| **Duplo clique abre o Gerenciador de Tarefas › Desempenho** | Atalho natural |

## Alto valor, esforço médio

| Ideia | Observação |
|---|---|
| **Mini gráfico (sparkline)** dos últimos 60 s atrás dos números ou num popup | Mostra tendência, não só o instante |
| **Painel ao clicar — versão ampliada**: gráfico de 5 min/1 h, máximos, médias | O flyout básico (60 s) **subiu para a Onda 2** por causa do estilo "Clima"; aqui fica a versão completa |
| **Latência (ping)** para o gateway e para 1.1.1.1, a cada 5 s, como terceira informação opcional | Internet "lenta" muitas vezes é latência, não banda. Custo de rede desprezível |
| **Teste de velocidade sob demanda** (botão no menu) | Só quando o usuário pedir, nunca em segundo plano |
| **Histórico em SQLite** (por hora/dia) com exportação CSV | Base para relatórios e franquia |

## Alto valor, alto esforço

| Ideia | Observação |
|---|---|
| **Consumo por aplicativo** ("quem está usando minha banda?") | Exige ETW (rastreamento de eventos do kernel) → normalmente **administrador**. Pode ser um modo opcional |
| **Relatório para reclamar com a operadora**: registra quedas e velocidades abaixo do contratado, gera PDF | Muito útil no Brasil (Anatel exige 40% instantâneo / 80% média mensal) |
| **Temas completos**: fonte, tamanho, só setas, só números, uma linha `↑1,2 ↓38` | Personalização além de cores |

## Ideias descartadas (e por quê)

- **Testar velocidade continuamente em segundo plano** — consome a própria banda que o app mede e
  seria tudo menos silencioso.
- **Notificações frequentes** ("sua internet caiu!") por padrão — contraria o "silencioso";
  só como opção desligada por padrão.
- **Versão para a bandeja com o número no ícone** como modo principal — 16–24 px não comportam dois
  números legíveis; pode existir como modo extra "só download".
- **Telemetria / conta online** — sem necessidade; o app não deve sair para a internet.

## Perguntas em aberto para o dono

1. ~~Prefere **Mbps** (comparar com o plano) ou **MB/s** (comparar com o navegador) como padrão?~~
   ✅ Respondido em 2026-10-31: **MB/s (bytes)** como padrão, com opção de mudar nas configurações.
   Design escolhido: **Modelo 3: Minimalismo Acrylic Pill** (`design/modelo-3-acrylic-pill/`).
2. O ícone na bandeja pode ficar, ou quer 100% invisível (menu só pelo clique-direito nos números)?
   *(Com o estilo Clima, o clique-direito no bloco já dá acesso a tudo — o ícone da bandeja vira opcional.)*
5. ~~Onde exatamente na barra?~~ ✅ Respondido em 2026-10-31: **como o Clima** (01 §3.1).
3. Vai distribuir para outras pessoas (precisa de instalador/atualização, Onda 4) ou é só para este PC?
4. Qual a velocidade contratada? (Serve para a "cor por intensidade" e o relatório para a operadora.)
