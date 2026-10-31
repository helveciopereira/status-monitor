using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// Janela sem borda do widget da barra de tarefas seguindo o Modelo 3 (Minimalismo Acrylic Pill).
/// Exibe upload (azul) em cima e download (vermelho) embaixo em cápsulas translúcidas,
/// alternando automaticamente para 1 linha compacta quando os botões de apps encostam no bloco.
/// </summary>
public partial class TaskbarWidget : Window
{
    private readonly SpeedFormatter _formatadorUpload;
    private readonly SpeedFormatter _formatadorDownload;

    public TaskbarWidget(ModoUnidadeVelocidade modoUnidade = ModoUnidadeVelocidade.BytesPorSegundo)
    {
        InitializeComponent();
        _formatadorUpload = new SpeedFormatter(modoUnidade);
        _formatadorDownload = new SpeedFormatter(modoUnidade);
    }

    /// <summary>
    /// Define os textos fixos exigidos na PoC 1 da Onda 0 ("↑ 1,24 MB/s" e "↓ 38,7 MB/s")
    /// para validação visual nítida a 150% DPI.
    /// </summary>
    public void DefinirTextosFixosPoC(string textoUpload = "1,24 MB/s", string textoDownload = "38,7 MB/s")
    {
        TextoUpload.Text = textoUpload;
        TextoDownload.Text = textoDownload;

        // No modo compacto de 1 linha, exibe a unidade apenas no final para caber com folga
        string upSemUnidade = textoUpload.Split(' ')[0];
        TextoCompactoUpload.Text = $"↑ {upSemUnidade}";
        TextoCompactoDownload.Text = $"↓ {textoDownload}";
    }

    /// <summary>
    /// Atualiza os valores em tempo real a partir das velocidades calculadas em bytes por segundo.
    /// </summary>
    public void AtualizarVelocidades(double uploadBytesPorSegundo, double downloadBytesPorSegundo)
    {
        string upCompleto = _formatadorUpload.FormatarValorEUnidade(uploadBytesPorSegundo);
        string downCompleto = _formatadorDownload.FormatarValorEUnidade(downloadBytesPorSegundo);

        TextoUpload.Text = upCompleto;
        TextoDownload.Text = downCompleto;

        // Linha única: o 1º valor sai só com o número e o ÚLTIMO leva a unidade (economiza
        // largura). v0.8.0: com "download em cima" o ↓ vem primeiro, então os papéis se invertem.
        if (_downloadEmCima)
        {
            TextoCompactoDownload.Text = $"↓ {downCompleto.Split(' ')[0]}";
            TextoCompactoUpload.Text = $"↑ {upCompleto}";
        }
        else
        {
            TextoCompactoUpload.Text = $"↑ {upCompleto.Split(' ')[0]}";
            TextoCompactoDownload.Text = $"↓ {downCompleto}";
        }
    }

    // ------------------------------------------------------------------ v0.8.0: aparência

    private bool _downloadEmCima;
    private bool _mostrarSistema = true;
    private double _fatorLargura = 1.0;

    /// <summary>
    /// v0.8.0: fonte, tamanho do texto e ordem das linhas (estúdio). A LARGURA da janela continua
    /// sendo do TaskbarHost (que recebe o mesmo fator); aqui só a coluna CPU/RAM acompanha.
    /// </summary>
    public void AplicarAparencia(AparenciaWidget aparencia, double fatorLargura)
    {
        Resources["Widget.Fonte"] = aparencia.Familia;
        Resources["Widget.TamanhoValor"] = aparencia.TamanhoValor;
        Resources["Widget.TamanhoRotulo"] = aparencia.TamanhoRotulo;
        Resources["Widget.TamanhoCompacto"] = aparencia.TamanhoCompacto;
        Resources["Widget.TamanhoSeparador"] = aparencia.TamanhoSeparador;
        _fatorLargura = fatorLargura;
        DefinirMostrarSistema(_mostrarSistema); // recalcula a largura da coluna CPU/RAM

        // Ordem das linhas: CPU/RAM ficam onde estão (CPU em cima); só ↑ e ↓ trocam
        _downloadEmCima = aparencia.DownloadEmCima;
        Grid.SetRow(PilulaUpload, _downloadEmCima ? 2 : 0);
        Grid.SetRow(PilulaDownload, _downloadEmCima ? 0 : 2);

        // Linha única: [CPU·RAM |] 1º | 2º — reordena só os três últimos filhos
        LinhaCompacta.Children.Remove(TextoCompactoUpload);
        LinhaCompacta.Children.Remove(SeparadorCompactoRede);
        LinhaCompacta.Children.Remove(TextoCompactoDownload);
        LinhaCompacta.Children.Add(_downloadEmCima ? TextoCompactoDownload : TextoCompactoUpload);
        LinhaCompacta.Children.Add(SeparadorCompactoRede);
        LinhaCompacta.Children.Add(_downloadEmCima ? TextoCompactoUpload : TextoCompactoDownload);
    }

