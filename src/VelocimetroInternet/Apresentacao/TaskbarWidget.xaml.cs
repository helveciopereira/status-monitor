using System.Windows;
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

        string upCurto = upCompleto.Split(' ')[0];
        TextoCompactoUpload.Text = $"↑ {upCurto}";
        TextoCompactoDownload.Text = $"↓ {downCompleto}";
    }

    /// <summary>
    /// v0.5.0: mostra/esconde a coluna CPU (em cima) / RAM (embaixo) à esquerda do upload/download.
    /// A LARGURA da janela é decidida pelo TaskbarHost (TaskbarLayout); aqui só some o conteúdo.
    /// </summary>
    public void DefinirMostrarSistema(bool mostrar)
    {
        Visibility v = mostrar ? Visibility.Visible : Visibility.Collapsed;
        PilulaCpu.Visibility = v;
        PilulaRam.Visibility = v;
        CompactoSistema.Visibility = v;
        // Coluna com largura fixa: precisa ir a 0, senão sobraria um vão vazio à esquerda
        ColunaSistema.Width = new GridLength(mostrar ? 61 : 0);
        ColunaRespiro.Width = new GridLength(mostrar ? 3 : 0);
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

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
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
