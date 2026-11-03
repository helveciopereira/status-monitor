using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using VelocimetroInternet.Medicao;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// v1.4.0 (Onda 9): janela do relatório para a operadora — escolhe o mês (só os que têm dados),
/// mostra o resumo e gera o PDF (pergunta onde salvar e abre no leitor padrão).
/// </summary>
public partial class JanelaRelatorio : Window
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

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

        TextoMes.Text = char.ToUpper(r.NomeMes[0], PtBr) + r.NomeMes[1..];
        // Setas: "anterior" = mais antigo (índice maior)
        BotaoAnterior.IsEnabled = _indice < _meses.Count - 1;
        BotaoProximo.IsEnabled = _indice > 0;

        // Número grande + unidade pequena: "492 Mbps" inteiro cabe no cartão
        (NumeroMedia.Text, UnidadeMedia.Text) = r.MediaDownloadBps is double md ? Partes(md) : ("—", "");
        SubMedia.Text = r.FracaoMedia is double f ? $"{CalculoRelatorio.Pct(f)} do plano"
            : r.Testes.Count == 0 ? "sem testes" : "sem plano";
        // Sem cor de alerta aqui: a janela segue o tema claro/escuro e um vermelho fixo ficaria
        // ilegível num deles; o percentual e as frases da conclusão já dizem se ficou abaixo

        NumeroTestes.Text = r.Testes.Count.ToString(PtBr);
        SubTestes.Text = r.PlanoMbps > 0 ? $"{r.TestesAbaixoDoMinimo} abaixo de 40 %" : $"{r.TestesPorWiFi} por Wi-Fi";

        NumeroQuedas.Text = r.QuedasInternet.Count.ToString(PtBr);
        SubQuedas.Text = $"total {CalculoRelatorio.Duracao(r.TempoSemInternet)}";

        NumeroDisponivel.Text = r.Disponibilidade is double d ? $"{(Math.Floor(d * 1000) / 10).ToString("0.0", PtBr)} %" : "—";
        SubDisponivel.Text = r.TempoObservado > TimeSpan.Zero ? $"em {CalculoRelatorio.Duracao(r.TempoObservado)}" : "sem observação";

        ListaConclusoes.ItemsSource = r.Conclusoes;
        TextoPlano.Text = r.PlanoMbps > 0
            ? $"Plano: {r.PlanoMbps.ToString("0.##", PtBr)} Mbps · para mudar: estúdio › Medição"
            : "Plano não informado: estúdio › Medição";
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
            Title = "Salvar relatório",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"Relatório internet {_atual.Ano}-{_atual.Mes:00}.pdf",
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
            TextoEstado.Text = $"PDF salvo em {dialogo.FileName}.";
            TextoEstado.Visibility = Visibility.Visible;
            _log?.Invoke($"Relatório da operadora gerado: {dialogo.FileName}");
            // Abre no leitor padrão (Edge, Acrobat…) para conferir/imprimir/anexar
            Process.Start(new ProcessStartInfo(dialogo.FileName) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Arquivo aberto em outro programa, pasta sem permissão, nenhum leitor de PDF: diz o que houve
            TextoEstado.Text = $"Não foi possível salvar/abrir o PDF: {ex.Message}";
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
            Title = "Exportar testes e quedas",
            Filter = "Planilha CSV (*.csv)|*.csv",
            FileName = $"Relatório internet {_atual.Ano}-{_atual.Mes:00}.csv",
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
            TextoEstado.Text = $"CSV salvo em {dialogo.FileName} ({_atual.Testes.Count} teste(s), {_atual.QuedasInternet.Count + _atual.QuedasRede.Count} queda(s)).";
            TextoEstado.Visibility = Visibility.Visible;
            _log?.Invoke($"CSV do relatório gerado: {dialogo.FileName}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TextoEstado.Text = $"Não foi possível salvar o CSV: {ex.Message}";
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
