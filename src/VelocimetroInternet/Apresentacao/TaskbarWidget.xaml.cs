using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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

        // v1.0.0 (Onda 5): dica ao parar o mouse em cima. O texto é montado NA HORA de abrir
        // (o App responde PedirDica com consumo/IP atuais) — nada é calculado enquanto ninguém olha.
        _dica = new ToolTip { Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        ContornoHover.ToolTip = _dica;
        ToolTipService.SetInitialShowDelay(ContornoHover, 700);
        ToolTipService.SetShowDuration(ContornoHover, 20_000);
        ContornoHover.ToolTipOpening += (_, e) =>
        {
            string? texto = PedirDica?.Invoke();
            if (texto is null)
            {
                e.Handled = true; // ninguém respondeu: não abre uma dica vazia
                return;
            }

            _dica.Content = texto;
        };
    }

    // ------------------------------------------------------------------ v1.0.0: dica e intensidade

    private readonly ToolTip _dica;

    /// <summary>Texto da dica, pedido ao App na hora de abrir (null = não mostrar).</summary>
    public Func<string?>? PedirDica { get; set; }

    /// <summary>Duplo clique no widget (abre o Gerenciador de Tarefas).</summary>
    public event Action? DuploClique;

    /// <summary>
    /// Velocidade de referência da "cor por intensidade" (bytes/s). 0 = desligada: os números
    /// ficam sempre com opacidade 1, o visual de sempre.
    /// </summary>
    private double _referenciaIntensidade;

    public void DefinirReferenciaIntensidade(double referenciaBytesPorSegundo)
    {
        _referenciaIntensidade = referenciaBytesPorSegundo > 0 ? referenciaBytesPorSegundo : 0;
        if (_referenciaIntensidade == 0)
        {
            AplicarOpacidade(1, 1); // desligou: volta ao normal já, sem esperar o próximo tick
        }
    }

    private void AplicarOpacidade(double upload, double download)
    {
        TextoUpload.Opacity = TextoCompactoUpload.Opacity = upload;
        TextoDownload.Opacity = TextoCompactoDownload.Opacity = download;
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
        // v1.2.0: no "só download" o ↓ está sozinho na linha e leva a unidade sempre.
        if (_modo == ModoExibicaoWidget.SoDownload)
        {
            TextoCompactoDownload.Text = $"↓ {downCompleto}";
        }
        else if (_downloadEmCima)
        {
            TextoCompactoDownload.Text = $"↓ {downCompleto.Split(' ')[0]}";
            TextoCompactoUpload.Text = $"↑ {upCompleto}";
        }
        else
        {
            TextoCompactoUpload.Text = $"↑ {upCompleto.Split(' ')[0]}";
            TextoCompactoDownload.Text = $"↓ {downCompleto}";
        }

        // v1.8.1: sem internet (rede local ok) — o número continua (é o tráfego real da rede local),
        // mas apagado, e a linha única ganha o "⚠" na frente do ↓ (lá não há seta separada para trocar)
        if (_semInternet)
        {
            TextoCompactoDownload.Text = "⚠ " + TextoCompactoDownload.Text;
            AplicarOpacidade(DicaWidget.OpacidadeMinima, DicaWidget.OpacidadeMinima);
        }
        // v1.0.0: cor por intensidade (opcional) — mais forte quanto mais perto da referência
        else if (_referenciaIntensidade > 0)
        {
            AplicarOpacidade(
                DicaWidget.OpacidadePorIntensidade(uploadBytesPorSegundo, _referenciaIntensidade),
                DicaWidget.OpacidadePorIntensidade(downloadBytesPorSegundo, _referenciaIntensidade));
        }

        RegistrarNoGrafico(uploadBytesPorSegundo, downloadBytesPorSegundo); // v1.7.0
    }

    // ------------------------------------------------------------------ v1.8.1: sem internet

    private bool _semInternet;

    /// <summary>
    /// v1.8.1 (varredura 2026-11-03): "sem internet" é diferente de "sem rede". A placa está ligada ao
    /// roteador, mas o Windows diz que não alcança a internet (o mesmo aviso do ícone de rede). O
    /// widget mostra a seta ↓ como "⚠" na cor de alerta (a mesma do CPU/RAM ≥ 80 %, já calibrada
    /// pelo motor de contraste) e os números apagados — eles seguem valendo para a rede local.
    /// Idempotente: só mexe na tela quando o estado muda. Chamado antes de <see cref="AtualizarVelocidades"/>.
    /// </summary>
    public void DefinirSemInternet(bool semInternet)
    {
        if (semInternet == _semInternet)
        {
            return;
        }

        _semInternet = semInternet;
        SetaDownload.Text = semInternet ? "⚠" : "↓";
        SetaDownload.SetResourceReference(TextBlock.ForegroundProperty, semInternet ? "SistemaAlertaBrush" : "DownloadTextBrush");
        if (!semInternet)
        {
            AplicarOpacidade(1, 1); // voltou: opacidade normal já (a intensidade, se ligada, refaz no próximo tick)
        }
    }

    /// <summary>Estado atual do aviso (para quem monta a dica e para as provas).</summary>
    public bool SemInternet => _semInternet;

    // ------------------------------------------------------------------ v1.7.0: mini gráfico

    private bool _miniGrafico;
    private readonly SerieCurta _serieUpload = new();
    private readonly SerieCurta _serieDownload = new();

    /// <summary>
    /// v1.7.0 (Onda 13): liga/desliga o mini gráfico dos últimos 60 s atrás dos números (padrão
    /// desligado). Só existe no layout de duas linhas e no "só download": na linha única não há
    /// altura para um gráfico legível. Desligar apaga o histórico curto (religar começa do zero).
    /// </summary>
    public void DefinirMiniGrafico(bool ligado)
    {
        _miniGrafico = ligado;
        Visibility v = ligado ? Visibility.Visible : Visibility.Collapsed;
        GraficoUpload.Visibility = v;
        GraficoDownload.Visibility = v;
        if (!ligado)
        {
            _serieUpload.Limpar();
            _serieDownload.Limpar();
            GraficoUpload.DefinirPontos([]);
            GraficoDownload.DefinirPontos([]);
        }
    }

    /// <summary>Guarda a amostra e redesenha — só com a opção ligada e o layout de duas linhas na tela.</summary>
    private void RegistrarNoGrafico(double upload, double download)
    {
        if (!_miniGrafico)
        {
            return;
        }

        long agora = Environment.TickCount64; // relógio monotônico: não pula com a hora do Windows
        _serieUpload.Adicionar(agora, upload);
        _serieDownload.Adicionar(agora, download);

        // Na linha única os gráficos não aparecem: guarda as amostras, mas não gasta com desenho
        if (PainelDuasLinhas.Visibility != Visibility.Visible)
        {
            return;
        }

        GraficoDownload.DefinirPontos(_serieDownload.Pontos(agora));
        if (_modo != ModoExibicaoWidget.SoDownload)
        {
            GraficoUpload.DefinirPontos(_serieUpload.Pontos(agora));
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

        DefinirModoExibicao(_modo); // a troca de linhas acima não pode desfazer o "só download"
    }

    // ------------------------------------------------------------------ v1.2.0: modo de exibição

    private ModoExibicaoWidget _modo = ModoExibicaoWidget.DuasLinhas;

    /// <summary>
    /// v1.2.0 (Onda 7): duas linhas (o de sempre), uma linha (o host já entrega o estado compacto)
    /// ou só download — a pílula ↓ ocupa as duas linhas, centralizada, e o ↑ some dos dois layouts.
    /// A LARGURA continua sendo do TaskbarHost (TaskbarLayout com o mesmo modo).
    /// </summary>
    public void DefinirModoExibicao(ModoExibicaoWidget modo)
    {
        _modo = modo;
        bool soDownload = modo == ModoExibicaoWidget.SoDownload;
        Visibility envio = soDownload ? Visibility.Collapsed : Visibility.Visible;
        PilulaUpload.Visibility = envio;
        TextoCompactoUpload.Visibility = envio;
        SeparadorCompactoRede.Visibility = envio;

        if (soDownload)
        {
            Grid.SetRow(PilulaDownload, 0);
            Grid.SetRowSpan(PilulaDownload, 3);
            PilulaDownload.VerticalAlignment = VerticalAlignment.Center;
            PilulaDownload.Height = AlturaPilulaUnica;
        }
        else
        {
            Grid.SetRow(PilulaDownload, _downloadEmCima ? 0 : 2);
            Grid.SetRowSpan(PilulaDownload, 1);
            PilulaDownload.VerticalAlignment = VerticalAlignment.Stretch;
            PilulaDownload.Height = double.NaN;
        }
    }

    /// <summary>Altura da pílula sozinha: a de uma linha do modo normal (42 − 4 − 2) ÷ 2 ≈ 18, +2 de respiro.</summary>
    private const double AlturaPilulaUnica = 20;

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
        RunCompactoCpu.Text = cpu;
        RunCompactoRam.Text = ram;

        // v1.7.1: número + "%" em vermelho a partir de 80 % (o rótulo "CPU"/"RAM" fica neutro)
        bool cpuAlerta = Medicao.MedidorSistema.EmAlerta(cpuPercentual);
        bool ramAlerta = Medicao.MedidorSistema.EmAlerta(ramPercentual);
        PintarAlerta(TextoCpu, cpuAlerta);
        PintarAlerta(TextoRam, ramAlerta);
        PintarAlerta(RunCompactoCpu, cpuAlerta);
        PintarAlerta(RunCompactoRam, ramAlerta);
    }

    /// <summary>
    /// v1.7.1: liga o texto ao pincel de alerta ou ao neutro. Por REFERÊNCIA de recurso (não pelo
    /// pincel em si): se o tema da barra ou a paleta mudarem enquanto está vermelho, a cor nova chega
    /// sozinha. Só troca quando o estado muda, para não refazer a ligação a cada segundo.
    /// </summary>
    private static void PintarAlerta(TextBlock texto, bool alerta)
    {
        // A Tag guarda o último estado aplicado (esses TextBlocks não a usam para mais nada)
        if (texto.Tag is bool anterior && anterior == alerta)
        {
            return;
        }

        texto.SetResourceReference(TextBlock.ForegroundProperty, alerta ? "SistemaAlertaBrush" : "SistemaTextBrush");
        texto.Tag = alerta;
    }

    /// <summary>Mesmo que o de cima, para o trecho (Run) do modo uma linha.</summary>
    private static void PintarAlerta(Run trecho, bool alerta)
    {
        if (trecho.Tag is bool anterior && anterior == alerta)
        {
            return;
        }

        if (alerta)
        {
            trecho.SetResourceReference(TextElement.ForegroundProperty, "SistemaAlertaBrush");
        }
        else
        {
            // Sem cor própria, o trecho herda a cor neutra do TextBlock que o contém
            trecho.ClearValue(TextElement.ForegroundProperty);
        }

        trecho.Tag = alerta;
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
        // v1.7.0: a "borracha" do gráfico é a cor da barra (a mesma para ↑ e ↓)
        GraficoUpload.DefinirCor(cores.MascaraGrafico);
        GraficoDownload.DefinirCor(cores.MascaraGrafico);

        // v0.5.0: CPU/RAM em tom neutro derivado do separador (branco na barra escura, preto na
        // clara) — legível nos dois temas e sem disputar atenção com as cores da paleta.
        Color neutro = Color.FromRgb(cores.Separador.R, cores.Separador.G, cores.Separador.B);
        Resources["SistemaTextBrush"] = CriarBrush(Color.FromArgb(0xE6, neutro.R, neutro.G, neutro.B));
        Resources["SistemaRotuloBrush"] = CriarBrush(Color.FromArgb(0xA6, neutro.R, neutro.G, neutro.B));
        Resources["SistemaPillBgBrush"] = CriarBrush(Color.FromArgb(0x1A, neutro.R, neutro.G, neutro.B));
        Resources["SistemaPillBorderBrush"] = CriarBrush(Color.FromArgb(0x33, neutro.R, neutro.G, neutro.B));
        // v1.7.1: vermelho do alerta ≥ 80 %, já calibrado pelo motor de contraste (opaco, como o texto)
        Resources["SistemaAlertaBrush"] = CriarBrush(cores.Alerta);
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
        RegistrarNoGrafico(0, 0); // v1.7.0: sem placa = nada passando; o gráfico desce ao chão
    }

    /// <summary>Medição pausada pelo menu (v0.3.0): texto explícito em vez de números congelados.</summary>
    public void MostrarPausado()
    {
        // v1.9.0: "pausado"/"paused" conforme o idioma (o App chama de novo quando o idioma muda)
        string pausado = Recursos.Textos.Widget_Pausado;
        TextoUpload.Text = pausado;
        TextoDownload.Text = pausado;
        TextoCompactoUpload.Text = "↑ —";
        TextoCompactoDownload.Text = $"↓ {pausado}";
        AtualizarSistema(null, null); // pausa vale para tudo: nada de CPU/RAM congelados
        // v1.7.0: o tempo pausado não foi medido — apaga o gráfico em vez de ligar os pontos por cima do buraco
        _serieUpload.Limpar();
        _serieDownload.Limpar();
        GraficoUpload.DefinirPontos([]);
        GraficoDownload.DefinirPontos([]);
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
        _dica.IsOpen = false; // clicou: a dica sai da frente do painel/arrasto

        bool comCtrl = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0;
        if (!Sistema.NativeMethods.GetCursorPos(out Sistema.NativeMethods.POINT p))
        {
            return;
        }

        // Só com Ctrl: o clique simples continua abrindo o painel rápido, como sempre
        if (!comCtrl)
        {
            // v1.0.0: duplo clique contado à mão (tempo e distância do Windows) — ver NativeMethods
            _duploPendente = RegraDuploClique.EhDuplo(
                Environment.TickCount64 - _ultimoApertoMs, p.X - _ultimoApertoPx.X, p.Y - _ultimoApertoPx.Y,
                Sistema.NativeMethods.GetDoubleClickTime(),
                Sistema.NativeMethods.GetSystemMetrics(Sistema.NativeMethods.SM_CXDOUBLECLK),
                Sistema.NativeMethods.GetSystemMetrics(Sistema.NativeMethods.SM_CYDOUBLECLK));
            // Depois de um duplo, o próximo aperto recomeça a contagem (3 cliques ≠ 2 duplos)
            _ultimoApertoMs = _duploPendente ? long.MinValue / 2 : Environment.TickCount64;
            _ultimoApertoPx = p;
            return;
        }

        // Cursor em px físicos (o processo é PerMonitorV2): mesma unidade do SetWindowPos do host
        _cursorInicialXPx = p.X;
        _arrastando = true;
        ToolTipService.SetIsEnabled(ContornoHover, false); // nada de dica pulando durante o arrasto
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
        ToolTipService.SetIsEnabled(ContornoHover, true);
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

        // v1.0.0: o 1º clique já abriu o painel (sem atraso — esperar o tempo do duplo deixaria
        // o clique comum lento); o 2º vira "duplo" e o App fecha o painel e abre o Gerenciador
        if (_duploPendente)
        {
            _duploPendente = false;
            DuploClique?.Invoke();
            return;
        }

        CliqueEsquerdo?.Invoke();
    }

    private bool _duploPendente;
    private long _ultimoApertoMs = long.MinValue / 2; // "nunca" sem risco de estouro na subtração
    private Sistema.NativeMethods.POINT _ultimoApertoPx;

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
