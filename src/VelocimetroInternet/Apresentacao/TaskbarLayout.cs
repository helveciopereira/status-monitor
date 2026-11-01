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
/// v1.2.0 (Onda 7): como o widget mostra os números na barra (estúdio › "Modo de exibição").
/// </summary>
public enum ModoExibicaoWidget
{
    /// <summary>↑ em cima e ↓ embaixo, em pílulas (o de sempre). Compacta sozinho se faltar espaço.</summary>
    DuasLinhas = 0,

    /// <summary>Sempre a linha única "↑ 1,2 | ↓ 38,7 MB/s" (a mesma do modo anti-colisão).</summary>
    UmaLinha = 1,

    /// <summary>Só a pílula de download, centralizada (para quem não liga para o envio).</summary>
    SoDownload = 2
}

/// <summary>v1.2.0: conversão entre o modo e o código gravado no settings.json.</summary>
public static class ModosExibicao
{
    public static ModoExibicaoWidget DeCodigo(string? codigo) => codigo switch
    {
        "uma-linha" => ModoExibicaoWidget.UmaLinha,
        "so-download" => ModoExibicaoWidget.SoDownload,
        _ => ModoExibicaoWidget.DuasLinhas
    };

    public static string Codigo(ModoExibicaoWidget modo) => modo switch
    {
        ModoExibicaoWidget.UmaLinha => "uma-linha",
        ModoExibicaoWidget.SoDownload => "so-download",
        _ => "duas-linhas"
    };
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

    /// <summary>Contorno externo (padding 4 + 4 do ContornoHover): NÃO escala com a fonte.</summary>
    public const int PaddingContornoLogicoPx = 8;

    /// <summary>Respiro fixo entre a coluna CPU/RAM e a pílula de rede.</summary>
    public const int RespiroSistemaLogicoPx = 3;

    /// <summary>Pílula ↑/↓ no padrão (78) e coluna CPU/RAM (61) — v0.6.0.</summary>
    public const int LarguraPilulaRedeLogicaPx = LarguraLogicaNormalPx - PaddingContornoLogicoPx;
    public const int LarguraColunaSistemaLogicaPx = LarguraLogicaSistemaNormalPx - RespiroSistemaLogicoPx;

    /// <summary>
    /// v0.8.0: distância (px lógicos) até a posição automática abaixo da qual soltar o widget
    /// arrastado "gruda" de volta no automático (apaga a posição manual).
    /// </summary>
    public const int ImaPosicaoAutomaticaLogicaPx = 16;

    /// <summary>Largura lógica do widget normal (2 linhas), com ou sem a coluna CPU/RAM.</summary>
    public static int LarguraLogicaNormal(bool mostrarSistema) => LarguraLogicaNormal(mostrarSistema, 1.0);

    /// <summary>
    /// v0.8.0: idem, com as pílulas escaladas pelo fator da fonte/tamanho escolhidos
    /// (<see cref="AparenciaWidget.FatorLargura"/>). Fator 1 = exatamente a largura de antes.
    /// </summary>
    public static int LarguraLogicaNormal(bool mostrarSistema, double fatorTexto) =>
        PaddingContornoLogicoPx
        + LarguraPilulaRede(fatorTexto)
        + (mostrarSistema ? LarguraColunaSistema(fatorTexto) + RespiroSistemaLogicoPx : 0);

    /// <summary>Largura lógica do widget compacto (1 linha), com ou sem CPU/RAM.</summary>
    public static int LarguraLogicaCompacta(bool mostrarSistema) => LarguraLogicaCompacta(mostrarSistema, 1.0);

    /// <summary>v0.8.0: idem, escalada pelo fator da fonte/tamanho (a linha única é quase só texto).</summary>
    public static int LarguraLogicaCompacta(bool mostrarSistema, double fatorTexto) =>
        Escalar(LarguraLogicaCompactaPx, fatorTexto)
        + (mostrarSistema ? Escalar(LarguraLogicaSistemaCompactaPx, fatorTexto) : 0);

    /// <summary>Pílula ↑/↓ (px lógicos) para o fator de texto — usada também pelo widget.</summary>
    public static int LarguraPilulaRede(double fatorTexto) => Escalar(LarguraPilulaRedeLogicaPx, fatorTexto);

