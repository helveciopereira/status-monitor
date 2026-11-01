using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;

namespace VelocimetroInternet.Medicao;

/// <summary>Etapa do teste, para a tela mostrar o andamento.</summary>
public enum EtapaTeste
{
    Latencia,
    Download,
    Upload,
    Concluido
}

/// <summary>Andamento: etapa atual e a velocidade parcial dela (bits/s; 0 na etapa de latência).</summary>
public readonly record struct ProgressoTeste(EtapaTeste Etapa, double BitsPorSegundoParcial, double Fracao);

/// <summary>Resultado final. Velocidades em BITS por segundo (como a operadora vende).</summary>
public sealed record ResultadoTeste(
    double LatenciaMs, double DownloadBps, double UploadBps,
    long BytesBaixados, long BytesEnviados, string Servidor)
{
    /// <summary>Dados gastos pelo teste (vale contra a franquia).</summary>
    public long BytesTotais => BytesBaixados + BytesEnviados;
}

/// <summary>
/// v1.2.0 (Onda 7): teste de velocidade SOB DEMANDA — só roda quando o usuário clica (nunca em
/// segundo plano: consumiria a banda que o app mede e a franquia de quem tem limite).
///
/// Servidor: speed.cloudflare.com (o mesmo do teste do navegador da Cloudflare): GET
/// <c>/__down?bytes=N</c> devolve N bytes; POST <c>/__up</c> aceita o corpo e descarta. Sem
/// conta, sem chave, sem rastrear o usuário.
///
/// Método (o mesmo dos testes de navegador): latência = mediana de várias requisições vazias;
/// download/upload = várias conexões em paralelo por um tempo fixo, com TETO de dados (internet
/// rápida termina pelo teto, lenta pelo tempo). O 1º trecho de cada etapa é aquecimento e não
/// entra na conta (a janela do TCP ainda está abrindo).
/// </summary>
public sealed class TesteVelocidade
{
    public const string ServidorPadrao = "https://speed.cloudflare.com";

    /// <summary>Limites de uma rodada. Os padrões gastam no máximo ~130 MB.</summary>
    public sealed record Limites(
        int Conexoes = 4,
        double SegundosPorEtapa = 10,
        long TetoDownloadBytes = 100_000_000,
        long TetoUploadBytes = 30_000_000,
        int AmostrasLatencia = 7,
        double SegundosAquecimento = 1.0);

    private readonly HttpClient _http;
    private readonly string _servidor;
    private readonly Limites _limites;

    public TesteVelocidade(HttpClient? http = null, string servidor = ServidorPadrao, Limites? limites = null)
    {
        _http = http ?? CriarCliente();
        _servidor = servidor.TrimEnd('/');
        _limites = limites ?? new Limites();
    }

