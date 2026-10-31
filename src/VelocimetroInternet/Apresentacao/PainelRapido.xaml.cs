using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VelocimetroInternet.Config;
using VelocimetroInternet.Medicao;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// Painel rápido (Quick Flyout do Modelo 3). Uma única instância, reaproveitada: abrir = Show,
/// fechar = Hide (abre instantâneo e não recria o XAML a cada clique).
///
/// Comportamento de flyout do Windows: fecha ao perder o foco, com Esc e quando um app entra
/// em tela cheia (o App chama <see cref="Fechar"/>).
/// </summary>
public partial class PainelRapido : Window
{
    private readonly HistoricoRede _historico;
    private readonly SpeedFormatter _formatadorUp = new();
    private readonly SpeedFormatter _formatadorDown = new();

    /// <summary>true enquanto o código ajusta os controles — os eventos de clique não viram gravação.</summary>
    private bool _sincronizando;

    private double _velocidadeContratadaMbps;
    private long _fechadoEmMs;

    public event Action<bool>? UnidadeEscolhida;          // true = bits (Mbps)
    public event Action<bool>? FixarAlterado;
    public event Action<bool>? IniciarComWindowsAlterado;
    public event Action? AbrirEstudioPedido;

    public PainelRapido(HistoricoRede historico)
    {
        InitializeComponent();
        _historico = historico;
        Deactivated += (_, _) => Fechar();
    }

    /// <summary>
    /// true logo depois de fechar. Clicar no widget com o painel aberto faz o painel perder o
    /// foco (fecha) e EM SEGUIDA chega o clique no widget — sem esta janela de 300 ms o
    /// painel reabriria na hora, e "clicar de novo para fechar" nunca funcionaria.
    /// </summary>
    public bool FechadoHaPouco => Environment.TickCount64 - _fechadoEmMs < 300;

    // ------------------------------------------------------------------ estado vindo do App

    public void SincronizarComConfiguracoes(Configuracoes cfg, bool iniciarComWindows)
    {
        _sincronizando = true;
        try
        {
            bool bits = cfg.Unidade == "bits";
            OpcaoMbps.IsChecked = bits;
            OpcaoMBs.IsChecked = !bits;
            ChaveIniciar.IsChecked = iniciarComWindows;
            ChaveFixar.IsChecked = cfg.FixarNaBarra;
            _velocidadeContratadaMbps = cfg.VelocidadeContratadaMbps;

            ModoUnidadeVelocidade modo = bits ? ModoUnidadeVelocidade.BitsPorSegundo : ModoUnidadeVelocidade.BytesPorSegundo;
            _formatadorUp.ModoUnidade = modo;
            _formatadorDown.ModoUnidade = modo;
        }
        finally
        {
            _sincronizando = false;
        }

        if (IsVisible)
        {
            Preencher();
        }
    }

    /// <summary>
    /// Pinta Envio/Download com a paleta ativa, resolvida para o tema dos APPS (o painel é uma
    /// janela de app; o widget segue o tema da barra). O motor WCAG garante o contraste.
    /// </summary>
    public void AplicarPaleta(PaletaCores paleta)
    {
        bool claro = ServicoTema.ClaroAtivo;
        CoresResolvidas c = Paletas.Resolver(paleta, claro);
        Resources["Painel.Upload"] = Pincel(c.Upload);
        Resources["Painel.Download"] = Pincel(c.Download);
        Resources["Painel.UploadVeu"] = Pincel(Paletas.ComAlfa(c.Upload, 0x1A));
        Resources["Painel.DownloadVeu"] = Pincel(Paletas.ComAlfa(c.Download, 0x1A));
        Resources["Painel.UploadBorda"] = Pincel(Paletas.ComAlfa(c.Upload, 0x40));
        Resources["Painel.DownloadBorda"] = Pincel(Paletas.ComAlfa(c.Download, 0x40));
        Resources["Painel.Trilho"] = Pincel(claro ? Color.FromArgb(0x1A, 0, 0, 0) : Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
    }

    // ------------------------------------------------------------------ abrir / fechar

    /// <summary>
    /// Abre acima do widget (alinhado à esquerda dele, como o Clima) ou, vindo da bandeja,
    /// acima do ponteiro. Sempre dentro da área de trabalho do monitor da barra.
    /// </summary>
    public void AbrirAncorado(PosicaoWidgetBarra posWidget, nint hwndWidget, bool veioDaBandeja)
    {
        uint dpi = NativeMethods.GetDpiForWindow(hwndWidget);
        double escala = (dpi == 0 ? 96 : dpi) / 96.0;

        // Altura real antes de mostrar (SizeToContent): mede o conteúdo na largura fixa
        Moldura.Measure(new Size(Width, double.PositiveInfinity));
        double alturaPx = Moldura.DesiredSize.Height * escala;
        double larguraPx = Width * escala;
        double margemPx = 12 * escala;

        NativeMethods.GetCursorPos(out NativeMethods.POINT cursor);
        nint monitor = veioDaBandeja
            ? NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST)
            : NativeMethods.MonitorFromWindow(hwndWidget, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfoW(monitor, ref info);
        NativeMethods.RECT area = info.rcWork;

        double xPx = veioDaBandeja || posWidget.LarguraFisicaPx == 0
            ? cursor.X - larguraPx / 2
            : posWidget.XTelaPx;
        xPx = Math.Clamp(xPx, area.Left + margemPx, area.Right - margemPx - larguraPx);
        double yPx = area.Bottom - margemPx - alturaPx;

        Left = xPx / escala;
        Top = Math.Max(area.Top, yPx) / escala;

        Show();
        Preencher(); // já com layout (larguras reais das barras e do gráfico)
        Activate();
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        AnimarEntrada();
    }

    public void Fechar()
    {
        if (!IsVisible)
        {
            return;
        }

        _fechadoEmMs = Environment.TickCount64;
        Hide();
    }

    /// <summary>
    /// Entrada curta (160 ms): sobe 12 px e aparece, como os flyouts do Windows 11.
    /// Desligada quando o usuário desativou as animações do Windows.
    /// </summary>
    private void AnimarEntrada()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            Deslize.Y = 0;
            Moldura.Opacity = 1;
            return;
        }

