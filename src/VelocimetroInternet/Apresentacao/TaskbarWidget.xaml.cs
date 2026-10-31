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
