using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using VelocimetroInternet.Medicao;
using VelocimetroInternet.Recursos;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// v1.4.0 (Onda 9): janela do relatório para a operadora — escolhe o mês (só os que têm dados),
/// mostra o resumo e gera o PDF (pergunta onde salvar e abre no leitor padrão).
/// </summary>
public partial class JanelaRelatorio : Window
{
    /// <summary>v1.9.0: números da JANELA no idioma da interface (o PDF/CSV continuam em pt-BR, dentro do CalculoRelatorio).</summary>
    private static CultureInfo Cultura => Idioma.Cultura;

    private readonly Func<int, int, ResumoMes> _resumir;
    private readonly string _versaoApp;
    private readonly Action<string>? _log;
    private readonly List<(int Ano, int Mes)> _meses;
    private int _indice; // 0 = mês mais recente
    private ResumoMes? _atual;

    /// <param name="resumir">Resumo de (ano, mês) com o plano ATUAL das configurações.</param>
    /// <param name="meses">Meses com dados, o mais recente primeiro (o atual sempre entra).</param>
    public JanelaRelatorio(Func<int, int, ResumoMes> resumir, List<(int Ano, int Mes)> meses, string versaoApp, Action<string>? log = null)
    {
        InitializeComponent();
        _resumir = resumir;
        _meses = meses.Count > 0 ? meses : [(DateTime.Now.Year, DateTime.Now.Month)];
        _versaoApp = versaoApp;
        _log = log;
        Atualizar();
    }

    /// <summary>Recalcula e mostra o mês escolhido (também usado depois de um teste novo).</summary>
    public void Atualizar()
    {
        (int ano, int mes) = _meses[_indice];
        _atual = _resumir(ano, mes);
        ResumoMes r = _atual;

        // v1.9.0: o nome do mês segue o idioma da interface ("Novembro de 2026" / "November 2026")
        string nomeMes = CalculoRelatorio.NomeMes(r.Ano, r.Mes, Cultura);
        TextoMes.Text = char.ToUpper(nomeMes[0], Cultura) + nomeMes[1..];
        // Setas: "anterior" = mais antigo (índice maior)
        BotaoAnterior.IsEnabled = _indice < _meses.Count - 1;
        BotaoProximo.IsEnabled = _indice > 0;

        // Número grande + unidade pequena: "492 Mbps" inteiro cabe no cartão
        (NumeroMedia.Text, UnidadeMedia.Text) = r.MediaDownloadBps is double md ? Partes(md) : ("—", "");
        SubMedia.Text = r.FracaoMedia is double f ? Textos.Formatar(Textos.Relatorio_DoPlano, DicaWidget.TextoPercentual(f))
            : r.Testes.Count == 0 ? Textos.Relatorio_SemTestes : Textos.Relatorio_SemPlano;
        // Sem cor de alerta aqui: a janela segue o tema claro/escuro e um vermelho fixo ficaria
        // ilegível num deles; o percentual e as frases da conclusão já dizem se ficou abaixo

        NumeroTestes.Text = r.Testes.Count.ToString(Cultura);
        SubTestes.Text = r.PlanoMbps > 0
            ? Textos.Formatar(Textos.Relatorio_AbaixoDe40, r.TestesAbaixoDoMinimo)
            : Textos.Formatar(Textos.Relatorio_PorWiFi, r.TestesPorWiFi);

        NumeroQuedas.Text = r.QuedasInternet.Count.ToString(Cultura);
        SubQuedas.Text = Textos.Formatar(Textos.Relatorio_Total, CalculoRelatorio.Duracao(r.TempoSemInternet));

        // Disponibilidade com 1 casa, arredondada para baixo (99,96 % não vira "100,0 %")
        NumeroDisponivel.Text = r.Disponibilidade is double d ? DicaWidget.TextoPercentual(d, Cultura, casas: 1) : "—";
        SubDisponivel.Text = r.TempoObservado > TimeSpan.Zero
            ? Textos.Formatar(Textos.Relatorio_Em, CalculoRelatorio.Duracao(r.TempoObservado))
            : Textos.Relatorio_SemObservacao;

        // As conclusões são as frases do PDF (documento para a operadora): ficam em português
        ListaConclusoes.ItemsSource = r.Conclusoes;
        TextoPlano.Text = r.PlanoMbps > 0
            ? Textos.Formatar(Textos.Relatorio_PlanoInformado, r.PlanoMbps.ToString("0.##", Cultura))
            : Textos.Relatorio_PlanoNaoInformado;
        TextoEstado.Visibility = Visibility.Collapsed;
    }