    /// <summary>
    /// v0.5.0: mostra/esconde a coluna CPU (em cima) / RAM (embaixo) à esquerda do upload/download.
    /// A LARGURA da janela é decidida pelo TaskbarHost (TaskbarLayout); aqui só some o conteúdo.
    /// </summary>
    public void DefinirMostrarSistema(bool mostrar)
    {
        _mostrarSistema = mostrar;
        Visibility v = mostrar ? Visibility.Visible : Visibility.Collapsed;
        PilulaCpu.Visibility = v;
        PilulaRam.Visibility = v;
        CompactoSistema.Visibility = v;
        // Coluna com largura fixa: precisa ir a 0, senão sobraria um vão vazio à esquerda.
        // v0.8.0: a largura acompanha a fonte/tamanho (61 no padrão — mesma conta do TaskbarLayout).
        ColunaSistema.Width = new GridLength(mostrar ? TaskbarLayout.LarguraColunaSistema(_fatorLargura) : 0);
        ColunaRespiro.Width = new GridLength(mostrar ? TaskbarLayout.RespiroSistemaLogicoPx : 0);
    }

    /// <summary>Atualiza CPU e RAM (%) nos dois layouts. null = leitura indisponível ("—").</summary>
    public void AtualizarSistema(double? cpuPercentual, double? ramPercentual)
    {
        string cpu = Medicao.MedidorSistema.Formatar(cpuPercentual);
        string ram = Medicao.MedidorSistema.Formatar(ramPercentual);
        TextoCpu.Text = cpu;
        TextoRam.Text = ram;
        TextoCompactoSistema.Text = $"CPU {cpu} · RAM {ram}";
    }