    /// <summary>Coluna CPU/RAM (px lógicos) para o fator de texto — usada também pelo widget.</summary>
    public static int LarguraColunaSistema(double fatorTexto) => Escalar(LarguraColunaSistemaLogicaPx, fatorTexto);

    /// <summary>
    /// v1.2.0: linha única SÓ com o download ("↓ 88,8 Mbps" + padding 6+6 + borda): o trecho
    /// "↑ 1,2  |  " some. Medido no pior caso, como as demais larguras.
    /// </summary>
    public const int LarguraLogicaCompactaSoDownloadPx = 84;

    /// <summary>v1.2.0: largura da linha única no modo dado (só download = mais estreita).</summary>
    public static int LarguraLogicaCompacta(bool mostrarSistema, double fatorTexto, ModoExibicaoWidget modo) =>
        modo == ModoExibicaoWidget.SoDownload
            ? Escalar(LarguraLogicaCompactaSoDownloadPx, fatorTexto)
              + (mostrarSistema ? Escalar(LarguraLogicaSistemaCompactaPx, fatorTexto) : 0)
            : LarguraLogicaCompacta(mostrarSistema, fatorTexto);

    /// <summary>
    /// v1.2.0: largura e estado PREFERIDOS (com espaço sobrando). Uma linha prefere a linha única;
    /// os outros dois, o bloco de duas linhas (o "só download" ocupa a mesma pílula de rede).
    /// </summary>
    public static (int LarguraLogica, EstadoColisaoBarra Estado) Preferido(
        bool mostrarSistema, double fatorTexto, ModoExibicaoWidget modo) =>
        modo == ModoExibicaoWidget.UmaLinha
            ? (LarguraLogicaCompacta(mostrarSistema, fatorTexto, modo), EstadoColisaoBarra.CompactoUmaLinha)
            : (LarguraLogicaNormal(mostrarSistema, fatorTexto), EstadoColisaoBarra.NormalDuasLinhas);