    private static HttpClient CriarCliente()
    {
        var c = new HttpClient(new SocketsHttpHandler
        {
            // Uma conexão por requisição em paralelo (o padrão já permite; explícito por clareza)
            MaxConnectionsPerServer = 16,
            AutomaticDecompression = DecompressionMethods.None // medir bytes reais, sem gzip
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("StatusMonitor-TesteVelocidade/1.2");
        return c;
    }

    public async Task<ResultadoTeste> ExecutarAsync(IProgress<ProgressoTeste>? progresso, CancellationToken ct)
    {
        progresso?.Report(new ProgressoTeste(EtapaTeste.Latencia, 0, 0));
        double latencia = await MedirLatenciaAsync(ct).ConfigureAwait(false);

        (double down, long baixados) = await MedirAsync(EtapaTeste.Download, progresso, ct).ConfigureAwait(false);
        (double up, long enviados) = await MedirAsync(EtapaTeste.Upload, progresso, ct).ConfigureAwait(false);

        progresso?.Report(new ProgressoTeste(EtapaTeste.Concluido, 0, 1));
        return new ResultadoTeste(latencia, down, up, baixados, enviados, new Uri(_servidor).Host);
    }

    // ------------------------------------------------------------------ latência

    private async Task<double> MedirLatenciaAsync(CancellationToken ct)
    {
        var tempos = new List<double>(_limites.AmostrasLatencia);
        for (int i = 0; i < _limites.AmostrasLatencia; i++)
        {
            var sw = Stopwatch.StartNew();
            using HttpResponseMessage r = await _http.GetAsync(
                $"{_servidor}/__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            r.EnsureSuccessStatusCode();
            sw.Stop();
            // A 1ª inclui DNS + TLS: fica de fora (não é a latência da linha)
            if (i > 0)
            {
                tempos.Add(sw.Elapsed.TotalMilliseconds);
            }
        }

        return CalculoTeste.Mediana(tempos);
    }

    // ------------------------------------------------------------------ download / upload

    /// <summary>
    /// v1.2.1: tamanho MÁXIMO de cada pedido. O speed.cloudflare.com recusa (403) alguns tamanhos
    /// acima de ~10 MB de forma irregular (medido em 2026-11-01: 11, 12, 12,5, 15 e 16 MB → 403;
    /// 20, 25 e 30 MB → 200; até 10 MB → sempre 200). Na v1.2.0 cada pedido tinha 12,5 MB e o
    /// download inteiro dava 0. Pedidos de até 8 MB ficam com folga dentro da faixa sempre aceita.
    /// </summary>
    public const long TamanhoMaximoPedido = 8_000_000;

    /// <summary>Menor pedido tentado depois de recusas sucessivas (abaixo disso, desiste).</summary>
    public const long TamanhoMinimoPedido = 1_000_000;

    /// <summary>Contador compartilhado pelas conexões da etapa (Interlocked) + 1º erro visto.</summary>
    private sealed class Contador
    {
        public long Bytes;

        /// <summary>v1.2.1: erro que fez uma conexão desistir (antes era engolido em silêncio).</summary>
        public HttpRequestException? Erro;
    }

    private async Task<(double Bps, long Bytes)> MedirAsync(
        EtapaTeste etapa, IProgress<ProgressoTeste>? progresso, CancellationToken ct)
    {
        long teto = etapa == EtapaTeste.Download ? _limites.TetoDownloadBytes : _limites.TetoUploadBytes;
        var contador = new Contador();
        using var fim = CancellationTokenSource.CreateLinkedTokenSource(ct);
        fim.CancelAfter(TimeSpan.FromSeconds(_limites.SegundosPorEtapa));

        var relogio = Stopwatch.StartNew();
        long bytesNoAquecimento = -1;
        double segundosNoAquecimento = 0;

        // Cada conexão repete pedidos até o tempo ou o teto acabarem (v1.2.1: no máximo 8 MB cada)
        long porPedido = CalculoTeste.TamanhoDoPedido(teto, _limites.Conexoes);
        Task[] conexoes = Enumerable.Range(0, _limites.Conexoes)
            .Select(_ => Task.Run(() => LacoConexaoAsync(etapa, porPedido, teto, contador, fim.Token), CancellationToken.None))
            .ToArray();

        // Acompanha a cada 250 ms: marca o fim do aquecimento e informa a velocidade parcial
        Task todas = Task.WhenAll(conexoes);
        while (!todas.IsCompleted)
        {
            await Task.WhenAny(todas, Task.Delay(250, CancellationToken.None)).ConfigureAwait(false);
            double s = relogio.Elapsed.TotalSeconds;
            long b = Interlocked.Read(ref contador.Bytes);
            if (bytesNoAquecimento < 0 && s >= _limites.SegundosAquecimento)
            {
                bytesNoAquecimento = b;
                segundosNoAquecimento = s;
            }

            double fracao = Math.Max(s / _limites.SegundosPorEtapa, (double)b / teto);
            progresso?.Report(new ProgressoTeste(etapa, CalculoTeste.BitsPorSegundo(b, s), Math.Min(1, fracao)));
        }

        ct.ThrowIfCancellationRequested(); // cancelado pelo usuário (não pelo tempo da etapa)
        relogio.Stop();
        await todas.ConfigureAwait(false); // v1.2.1: erro inesperado de uma conexão aparece, não some

        long total = Interlocked.Read(ref contador.Bytes);

        // v1.2.1: etapa sem NENHUM byte não é "0 Mbps" — é falha. Vai para a tela como erro
        // (a janela mostra "o servidor recusou…"), em vez de um resultado falso.
        if (total == 0)
        {
            throw contador.Erro ?? new HttpRequestException(
                $"Nenhum dado {(etapa == EtapaTeste.Download ? "recebido" : "enviado")} na etapa de {etapa.ToString().ToLowerInvariant()}.");
        }
        double bps = CalculoTeste.VelocidadeSemAquecimento(
            total, relogio.Elapsed.TotalSeconds, Math.Max(0, bytesNoAquecimento), segundosNoAquecimento);
        return (bps, total);
    }

    private async Task LacoConexaoAsync(EtapaTeste etapa, long porPedido, long teto, Contador contador, CancellationToken ct)
    {
        byte[] buffer = new byte[64 * 1024];
        while (!ct.IsCancellationRequested && Interlocked.Read(ref contador.Bytes) < teto)
        {
            try
            {
                if (etapa == EtapaTeste.Download)
                {
                    using HttpResponseMessage r = await _http.GetAsync(
                        $"{_servidor}/__down?bytes={porPedido}", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    r.EnsureSuccessStatusCode();
                    await using Stream s = await r.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    int lidos;
                    while ((lidos = await s.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        if (Interlocked.Add(ref contador.Bytes, lidos) >= teto)
                        {
                            return; // teto atingido: para de baixar no meio do pedido
                        }
                    }
                }
                else
                {
                    using var corpo = new ConteudoContado(porPedido, contador, teto);
                    using HttpResponseMessage r = await _http.PostAsync($"{_servidor}/__up", corpo, ct).ConfigureAwait(false);
                    r.EnsureSuccessStatusCode();
                }
            }
            catch (OperationCanceledException)
            {
                return; // fim do tempo da etapa (ou cancelado): o que já passou conta
            }
            catch (HttpRequestException) when (ct.IsCancellationRequested)
            {
                return; // conexão cortada pelo cancelamento no meio da transferência
            }
            catch (HttpRequestException ex)
            {
                // v1.2.1: servidor recusou (ex.: 403 em certos tamanhos) ou a conexão caiu.
                // Tenta de novo com a METADE do tamanho; abaixo do mínimo, desiste e guarda o
                // erro para a etapa poder explicá-lo se nenhuma conexão conseguiu nada.
                long menor = CalculoTeste.PedidoAposRecusa(porPedido);
                if (menor <= 0)
                {
                    Interlocked.CompareExchange(ref contador.Erro, ex, null);
                    return;
                }

                porPedido = menor;
            }
        }
    }

    /// <summary>
    /// Corpo do upload gerado na hora (sem alocar o pedido inteiro), contando os bytes que saem.
    /// Bytes pseudoaleatórios fixos: nada do PC do usuário é enviado.
    /// </summary>
    private sealed class ConteudoContado : HttpContent
    {
        private static readonly byte[] Bloco = CriarBloco();
        private readonly long _tamanho;
        private readonly Contador _contador;
        private readonly long _teto;

        public ConteudoContado(long tamanho, Contador contador, long teto)
        {
            _tamanho = tamanho;
            _contador = contador;
            _teto = teto;
            Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        }

        private static byte[] CriarBloco()
        {
            var b = new byte[64 * 1024];
            new Random(42).NextBytes(b); // incompressível: nenhum proxy "acelera" o teste
            return b;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            await SerializeToStreamAsync(stream, context, CancellationToken.None).ConfigureAwait(false);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
        {
            long restante = _tamanho;
            while (restante > 0 && Interlocked.Read(ref _contador.Bytes) < _teto)
            {
                int n = (int)Math.Min(Bloco.Length, restante);
                await stream.WriteAsync(Bloco.AsMemory(0, n), ct).ConfigureAwait(false);
                Interlocked.Add(ref _contador.Bytes, n);
                restante -= n;
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false; // envio em partes (chunked): pode parar antes ao bater o teto
        }
    }
}

/// <summary>Contas puras do teste (testáveis sem rede).</summary>
public static class CalculoTeste
{
    /// <summary>
    /// v1.2.1: tamanho de cada pedido = 1/(conexões × 2) do teto, entre 64 KB e
    /// <see cref="TesteVelocidade.TamanhoMaximoPedido"/> (o servidor recusa alguns tamanhos maiores).
    /// </summary>
    public static long TamanhoDoPedido(long teto, int conexoes) =>
        Math.Clamp(teto / (Math.Max(1, conexoes) * 2L), 64 * 1024, TesteVelocidade.TamanhoMaximoPedido);

    /// <summary>
    /// v1.2.1: próximo tamanho depois de uma recusa (metade); 0 = desistir (já estava no mínimo).
    /// </summary>
    public static long PedidoAposRecusa(long atual) =>
        atual <= TesteVelocidade.TamanhoMinimoPedido ? 0 : Math.Max(TesteVelocidade.TamanhoMinimoPedido, atual / 2);

    public static double BitsPorSegundo(long bytes, double segundos) =>
        segundos > 0 && double.IsFinite(segundos) ? bytes * 8 / segundos : 0;

    /// <summary>
    /// Velocidade descontando o aquecimento: (total − bytes até o aquecimento) ÷ (tempo − tempo
    /// do aquecimento). Etapa curta demais (terminou pelo teto antes do aquecimento) usa o total.
    /// </summary>
    public static double VelocidadeSemAquecimento(long total, double segundos, long bytesAquecimento, double segundosAquecimento)
    {
        double janela = segundos - segundosAquecimento;
        if (bytesAquecimento <= 0 || janela < 0.5 || total <= bytesAquecimento)
        {
            return BitsPorSegundo(total, segundos);
        }

        return BitsPorSegundo(total - bytesAquecimento, janela);
    }

    /// <summary>
    /// "94,3" + "Mbps" (1 casa abaixo de 100), "512" + "Mbps", "1,25" + "Gbps" — bits, como o
    /// plano da operadora. Número e unidade separados para a tela destacar o número.
    /// </summary>
    public static (string Numero, string Unidade) FormatarMbps(double bitsPorSegundo)
    {
        var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        double bps = double.IsFinite(bitsPorSegundo) ? Math.Max(0, bitsPorSegundo) : 0;
        if (bps >= 1e9)
        {
            return ((bps / 1e9).ToString("0.00", pt), "Gbps");
        }

        double mbps = bps / 1e6;
        return (mbps < 100 ? mbps.ToString("0.0", pt) : mbps.ToString("0", pt), "Mbps");
    }

    public static double Mediana(IReadOnlyList<double> valores)
    {
        if (valores.Count == 0)
        {
            return 0;
        }

        double[] o = [.. valores.OrderBy(v => v)];
        int m = o.Length / 2;
        return o.Length % 2 == 1 ? o[m] : (o[m - 1] + o[m]) / 2;
    }

    /// <summary>
    /// Comparação com o plano contratado (Mbps). A Anatel exige da operadora, no INSTANTE, pelo
    /// menos 40 % do contratado (e 80 % na média do mês) — um teste é uma medida instantânea.
    /// null = sem plano informado.
    /// </summary>
    public static string? ComparacaoComPlano(double downloadBps, double planoMbps)
    {
        if (!(planoMbps > 0) || !double.IsFinite(downloadBps))
        {
            return null;
        }

        double fracao = downloadBps / (planoMbps * 1_000_000);
        string pct = $"{Math.Floor(fracao * 100):0} %";
        return fracao >= 0.8
            ? $"{pct} do plano de {planoMbps:0} Mbps — dentro do esperado."
            : fracao >= 0.4
                ? $"{pct} do plano de {planoMbps:0} Mbps — acima do mínimo instantâneo da Anatel (40 %), mas abaixo dos 80 % da média mensal."
                : $"{pct} do plano de {planoMbps:0} Mbps — ABAIXO do mínimo instantâneo da Anatel (40 %). Repita o teste por cabo para confirmar.";
    }
}
