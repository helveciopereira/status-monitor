using System.Globalization;
using System.Net.Http;
using System.Windows;
using VelocimetroInternet.Medicao;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// v1.2.0 (Onda 7): janela do teste de velocidade sob demanda. Uma instância por vez (o App
/// reaproveita); fechar a janela no meio CANCELA o teste — nada continua gastando dados escondido.
/// </summary>
public partial class JanelaTesteVelocidade : Window
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly Func<double> _planoMbps;
    private readonly Action<string>? _log;
    private CancellationTokenSource? _cts;

    /// <param name="planoMbps">Velocidade contratada atual (0 = não informada), lida a cada teste.</param>
    public JanelaTesteVelocidade(Func<double> planoMbps, Action<string>? log = null)
    {
        InitializeComponent();
        _planoMbps = planoMbps;
        _log = log;
        Closing += (_, _) => _cts?.Cancel();
    }

    /// <summary>Teste em andamento (o App não abre outro).</summary>
    public bool Testando => _cts is not null;

    private async void AoClicarIniciar(object sender, RoutedEventArgs e)
    {
        // O mesmo botão vira "Cancelar" durante o teste
        if (_cts is not null)
        {
            _cts.Cancel();
            return;
        }

        _cts = new CancellationTokenSource();
        BotaoIniciar.Content = "Cancelar";
        TextoComparacao.Visibility = TextoDetalhe.Visibility = Visibility.Collapsed;
        NumeroDownload.Text = NumeroUpload.Text = NumeroLatencia.Text = "—";
        DefinirFracao(0);

        var progresso = new Progress<ProgressoTeste>(MostrarProgresso);
        try
        {
            ResultadoTeste r = await new TesteVelocidade().ExecutarAsync(progresso, _cts.Token);
            MostrarResultado(r);
            _log?.Invoke($"Teste de velocidade: ↓ {Mbps(r.DownloadBps)} · ↑ {Mbps(r.UploadBps)} · {r.LatenciaMs:0} ms · {SpeedFormatter.FormatarQuantidade((ulong)r.BytesTotais)} gastos.");
        }
        catch (OperationCanceledException)
        {
            TextoEtapa.Text = "Teste cancelado.";
            DefinirFracao(0);
        }
        catch (HttpRequestException ex)
        {
            // Sem internet, servidor fora do ar, limite de pedidos (429): mensagem curta e útil
            TextoEtapa.Text = ex.StatusCode is { } codigo
                ? $"O servidor de teste recusou o pedido ({(int)codigo}). Tente de novo em alguns minutos."
                : "Não foi possível falar com o servidor de teste. Confira a conexão e tente de novo.";
            DefinirFracao(0);
            _log?.Invoke($"Teste de velocidade falhou: {ex.Message}");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            BotaoIniciar.Content = "Testar de novo";
        }
    }

    private void MostrarProgresso(ProgressoTeste p)
    {
        switch (p.Etapa)
        {
            case EtapaTeste.Latencia:
                TextoEtapa.Text = "Medindo a latência…";
                DefinirFracao(0.02);
                break;
            case EtapaTeste.Download:
                TextoEtapa.Text = "Medindo o download…";
                (NumeroDownload.Text, UnidadeDownload.Text) = Partes(p.BitsPorSegundoParcial);
                DefinirFracao(0.05 + 0.5 * p.Fracao);
                break;
            case EtapaTeste.Upload:
                TextoEtapa.Text = "Medindo o upload…";
                (NumeroUpload.Text, UnidadeUpload.Text) = Partes(p.BitsPorSegundoParcial);
                DefinirFracao(0.55 + 0.45 * p.Fracao);
                break;
        }
    }

    private void MostrarResultado(ResultadoTeste r)
    {
        (NumeroDownload.Text, UnidadeDownload.Text) = Partes(r.DownloadBps);
        (NumeroUpload.Text, UnidadeUpload.Text) = Partes(r.UploadBps);
        NumeroLatencia.Text = r.LatenciaMs.ToString("0", PtBr);
        TextoEtapa.Text = $"Concluído às {DateTime.Now:HH:mm}.";
        DefinirFracao(1);

        string? comparacao = CalculoTeste.ComparacaoComPlano(r.DownloadBps, _planoMbps());
        TextoComparacao.Text = comparacao ?? "Informe a velocidade do seu plano no estúdio (Medição) para comparar com o contratado.";
        TextoComparacao.Visibility = Visibility.Visible;
        TextoDetalhe.Text = $"Servidor {r.Servidor} · dados usados: {SpeedFormatter.FormatarQuantidade((ulong)r.BytesTotais)}";
        TextoDetalhe.Visibility = Visibility.Visible;
    }

    /// <summary>Barra de andamento por colunas proporcionais (certa mesmo antes do 1º layout).</summary>
    private void DefinirFracao(double f)
    {
        f = Math.Clamp(f, 0, 1);
        ColunaFeita.Width = new GridLength(f, GridUnitType.Star);
        ColunaFalta.Width = new GridLength(1 - f, GridUnitType.Star);
    }

    private static (string Numero, string Unidade) Partes(double bps) => CalculoTeste.FormatarMbps(bps);

    private static string Mbps(double bps)
    {
        (string n, string u) = CalculoTeste.FormatarMbps(bps);
        return $"{n} {u}";
    }

    private void AoClicarFechar(object sender, RoutedEventArgs e) => Close();
}