    private static int Escalar(int larguraPadrao, double fator) =>
        (int)Math.Round(larguraPadrao * (double.IsFinite(fator) && fator > 0 ? fator : 1.0));

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
        bool mostrarSistema = false,
        double fatorTexto = 1.0,
        ModoExibicaoWidget modo = ModoExibicaoWidget.DuasLinhas)
    {
        double escala = escalaDpi <= 0.1 ? 1.0 : escalaDpi;

        // v1.2.0: "normal" = o tamanho PREFERIDO do modo (a linha única, no modo Uma linha)
        (int larguraPreferidaLogica, EstadoColisaoBarra estadoPreferido) = Preferido(mostrarSistema, fatorTexto, modo);
        int larguraNormalPx = (int)Math.Round(larguraPreferidaLogica * escala);
        int larguraCompactaPx = (int)Math.Round(LarguraLogicaCompacta(mostrarSistema, fatorTexto, modo) * escala);
        int alturaWidgetPx = Math.Min(rectBarra.Height, (int)Math.Round(AlturaLogicaWidgetPx * escala));
        int margemPx = (int)Math.Round(MargemSegurancaLogicaPx * escala);

        // Espaço físico livre entre o último botão de app aberto e o início da bandeja ('^')
        int espacoLivrePx = Math.Max(0, bordaEsquerdaBandejaPx - bordaDireitaAppsPx);

        // Determina o estado conforme o espaço livre (com margem de segurança dos dois lados)
        EstadoColisaoBarra estado;
        int larguraEscolhidaPx;

        if (espacoLivrePx >= larguraNormalPx + (margemPx * 2))
        {
            estado = estadoPreferido;
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

    /// <summary>
    /// v0.8.0 (Ctrl + arrastar): posição ESCOLHIDA pelo usuário, sempre no modo normal de 2 linhas.
    /// Devolve null quando ela não serve mais — o widget cobriria algum botão (os apps cresceram
    /// até ele, ou a barra mudou de tamanho/monitor) — e então o chamador volta à vaga automática.
    /// O X é limitado à faixa útil da barra (da borda esquerda até a margem antes do '^').
    /// </summary>
    /// <param name="xLogicoDesdeBordaBarra">Distância salva (px lógicos) da borda esquerda da barra até o widget.</param>
    /// <param name="zonasOcupadasPx">Trechos [Esquerda, Direita) em px físicos de TELA com botões (Iniciar + apps, Widgets…).</param>
    public static PosicaoWidgetBarra? CalcularPosicaoManual(
        NativeMethods.RECT rectBarra,
        int xLogicoDesdeBordaBarra,
        IReadOnlyList<(int Esquerda, int Direita)> zonasOcupadasPx,
        int bordaEsquerdaBandejaPx,
        double escalaDpi,
        bool mostrarSistema = false,
        double fatorTexto = 1.0,
        ModoExibicaoWidget modo = ModoExibicaoWidget.DuasLinhas)
    {
        double escala = escalaDpi <= 0.1 ? 1.0 : escalaDpi;
        // v1.2.0: a posição manual usa o tamanho preferido do modo (linha única no "Uma linha")
        (int larguraLogica, EstadoColisaoBarra estadoPreferido) = Preferido(mostrarSistema, fatorTexto, modo);
        int larguraPx = (int)Math.Round(larguraLogica * escala);
        int alturaPx = Math.Min(rectBarra.Height, (int)Math.Round(AlturaLogicaWidgetPx * escala));
        int margemPx = (int)Math.Round(MargemSegurancaLogicaPx * escala);

        int minimoPx = rectBarra.Left + margemPx;
        int maximoPx = bordaEsquerdaBandejaPx - larguraPx - margemPx;
        if (maximoPx < minimoPx)
        {
            return null; // barra estreita demais para o widget fora da vaga automática
        }

        int xTelaPx = Math.Clamp(rectBarra.Left + (int)Math.Round(xLogicoDesdeBordaBarra * escala), minimoPx, maximoPx);
        if (SobrepoeAlgumaZona(xTelaPx, larguraPx, margemPx, zonasOcupadasPx))
        {
            return null;
        }

        int yTelaPx = rectBarra.Top + ((rectBarra.Height - alturaPx) / 2);
        return new PosicaoWidgetBarra(
            Estado: estadoPreferido,
            XRelativoBarraPx: xTelaPx - rectBarra.Left,
            YRelativoBarraPx: yTelaPx - rectBarra.Top,
            XTelaPx: xTelaPx,
            YTelaPx: yTelaPx,
            LarguraFisicaPx: larguraPx,
            AlturaFisicaPx: alturaPx,
            EspacoLivreFisicoPx: maximoPx - minimoPx);
    }

    /// <summary>
    /// Limita o X durante o arrasto à faixa útil da barra (não sai da barra nem invade o '^').
    /// Ao contrário de <see cref="CalcularPosicaoManual"/>, deixa passar POR CIMA dos botões —
    /// o usuário está atravessando a barra; a validade só é julgada ao soltar.
    /// </summary>
    public static int LimitarXDuranteArrasto(
        NativeMethods.RECT rectBarra, int xTelaDesejadoPx, int larguraPx, int bordaEsquerdaBandejaPx, double escalaDpi)
    {
        double escala = escalaDpi <= 0.1 ? 1.0 : escalaDpi;
        int margemPx = (int)Math.Round(MargemSegurancaLogicaPx * escala);
        int minimoPx = rectBarra.Left + margemPx;
        int maximoPx = Math.Max(minimoPx, bordaEsquerdaBandejaPx - larguraPx - margemPx);
        return Math.Clamp(xTelaDesejadoPx, minimoPx, maximoPx);
    }

    /// <summary>[x - margem, x + largura + margem) encosta em algum trecho com botões?</summary>
    public static bool SobrepoeAlgumaZona(
        int xTelaPx, int larguraPx, int margemPx, IReadOnlyList<(int Esquerda, int Direita)> zonasOcupadasPx)
    {
        int esquerda = xTelaPx - margemPx;
        int direita = xTelaPx + larguraPx + margemPx;
        foreach ((int e, int d) in zonasOcupadasPx)
        {
            if (d > e && esquerda < d && direita > e)
            {
                return true;
            }
        }

        return false;
    }
}