        var suave = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duracao = TimeSpan.FromMilliseconds(160);
        Deslize.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, duracao) { EasingFunction = suave });
        Moldura.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duracao) { EasingFunction = suave });
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Fechar();
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ dados ao vivo

    /// <summary>Chamado a cada tick (thread de UI). Só trabalha com o painel aberto.</summary>
    public void AtualizarAoVivo(bool semConexao)
    {
        _semConexao = semConexao;
        if (IsVisible)
        {
            Preencher();
        }
    }

    private bool _semConexao;

    /// <summary>Preenche todos os textos/barras/gráfico com o instantâneo atual do histórico.</summary>
    private void Preencher()
    {
        bool semConexao = _semConexao;
        InstantaneoHistorico h = _historico.Capturar();

        if (semConexao)
        {
            TextoStatus.Text = "Sem conexão";
            TextoStatus.Foreground = (Brush)FindResource("Tema.TextoSecundario");
            PontoAoVivo.Fill = (Brush)FindResource("Tema.TextoSecundario");
        }
        else
        {
            TextoStatus.Text = string.IsNullOrEmpty(h.NomeInterface) ? "Ao vivo" : $"Ao vivo · {h.NomeInterface}";
            TextoStatus.SetResourceReference(TextBlock.ForegroundProperty, "Tema.AoVivo");
            PontoAoVivo.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Tema.AoVivo");
        }

        (string nUp, string uUp) = SpeedFormatter.SepararNumeroEUnidade(_formatadorUp.FormatarValorEUnidade(h.UploadAtual));
        (string nDown, string uDown) = SpeedFormatter.SepararNumeroEUnidade(_formatadorDown.FormatarValorEUnidade(h.DownloadAtual));
        NumeroUpload.Text = nUp;
        UnidadeUpload.Text = uUp;
        NumeroDownload.Text = nDown;
        UnidadeDownload.Text = uDown;

        // Pico sem histerese (formatador novo): é um valor estático, não pisca
        var fPico = new SpeedFormatter(_formatadorUp.ModoUnidade);
        TextoPicoUpload.Text = $"Pico: {fPico.FormatarValorEUnidade(h.PicoUpload)}";
        TextoPicoDownload.Text = $"Pico: {new SpeedFormatter(fPico.ModoUnidade).FormatarValorEUnidade(h.PicoDownload)}";

        // Barras: fração da velocidade contratada; sem ela, da velocidade de link da placa
        double referenciaBps = _velocidadeContratadaMbps > 0
            ? _velocidadeContratadaMbps * 1_000_000 / 8
            : h.VelocidadeLinkBps / 8.0;
        AjustarBarra(BarraUpload, TrilhoUpload, h.UploadAtual, referenciaBps);
        AjustarBarra(BarraDownload, TrilhoDownload, h.DownloadAtual, referenciaBps);

        TextoLink.Text = _velocidadeContratadaMbps > 0
            ? $"Plano de {_velocidadeContratadaMbps:0} Mbps"
            : h.VelocidadeLinkBps > 0 ? $"Link de {SpeedFormatter.FormatarLink(h.VelocidadeLinkBps)}" : string.Empty;

        ConsumoUpload.Text = $"↑ {SpeedFormatter.FormatarQuantidade(h.BytesEnviadosHoje)}";
        ConsumoDownload.Text = $"↓ {SpeedFormatter.FormatarQuantidade(h.BytesRecebidosHoje)}";

        DesenharGrafico(h);
    }

    private static void AjustarBarra(FrameworkElement barra, FrameworkElement trilho, double atualBps, double referenciaBps)
    {
        double fracao = referenciaBps > 0 ? Math.Clamp(atualBps / referenciaBps, 0, 1) : 0;
        // Mínimo de 3 px quando há tráfego: "tem algo passando" fica visível mesmo num link de 1 Gbps
        double largura = trilho.ActualWidth * fracao;
        barra.Width = atualBps > 1024 ? Math.Max(3, largura) : largura;
    }

    private InstantaneoHistorico? _ultimoDesenho;

    private void AoMudarTamanhoGrafico(object sender, SizeChangedEventArgs e)
    {
        if (_ultimoDesenho is not null)
        {
            DesenharGrafico(_ultimoDesenho);
        }
    }

    /// <summary>
    /// Áreas sobrepostas (download atrás, upload na frente) numa escala comum = maior valor da
    /// janela. As 60 posições ocupam a largura toda; com menos amostras, encostam à direita
    /// (o "agora" fica sempre na borda direita).
    /// </summary>
    private void DesenharGrafico(InstantaneoHistorico h)
    {
        _ultimoDesenho = h;
        double largura = Grafico.ActualWidth;
        double altura = Grafico.ActualHeight;
        if (largura <= 0 || altura <= 0 || h.Pontos.Count < 2)
        {
            AreaDownload.Data = LinhaDownload.Data = AreaUpload.Data = LinhaUpload.Data = null;
            TextoEscala.Text = "Aguardando amostras…";
            return;
        }

        double maximo = Math.Max(1024, Math.Max(h.PicoUpload, h.PicoDownload)) * 1.1;
        double passo = largura / (HistoricoRede.JanelaSegundos - 1);
        double x0 = largura - passo * (h.Pontos.Count - 1);

        (Geometry areaD, Geometry linhaD) = MontarSerie(h.Pontos, p => p.DownloadBps, x0, passo, altura, maximo);
        (Geometry areaU, Geometry linhaU) = MontarSerie(h.Pontos, p => p.UploadBps, x0, passo, altura, maximo);
        AreaDownload.Data = areaD;
        LinhaDownload.Data = linhaD;
        AreaUpload.Data = areaU;
        LinhaUpload.Data = linhaU;

        var f = new SpeedFormatter(_formatadorUp.ModoUnidade);
        TextoEscala.Text = $"Topo do gráfico: {f.FormatarValorEUnidade(maximo / 1.1)}";
    }

    private static (Geometry Area, Geometry Linha) MontarSerie(
        IReadOnlyList<PontoHistorico> pontos, Func<PontoHistorico, double> valor,
        double x0, double passo, double altura, double maximo)
    {
        var linha = new StreamGeometry();
        var area = new StreamGeometry();
        using (StreamGeometryContext cl = linha.Open())
        using (StreamGeometryContext ca = area.Open())
        {
            for (int i = 0; i < pontos.Count; i++)
            {
                var p = new Point(x0 + passo * i, altura - Math.Min(1, valor(pontos[i]) / maximo) * (altura - 2));
                if (i == 0)
                {
                    cl.BeginFigure(p, isFilled: false, isClosed: false);
                    ca.BeginFigure(new Point(p.X, altura), isFilled: true, isClosed: true);
                    ca.LineTo(p, isStroked: false, isSmoothJoin: false);
                }
                else
                {
                    cl.LineTo(p, isStroked: true, isSmoothJoin: true);
                    ca.LineTo(p, isStroked: false, isSmoothJoin: false);
                }
            }

            ca.LineTo(new Point(x0 + passo * (pontos.Count - 1), altura), isStroked: false, isSmoothJoin: false);
        }

        linha.Freeze();
        area.Freeze();
        return (area, linha);
    }

    // ------------------------------------------------------------------ cliques

    private void AoClicarFechar(object sender, RoutedEventArgs e) => Fechar();

    private void AoEscolherUnidade(object sender, RoutedEventArgs e)
    {
        if (_sincronizando)
        {
            return;
        }

        UnidadeEscolhida?.Invoke(ReferenceEquals(sender, OpcaoMbps));
    }

    private void AoAlternarIniciar(object sender, RoutedEventArgs e)
    {
        if (!_sincronizando)
        {
            IniciarComWindowsAlterado?.Invoke(ChaveIniciar.IsChecked == true);
        }
    }

    private void AoAlternarFixar(object sender, RoutedEventArgs e)
    {
        if (!_sincronizando)
        {
            FixarAlterado?.Invoke(ChaveFixar.IsChecked == true);
        }
    }

    private void AoClicarEstudio(object sender, RoutedEventArgs e) => AbrirEstudioPedido?.Invoke();

    private static SolidColorBrush Pincel(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
