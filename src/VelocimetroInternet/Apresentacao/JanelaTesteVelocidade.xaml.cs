using System.Globalization;
using System.Net.Http;
using System.Windows;
using VelocimetroInternet.Medicao;
using VelocimetroInternet.Recursos;
using VelocimetroInternet.Sistema;

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
    private readonly Action<ResultadoTeste, ConexaoNoTeste?>? _aoConcluir;
    private readonly Func<ConexaoNoTeste>? _lerConexao;
    private CancellationTokenSource? _cts;

    /// <summary>v1.5.0: link/sinal lidos durante o teste atual (null até a leitura).</summary>
    private ConexaoNoTeste? _conexao;

    /// <param name="planoMbps">Velocidade contratada atual (0 = não informada), lida a cada teste.</param>
    /// <param name="aoConcluir">v1.4.0: guarda o resultado para o relatório do mês (só testes concluídos); v1.5.0: com a conexão.</param>
    /// <param name="lerConexao">v1.5.0 (Onda 10): lê o link/sinal do PC com o roteador (1× por teste).</param>
    public JanelaTesteVelocidade(
        Func<double> planoMbps, Action<string>? log = null, Action<ResultadoTeste, ConexaoNoTeste?>? aoConcluir = null,
        Func<ConexaoNoTeste>? lerConexao = null)
    {
        InitializeComponent();
        _planoMbps = planoMbps;
        _log = log;
        _aoConcluir = aoConcluir;
        _lerConexao = lerConexao;
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
        BotaoIniciar.Content = Textos.Teste_Cancelar;
        TextoComparacao.Visibility = TextoDetalhe.Visibility = TextoConexao.Visibility = Visibility.Collapsed;
        _conexao = null;
        NumeroDownload.Text = NumeroUpload.Text = NumeroLatencia.Text = "—";
        DefinirFracao(0);

        var progresso = new Progress<ProgressoTeste>(MostrarProgresso);
        try
        {
            var teste = new TesteVelocidade(); // v1.6.0: servidor automático (fast.com; Cloudflare de reserva)
            ResultadoTeste r = await teste.ExecutarAsync(progresso, _cts.Token);
            if (teste.MotivoDaReserva is { } motivo)
            {
                _log?.Invoke($"Teste de velocidade pela Cloudflare (reserva): {motivo}.");
            }

            _conexao ??= LerConexao(); // upload pulado/rápido demais: lê agora
            MostrarResultado(r);
            _aoConcluir?.Invoke(r, _conexao);
            _log?.Invoke($"Teste de velocidade: ↓ {Mbps(r.DownloadBps)} · ↑ {Mbps(r.UploadBps)} · {r.LatenciaMs:0} ms · {SpeedFormatter.FormatarQuantidade((ulong)r.BytesTotais)} gastos" +
                (_conexao is null ? "." : $" · {_conexao.Descricao(PtBr)}.")); // log sempre em pt-BR
        }
        catch (OperationCanceledException)
        {
            TextoEtapa.Text = Textos.Teste_Cancelado;
            DefinirFracao(0);
        }
        catch (HttpRequestException ex)
        {
            // Sem internet, servidor fora do ar, limite de pedidos (429): mensagem curta e útil
            TextoEtapa.Text = ex.StatusCode is { } codigo
                ? Textos.Formatar(Textos.Teste_ServidorRecusou, (int)codigo)
                : Textos.Teste_ServidorInacessivel;
            DefinirFracao(0);
            _log?.Invoke($"Teste de velocidade falhou: {ex.Message}");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            BotaoIniciar.Content = Textos.Teste_TestarDeNovo;
        }
    }

    private void MostrarProgresso(ProgressoTeste p)
    {
        switch (p.Etapa)
        {
            case EtapaTeste.Latencia:
                TextoEtapa.Text = Textos.Teste_MedindoLatencia;
                DefinirFracao(0.02);
                break;
            case EtapaTeste.Download:
                TextoEtapa.Text = Textos.Teste_MedindoDownload;
                (NumeroDownload.Text, UnidadeDownload.Text) = Partes(p.BitsPorSegundoParcial);
                DefinirFracao(0.05 + 0.5 * p.Fracao);
                break;
            case EtapaTeste.Upload:
                // v1.5.0: o download acabou de puxar o link ao máximo — é a hora certa de ler a taxa
                // do Wi-Fi (parado, ele economiza energia e mostra menos que o real)
                _conexao ??= LerConexao();
                TextoEtapa.Text = Textos.Teste_MedindoUpload;
                (NumeroUpload.Text, UnidadeUpload.Text) = Partes(p.BitsPorSegundoParcial);
                DefinirFracao(0.55 + 0.45 * p.Fracao);
                break;
        }
    }

    private void MostrarResultado(ResultadoTeste r)
    {
        (NumeroDownload.Text, UnidadeDownload.Text) = Partes(r.DownloadBps);
        (NumeroUpload.Text, UnidadeUpload.Text) = Partes(r.UploadBps);
        // v1.9.0: textos e números no idioma da interface
        NumeroLatencia.Text = r.LatenciaMs.ToString("0", Idioma.Cultura);
        TextoEtapa.Text = Textos.Formatar(Textos.Teste_ConcluidoAs, DateTime.Now.ToString("HH:mm"));
        DefinirFracao(1);

        string? comparacao = CalculoTeste.ComparacaoComPlano(r.DownloadBps, _planoMbps());
        TextoComparacao.Text = comparacao ?? Textos.Teste_InformePlano;
        TextoComparacao.Visibility = Visibility.Visible;
        TextoDetalhe.Text = Textos.Formatar(Textos.Teste_ServidorDados, r.Servidor, SpeedFormatter.FormatarQuantidade((ulong)r.BytesTotais)) +
            (_aoConcluir is null ? "" : Textos.Teste_GuardadoNoRelatorio);
        TextoDetalhe.Visibility = Visibility.Visible;

        // v1.5.0 (Onda 10): de onde o teste saiu. Se o link é mais lento que o plano, diz com todas as
        // letras que o limite é do Wi-Fi/cabo — senão o usuário culpa a operadora (e a operadora descarta)
        if (_conexao is not null)
        {
            string? aviso = _conexao.AvisoDeLimite(_planoMbps());
            TextoConexao.Text = Textos.Formatar(Textos.Teste_Conexao, _conexao.Descricao()) + (aviso is null ? "" : $" {aviso}");
            // Referência dinâmica: troca de tema claro/escuro com a janela aberta continua certa
            TextoConexao.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, aviso is null ? "Tema.TextoSecundario" : "Tema.Texto");
            TextoConexao.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Lê a conexão sem nunca derrubar o teste (a leitura é extra; o resultado vale sem ela).</summary>
    private ConexaoNoTeste? LerConexao()
    {
        try
        {
            return _lerConexao?.Invoke();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            _log?.Invoke($"Leitura da conexão do teste falhou: {ex.Message}");
            return null;
        }
    }

    /// <summary>Barra de andamento por colunas proporcionais (certa mesmo antes do 1º layout).</summary>
    private void DefinirFracao(double f)
    {
        f = Math.Clamp(f, 0, 1);
        ColunaFeita.Width = new GridLength(f, GridUnitType.Star);
        ColunaFalta.Width = new GridLength(1 - f, GridUnitType.Star);
    }

    /// <summary>Para a tela: no idioma da interface.</summary>
    private static (string Numero, string Unidade) Partes(double bps) => CalculoTeste.FormatarMbps(bps);

    /// <summary>Para o log: sempre pt-BR.</summary>
    private static string Mbps(double bps)
    {
        (string n, string u) = CalculoTeste.FormatarMbps(bps, PtBr);
        return $"{n} {u}";
    }

    private void AoClicarFechar(object sender, RoutedEventArgs e) => Close();
}