    /// <summary>Gera os bytes do PDF do mês mostrado (público para o render de conferência).</summary>
    public byte[] GerarPdf(DateTime geradoEm)
    {
        ResumoMes r = _atual ?? throw new InvalidOperationException("Nenhum mês carregado.");
        List<PaginaRgb> paginas = RelatorioPaginas.Desenhar(r, geradoEm, _versaoApp);
        return PdfDeImagens.Gerar(paginas, $"Relatório de qualidade da internet — {r.NomeMes}");
    }

    private void AoClicarGerar(object sender, RoutedEventArgs e)
    {
        if (_atual is null)
        {
            return;
        }

        var dialogo = new SaveFileDialog
        {
            Title = Textos.Relatorio_SalvarRelatorio,
            Filter = Textos.Relatorio_FiltroPdf,
            FileName = Textos.Formatar(Textos.Relatorio_NomeArquivo, $"{_atual.Ano}-{_atual.Mes:00}") + ".pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialogo.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            File.WriteAllBytes(dialogo.FileName, GerarPdf(DateTime.Now));
            TextoEstado.Text = Textos.Formatar(Textos.Relatorio_PdfSalvoEm, dialogo.FileName);
            TextoEstado.Visibility = Visibility.Visible;
            _log?.Invoke($"Relatório da operadora gerado: {dialogo.FileName}");
            // Abre no leitor padrão (Edge, Acrobat…) para conferir/imprimir/anexar
            Process.Start(new ProcessStartInfo(dialogo.FileName) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Arquivo aberto em outro programa, pasta sem permissão, nenhum leitor de PDF: diz o que houve
            TextoEstado.Text = Textos.Formatar(Textos.Relatorio_PdfFalhou, ex.Message);
            TextoEstado.Visibility = Visibility.Visible;
            _log?.Invoke($"Relatório da operadora falhou: {ex.Message}");
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    /// <summary>
    /// v1.8.1 (Onda 14): testes e quedas do mês em CSV (";" e vírgula decimal, como o CSV de consumo).
    /// UTF-8 COM BOM: sem ele o Excel abre "Latência"/"Início" com os acentos quebrados.
    /// </summary>
    private void AoClicarCsv(object sender, RoutedEventArgs e)
    {
        if (_atual is null)
        {
            return;
        }

        var dialogo = new SaveFileDialog
        {
            Title = Textos.Relatorio_ExportarTestesEQuedas,
            Filter = Textos.Comum_FiltroCsv,
            FileName = Textos.Formatar(Textos.Relatorio_NomeArquivo, $"{_atual.Ano}-{_atual.Mes:00}") + ".csv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialogo.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialogo.FileName, CalculoRelatorio.ParaCsv(_atual), new System.Text.UTF8Encoding(true));
            TextoEstado.Text = Textos.Formatar(Textos.Relatorio_CsvSalvoEm, dialogo.FileName, _atual.Testes.Count, _atual.QuedasInternet.Count + _atual.QuedasRede.Count);
            TextoEstado.Visibility = Visibility.Visible;
            _log?.Invoke($"CSV do relatório gerado: {dialogo.FileName}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TextoEstado.Text = Textos.Formatar(Textos.Relatorio_CsvFalhou, ex.Message);
            TextoEstado.Visibility = Visibility.Visible;
            _log?.Invoke($"CSV do relatório falhou: {ex.Message}");
        }
    }

    private void AoClicarAnterior(object sender, RoutedEventArgs e)
    {
        if (_indice < _meses.Count - 1)
        {
            _indice++;
            Atualizar();
        }
    }

    private void AoClicarProximo(object sender, RoutedEventArgs e)
    {
        if (_indice > 0)
        {
            _indice--;
            Atualizar();
        }
    }

    private static (string, string) Partes(double bps)
    {
        (string n, string u) = CalculoTeste.FormatarMbps(bps);
        return (n, " " + u);
    }

    private void AoClicarFechar(object sender, RoutedEventArgs e) => Close();
}