    /// <summary>
    /// Alterna entre o layout normal de 2 linhas (Acrylic Pill) e o layout compacto de 1 linha.
    /// </summary>
    public void AplicarEstadoColisao(EstadoColisaoBarra estado)
    {
        if (estado == EstadoColisaoBarra.CompactoUmaLinha)
        {
            PainelDuasLinhas.Visibility = Visibility.Collapsed;
            PainelCompactoUmaLinha.Visibility = Visibility.Visible;
        }
        else
        {
            PainelDuasLinhas.Visibility = Visibility.Visible;
            PainelCompactoUmaLinha.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Aplica as cores já resolvidas pelo motor de paletas (tema da barra + paleta escolhida +
    /// ajuste de contraste WCAG). Substitui o antigo AplicarTemaBarra de cores fixas (v0.1.x).
    /// </summary>
    public void AplicarCores(CoresResolvidas cores)
    {
        Resources["UploadTextBrush"] = CriarBrush(cores.Upload);
        Resources["DownloadTextBrush"] = CriarBrush(cores.Download);
        Resources["UploadPillBgBrush"] = CriarBrush(cores.FundoPilulaUpload);
        Resources["UploadPillBorderBrush"] = CriarBrush(cores.BordaPilulaUpload);
        Resources["DownloadPillBgBrush"] = CriarBrush(cores.FundoPilulaDownload);
        Resources["DownloadPillBorderBrush"] = CriarBrush(cores.BordaPilulaDownload);
        Resources["HoverContainerBrush"] = CriarBrush(cores.Realce);
        Resources["CompactoFundoBrush"] = CriarBrush(cores.FundoCompacto);
        Resources["CompactoBordaBrush"] = CriarBrush(cores.BordaCompacto);
        Resources["SeparadorBrush"] = CriarBrush(cores.Separador);

        // v0.5.0: CPU/RAM em tom neutro derivado do separador (branco na barra escura, preto na
        // clara) — legível nos dois temas e sem disputar atenção com as cores da paleta.
        Color neutro = Color.FromRgb(cores.Separador.R, cores.Separador.G, cores.Separador.B);
        Resources["SistemaTextBrush"] = CriarBrush(Color.FromArgb(0xE6, neutro.R, neutro.G, neutro.B));
        Resources["SistemaRotuloBrush"] = CriarBrush(Color.FromArgb(0xA6, neutro.R, neutro.G, neutro.B));
        Resources["SistemaPillBgBrush"] = CriarBrush(Color.FromArgb(0x1A, neutro.R, neutro.G, neutro.B));
        Resources["SistemaPillBorderBrush"] = CriarBrush(Color.FromArgb(0x33, neutro.R, neutro.G, neutro.B));
    }

    /// <summary>Troca MB/s ↔ Mbps na hora (o próximo tick já sai na unidade nova).</summary>
    public void DefinirUnidade(ModoUnidadeVelocidade modo)
    {
        _formatadorUpload.ModoUnidade = modo;
        _formatadorDownload.ModoUnidade = modo;
    }

    /// <summary>Mostra "—" quando não há interface física conectada (ideia "sem conexão" do 04-IDEIAS).</summary>
    public void MostrarSemConexao()
    {
        TextoUpload.Text = "—";
        TextoDownload.Text = "—";
        TextoCompactoUpload.Text = "↑ —";
        TextoCompactoDownload.Text = "↓ —";
    }

    /// <summary>Medição pausada pelo menu (v0.3.0): texto explícito em vez de números congelados.</summary>
    public void MostrarPausado()
    {
        TextoUpload.Text = "pausado";
        TextoDownload.Text = "pausado";
        TextoCompactoUpload.Text = "↑ —";
        TextoCompactoDownload.Text = "↓ pausado";
        AtualizarSistema(null, null); // pausa vale para tudo: nada de CPU/RAM congelados
    }

    /// <summary>Clique esquerdo no widget (abre o painel rápido, como o Clima abre os widgets).</summary>
    public event Action? CliqueEsquerdo;

    /// <summary>Clique direito no widget (menu).</summary>
    public event Action? CliqueDireito;

    // ------------------------------------------------------------------ v0.8.0: Ctrl + arrastar

    /// <summary>Ctrl + botão esquerdo pressionado sobre o widget: o arrasto começou.</summary>
    public event Action? ArrasteIniciado;

    /// <summary>Deslocamento horizontal do cursor desde o início do arrasto, em px FÍSICOS.</summary>
    public event Action<int>? ArrasteMovido;

    /// <summary>Botão solto (ou captura perdida): o host decide se a posição vale e a grava.</summary>
    public event Action? ArrasteConcluido;

    private bool _arrastando;
    private int _cursorInicialXPx;

    /// <summary>Há um arrasto em andamento (o host não reposiciona por baixo dele).</summary>
    public bool Arrastando => _arrastando;

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        // Só com Ctrl: o clique simples continua abrindo o painel rápido, como sempre
        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0
            || !Sistema.NativeMethods.GetCursorPos(out Sistema.NativeMethods.POINT p))
        {
            return;
        }

        // Cursor em px físicos (o processo é PerMonitorV2): mesma unidade do SetWindowPos do host
        _cursorInicialXPx = p.X;
        _arrastando = true;
        CaptureMouse(); // continua recebendo o movimento mesmo com o cursor fora do widget
        System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.SizeWE;
        ArrasteIniciado?.Invoke();
        e.Handled = true;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_arrastando && Sistema.NativeMethods.GetCursorPos(out Sistema.NativeMethods.POINT p))
        {
            ArrasteMovido?.Invoke(p.X - _cursorInicialXPx);
        }
    }

    protected override void OnLostMouseCapture(System.Windows.Input.MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        // Captura roubada (Alt+Tab, outra janela): encerra o arrasto onde o widget está
        EncerrarArrasto();
    }

    /// <summary>Fecha o arrasto uma única vez (o ReleaseMouseCapture também dispara LostMouseCapture).</summary>
    private void EncerrarArrasto()
    {
        if (!_arrastando)
        {
            return;
        }

        _arrastando = false;
        System.Windows.Input.Mouse.OverrideCursor = null;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        ArrasteConcluido?.Invoke();
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        // Fim de um arrasto NÃO é clique (não abre o painel)
        if (_arrastando)
        {
            EncerrarArrasto();
            e.Handled = true;
            return;
        }

        CliqueEsquerdo?.Invoke();
    }

    protected override void OnMouseRightButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        e.Handled = true;
        CliqueDireito?.Invoke();
    }

    private static SolidColorBrush CriarBrush(Color cor)
    {
        var brush = new SolidColorBrush(cor);
        brush.Freeze();
        return brush;
    }
}
