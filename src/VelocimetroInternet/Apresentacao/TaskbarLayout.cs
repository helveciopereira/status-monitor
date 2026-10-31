using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// Estado responsivo do widget na barra de tarefas frente ao espaço livre entre os ícones de apps e a bandeja.
/// Referência: 01-ANALISE.md §3.1 e Modelo 3 (Minimalismo Acrylic Pill).
/// </summary>
public enum EstadoColisaoBarra
{
    NormalDuasLinhas = 0,
    CompactoUmaLinha = 1,
    OcultoPorColisao = 2
}

/// <summary>
/// Geometria calculada (em pixels físicos da tela e da barra) para posicionar o widget na "vaga do Clima".
/// </summary>
public readonly record struct PosicaoWidgetBarra(
    EstadoColisaoBarra Estado,
    int XRelativoBarraPx,
    int YRelativoBarraPx,
    int XTelaPx,
    int YTelaPx,
    int LarguraFisicaPx,
    int AlturaFisicaPx,
    int EspacoLivreFisicoPx);

/// <summary>
/// Calcula de forma pura e testável a posição exata da "vaga do Clima" na barra de tarefas do Windows 11
/// e decide se o bloco deve ficar em 2 linhas (Acrylic Pill normal), compactar para 1 linha ou ocultar-se.
/// Referência: 01-ANALISE.md §3.1 e 02-ARQUITETURA.md §4.
/// </summary>
public static class TaskbarLayout
{
    /// <summary>
    /// Largura lógica do modo normal SÓ com a rede: 8 (padding do contorno) + 78 (pílula ↑/↓).
    /// v0.6.0 (pedido do dono): pílula de rede 96 → 78, entre a de CPU/RAM (61) e a antiga. 78 cabe
    /// o pior texto medido com a fonte do widget ("99,9 Mbps" ≈ 50 + seta 5 + padding 12 + borda 1,5
    /// + folga dos números tabulares). Antes: 104.
    /// </summary>
    public const int LarguraLogicaNormalPx = 86;
    public const int LarguraLogicaCompactaPx = 132;
    public const int AlturaLogicaWidgetPx = 42;
    public const int MargemSegurancaLogicaPx = 8;

    /// <summary>
    /// v0.5.0: largura somada quando CPU/RAM estão ligados. Normal = coluna de pílulas "CPU 100%"
    /// (+ 3 px de respiro); compacta = trecho "CPU 100% · RAM 100%  |" antes do ↑↓ na linha única.
    /// Medidos no pior caso (100%), para o texto nunca ser cortado.
    /// </summary>
    public const int LarguraLogicaSistemaNormalPx = 64;
    public const int LarguraLogicaSistemaCompactaPx = 112;

    /// <summary>Largura lógica do widget normal (2 linhas), com ou sem a coluna CPU/RAM.</summary>
    public static int LarguraLogicaNormal(bool mostrarSistema) =>
        LarguraLogicaNormalPx + (mostrarSistema ? LarguraLogicaSistemaNormalPx : 0);

    /// <summary>Largura lógica do widget compacto (1 linha), com ou sem CPU/RAM.</summary>
    public static int LarguraLogicaCompacta(bool mostrarSistema) =>
        LarguraLogicaCompactaPx + (mostrarSistema ? LarguraLogicaSistemaCompactaPx : 0);

    /// <summary>
    /// Calcula a posição na vaga do Clima (à esquerda do botão '^' da bandeja quando a barra está alinhada
    /// à esquerda, ou quando o Widgets do Windows está ativo).
    /// </summary>
    /// <param name="rectBarra">Retângulo físico da Shell_TrayWnd.</param>
    /// <param name="bordaDireitaAppsPx">Maior X+Largura dos botões de apps (via UI Automation).</param>
    /// <param name="bordaEsquerdaBandejaPx">Menor X do SystemTrayIcon '^' ou TrayNotifyWnd.</param>
    /// <param name="escalaDpi">Fator de escala do monitor (ex.: 1.5 para 150% / 144 DPI).</param>
    /// <param name="mostrarSistema">v0.5.0: CPU/RAM ligados → o bloco fica mais largo (e colide antes).</param>
    public static PosicaoWidgetBarra CalcularVagaDoClima(
        NativeMethods.RECT rectBarra,
        int bordaDireitaAppsPx,
        int bordaEsquerdaBandejaPx,
        double escalaDpi,
        bool mostrarSistema = false)
    {
        double escala = escalaDpi <= 0.1 ? 1.0 : escalaDpi;

        int larguraNormalPx = (int)Math.Round(LarguraLogicaNormal(mostrarSistema) * escala);
        int larguraCompactaPx = (int)Math.Round(LarguraLogicaCompacta(mostrarSistema) * escala);
        int alturaWidgetPx = Math.Min(rectBarra.Height, (int)Math.Round(AlturaLogicaWidgetPx * escala));
        int margemPx = (int)Math.Round(MargemSegurancaLogicaPx * escala);

        // Espaço físico livre entre o último botão de app aberto e o início da bandeja ('^')
        int espacoLivrePx = Math.Max(0, bordaEsquerdaBandejaPx - bordaDireitaAppsPx);

        // Determina o estado conforme o espaço livre (com margem de segurança dos dois lados)
        EstadoColisaoBarra estado;
        int larguraEscolhidaPx;

        if (espacoLivrePx >= larguraNormalPx + (margemPx * 2))
        {
            estado = EstadoColisaoBarra.NormalDuasLinhas;
            larguraEscolhidaPx = larguraNormalPx;
        }
        else if (espacoLivrePx >= (int)(larguraNormalPx * 0.75) + margemPx)
        {
            // Quando os apps se aproximam do bloco, entra no Modo Compacto de 1 linha
            estado = EstadoColisaoBarra.CompactoUmaLinha;
            larguraEscolhidaPx = Math.Min(larguraCompactaPx, espacoLivrePx - margemPx);
        }
        else
        {
            estado = EstadoColisaoBarra.OcultoPorColisao;
            larguraEscolhidaPx = larguraNormalPx;
        }

        // Posiciona encostado à esquerda da bandeja ('^'), respeitando a margem de 8 px lógicos
        int xTelaPx = bordaEsquerdaBandejaPx - larguraEscolhidaPx - margemPx;
        int yTelaPx = rectBarra.Top + ((rectBarra.Height - alturaWidgetPx) / 2);

        int xRelativoPx = xTelaPx - rectBarra.Left;
        int yRelativoPx = yTelaPx - rectBarra.Top;

        return new PosicaoWidgetBarra(
            Estado: estado,
            XRelativoBarraPx: xRelativoPx,
            YRelativoBarraPx: yRelativoPx,
            XTelaPx: xTelaPx,
            YTelaPx: yTelaPx,
            LarguraFisicaPx: larguraEscolhidaPx,
            AlturaFisicaPx: alturaWidgetPx,
            EspacoLivreFisicoPx: espacoLivrePx);
    }
}

