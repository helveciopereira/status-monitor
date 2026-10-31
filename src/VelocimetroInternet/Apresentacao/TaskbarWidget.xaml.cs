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
    /// Aplica a paleta correspondente ao tema da barra de tarefas do Windows (Escuro vs Claro).
    /// Referência de contraste WCAG: 01-ANALISE.md §4.
    /// </summary>
    public void AplicarTemaBarra(bool temaClaro)
    {
        if (temaClaro)
        {
            Resources["UploadTextBrush"] = CriarBrush("#FF005FB8");
            Resources["DownloadTextBrush"] = CriarBrush("#FFC42B1C");
            Resources["UploadPillBgBrush"] = CriarBrush("#24005FB8");
            Resources["UploadPillBorderBrush"] = CriarBrush("#40005FB8");
            Resources["DownloadPillBgBrush"] = CriarBrush("#24C42B1C");
            Resources["DownloadPillBorderBrush"] = CriarBrush("#40C42B1C");
            Resources["HoverContainerBrush"] = CriarBrush("#0D000000");
        }
        else
        {
            Resources["UploadTextBrush"] = CriarBrush("#FF4CC2FF");
            Resources["DownloadTextBrush"] = CriarBrush("#FFFF6B6B");
            Resources["UploadPillBgBrush"] = CriarBrush("#2E4CC2FF");
            Resources["UploadPillBorderBrush"] = CriarBrush("#484CC2FF");
            Resources["DownloadPillBgBrush"] = CriarBrush("#2EFF6B6B");
            Resources["DownloadPillBorderBrush"] = CriarBrush("#48FF6B6B");
            Resources["HoverContainerBrush"] = CriarBrush("#12FFFFFF");
        }
    }

    private static SolidColorBrush CriarBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
