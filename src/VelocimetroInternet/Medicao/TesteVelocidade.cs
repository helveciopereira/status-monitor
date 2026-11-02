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
/// Servidor (v1.6.0, Onda 11): o mais próximo do fast.com (caches da Netflix, muitas vezes dentro
/// da operadora — ver <see cref="ServidorFast"/>); se o fast.com não responder, speed.cloudflare.com
/// (<see cref="ServidorCloudflare"/>, o único até a v1.5.0). Sem conta, sem chave, sem rastrear o usuário.
///
/// Método (o mesmo dos testes de navegador): latência = mediana de várias requisições vazias;
/// download/upload = várias conexões em paralelo por um tempo fixo, com TETO de dados (internet
/// rápida termina pelo teto, lenta pelo tempo). O 1º trecho de cada etapa é aquecimento e não
/// entra na conta (a janela do TCP ainda está abrindo).
/// </summary>
public sealed class TesteVelocidade
{
    /// <summary>Servidor de reserva (e o único até a v1.5.0).</summary>
    public const string ServidorPadrao = ServidorCloudflare.Endereco;

    /// <summary>
    /// Limites de uma rodada. Os padrões gastam no máximo ~210 MB (v1.6.0; antes ~180 MB).
    /// v1.2.2: teto do download 100 → 150 MB (+50 %, pedido do dono). A 530 Mbps (medido nesta
    /// rede em 2026-11-01) 100 MB acabavam em ~1,5 s — tirando 1 s de aquecimento sobrava meio
    /// segundo de medição, com o TCP ainda acelerando, e o resultado ficava abaixo do real.
    /// v1.6.0: teto do upload 30 → 60 MB (o teste inteiro passa de ~180 para ~210 MB). Mesmo motivo:
    /// a ~150 Mbps de upload (medido em 2026-11-02), 30 MB acabavam em ~1,6 s e sobrava meio segundo
    /// de medição — o upload oscilava de 31 a 184 Mbps entre rodadas seguidas. 60 MB também dobram o
    /// tamanho de cada envio (3,75 → 7,5 MB): no fast.com, envios pequenos rendiam ~40 % menos.
    /// </summary>
    public sealed record Limites(
        int Conexoes = 4,
        double SegundosPorEtapa = 10,
        long TetoDownloadBytes = 150_000_000,
        long TetoUploadBytes = 60_000_000,
        int AmostrasLatencia = 7,
        double SegundosAquecimento = 1.0);

    private readonly HttpClient _http;

    /// <summary>Servidor fixo; null = automático (fast.com, com a Cloudflare de reserva).</summary>
    private readonly IServidorTeste? _servidorFixo;
    private readonly Limites _limites;

    /// <param name="servidor">
    /// v1.6.0: null (padrão) = automático. Um endereço = servidor no formato da Cloudflare
    /// (<c>/__down</c> e <c>/__up</c>), usado pelos testes automatizados com servidor falso.
    /// </param>
    public TesteVelocidade(HttpClient? http = null, string? servidor = null, Limites? limites = null)
        : this(servidor is null ? null : new ServidorCloudflare(servidor), http, limites)
    {
    }

    /// <summary>v1.6.0: mede com um servidor escolhido por quem chama (null = automático).</summary>
    public TesteVelocidade(IServidorTeste? servidor, HttpClient? http = null, Limites? limites = null)
    {
        _http = http ?? CriarCliente();
        _servidorFixo = servidor;
        _limites = limites ?? new Limites();
    }

    /// <summary>
    /// v1.6.0: por que o teste NÃO usou o fast.com (null = usou, ou o servidor era fixo). Vai para o
    /// log — sem isso, um fast.com fora do ar viraria uma troca de servidor silenciosa.
    /// </summary>
    public string? MotivoDaReserva { get; private set; }

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
        MotivoDaReserva = null;

        // v1.6.0: escolhe o servidor. A latência é o 1º pedido de verdade ao servidor escolhido —
        // se o fast.com falhar AQUI (antes de gastar dados), troca para a Cloudflare e segue.
        IServidorTeste servidor = _servidorFixo ?? await EscolherAsync(ct).ConfigureAwait(false);
        double latencia;
        try
        {
            latencia = await MedirLatenciaAsync(servidor, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (_servidorFixo is null && servidor is ServidorFast)
        {
            MotivoDaReserva = $"o servidor do fast.com não respondeu ({ex.Message})";
            servidor = new ServidorCloudflare();
            latencia = await MedirLatenciaAsync(servidor, ct).ConfigureAwait(false);
        }

        (double down, long baixados) = await MedirAsync(servidor, EtapaTeste.Download, progresso, ct).ConfigureAwait(false);
        (double up, long enviados) = await MedirAsync(servidor, EtapaTeste.Upload, progresso, ct).ConfigureAwait(false);

        progresso?.Report(new ProgressoTeste(EtapaTeste.Concluido, 0, 1));
        return new ResultadoTeste(latencia, down, up, baixados, enviados, servidor.Nome);
    }

    /// <summary>v1.6.0: fast.com (o mais próximo da lista dele) ou, se ele não responder, a Cloudflare.</summary>
    private async Task<IServidorTeste> EscolherAsync(CancellationToken ct)
    {
        if (await ServidorFast.ObterAsync(_http, ct).ConfigureAwait(false) is { } fast)
        {
            return fast;
        }

        MotivoDaReserva = "o fast.com não informou servidor";
        return new ServidorCloudflare();
    }

    // ------------------------------------------------------------------ latência

    private async Task<double> MedirLatenciaAsync(IServidorTeste servidor, CancellationToken ct)
    {
        var tempos = new List<double>(_limites.AmostrasLatencia);
        for (int i = 0; i < _limites.AmostrasLatencia; i++)
        {
            using HttpRequestMessage pedido = servidor.PedidoLatencia();
            var sw = Stopwatch.StartNew();
            using HttpResponseMessage r = await _http.SendAsync(
                pedido, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
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
    /// v1.2.2: vale só para o UPLOAD; o download usa <see cref="TamanhosDownloadAceitos"/>.
    /// </summary>
    public const long TamanhoMaximoPedido = 8_000_000;

    /// <summary>
    /// v1.2.2: tamanhos de pedido de DOWNLOAD que o servidor aceita, do maior para o menor. Medido
    /// em 2026-11-01 (3 repetições cada): ≤ 10,5 MB → 200; 11 a 16,7 MB → 403 SEMPRE; 20 a 30 MB → 200.
    /// 25 MB é o tamanho do próprio teste da Cloudflare no navegador. Pedido maior = menos pausas
    /// entre pedidos (1 ida e volta cada), que impediam de chegar no máximo da rede.
    /// Depois de uso intenso o servidor passa a responder 429 a pedidos de ≥ 10 MB por 15+ minutos
    /// (pedidos de 8 MB ou menos continuam passando) — por isso a recusa cai DIRETO para 8 MB,
    /// sem o degrau de 10 MB, e depois para 1 MB.
    /// </summary>
    public static readonly long[] TamanhosDownloadAceitos = [25_000_000, 8_000_000, 1_000_000];

    /// <summary>Menor pedido tentado depois de recusas sucessivas (abaixo disso, desiste).</summary>
    public const long TamanhoMinimoPedido = 1_000_000;

    /// <summary>Contador compartilhado pelas conexões da etapa (Interlocked) + 1º erro visto.</summary>
    private sealed class Contador
    {
        public long Bytes;

        /// <summary>
        /// v1.6.0: bytes de upload já RESERVADOS por pedidos em andamento. Cada envio declara o
        /// tamanho (Content-Length: o fast.com recusa envio em partes com 406) e não pode parar no
        /// meio — então o teto é respeitado reservando antes de enviar, não cortando o envio.
        /// </summary>
        public long Reservado;

        /// <summary>v1.2.1: erro que fez uma conexão desistir (antes era engolido em silêncio).</summary>
        public HttpRequestException? Erro;
    }

    private async Task<(double Bps, long Bytes)> MedirAsync(
        IServidorTeste servidor, EtapaTeste etapa, IProgress<ProgressoTeste>? progresso, CancellationToken ct)
    {
        long teto = etapa == EtapaTeste.Download ? _limites.TetoDownloadBytes : _limites.TetoUploadBytes;
        var contador = new Contador();
        using var fim = CancellationTokenSource.CreateLinkedTokenSource(ct);
        fim.CancelAfter(TimeSpan.FromSeconds(_limites.SegundosPorEtapa));

        var relogio = Stopwatch.StartNew();
        long bytesNoAquecimento = -1;
        double segundosNoAquecimento = 0;

        // Cada conexão repete pedidos até o tempo ou o teto acabarem. Download (v1.2.2): o maior
        // tamanho ACEITO pelo servidor que caiba na parte do teto de cada conexão; upload: até 8 MB.
        long porPedido = etapa == EtapaTeste.Download
            ? CalculoTeste.PedidoDownload(teto, _limites.Conexoes)
            : CalculoTeste.TamanhoDoPedido(teto, _limites.Conexoes);
        Task[] conexoes = Enumerable.Range(0, _limites.Conexoes)
            .Select(_ => Task.Run(() => LacoConexaoAsync(servidor, etapa, porPedido, teto, contador, fim.Token), CancellationToken.None))
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

    private async Task LacoConexaoAsync(
        IServidorTeste servidor, EtapaTeste etapa, long porPedido, long teto, Contador contador, CancellationToken ct)
    {
        byte[] buffer = new byte[64 * 1024];
        while (!ct.IsCancellationRequested && Interlocked.Read(ref contador.Bytes) < teto)
        {
            long reservado = 0; // v1.6.0: parte do teto de upload presa a ESTE pedido
            try
            {
                if (etapa == EtapaTeste.Download)
                {
                    using HttpRequestMessage pedido = servidor.PedidoDownload(porPedido);
                    using HttpResponseMessage r = await _http.SendAsync(
                        pedido, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
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
                    // v1.6.0: reserva a fatia do teto ANTES de enviar; o último pedido encolhe para
                    // caber (o envio tem tamanho declarado e não pode ser cortado no meio)
                    long inicio = Interlocked.Add(ref contador.Reservado, porPedido) - porPedido;
                    if (inicio >= teto)
                    {
                        Interlocked.Add(ref contador.Reservado, -porPedido);
                        return; // outras conexões já cobriram o teto
                    }

                    long tamanho = Math.Min(porPedido, teto - inicio);
                    Interlocked.Add(ref contador.Reservado, tamanho - porPedido); // devolve a sobra
                    reservado = tamanho;
                    using var corpo = new ConteudoContado(tamanho, contador);
                    using HttpResponseMessage r = await _http.PostAsync(servidor.UrlUpload(tamanho), corpo, ct).ConfigureAwait(false);
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
                // v1.6.0: o pedido recusado devolve a fatia do teto que tinha reservado.
                Interlocked.Add(ref contador.Reservado, -reservado);
                long menor = etapa == EtapaTeste.Download
                    ? CalculoTeste.PedidoDownloadAposRecusa(porPedido) // v1.2.2: próximo tamanho aceito
                    : CalculoTeste.PedidoAposRecusa(porPedido);
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
    /// v1.6.0: tamanho DECLARADO (Content-Length) e enviado inteiro — o fast.com recusa envio em partes
    /// (406) e a Cloudflare aceita os dois. O teto agora é garantido pela reserva em <see cref="Contador.Reservado"/>.
    /// </summary>
    private sealed class ConteudoContado : HttpContent
    {
        private static readonly byte[] Bloco = CriarBloco();
        private readonly long _tamanho;
        private readonly Contador _contador;

        public ConteudoContado(long tamanho, Contador contador)
        {
            _tamanho = tamanho;
            _contador = contador;
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
            while (restante > 0)
            {
                int n = (int)Math.Min(Bloco.Length, restante);
                await stream.WriteAsync(Bloco.AsMemory(0, n), ct).ConfigureAwait(false);
                Interlocked.Add(ref _contador.Bytes, n);
                restante -= n;
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _tamanho; // v1.6.0: Content-Length (antes: envio em partes, que o fast.com recusa)
            return true;
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

    /// <summary>
    /// v1.2.2: pedido de download = o maior tamanho aceito que caiba em teto ÷ conexões (padrão:
    /// 150 MB ÷ 4 = 37,5 → 25 MB); teto pequeno usa o menor da lista. Nunca um tamanho fora da lista —
    /// tamanhos calculados (ex.: 12,5 MB) caem na faixa que o servidor recusa.
    /// </summary>
    public static long PedidoDownload(long teto, int conexoes)
    {
        long parte = teto / Math.Max(1, conexoes);
        long[] lista = TesteVelocidade.TamanhosDownloadAceitos;
        return lista.FirstOrDefault(t => t <= parte, lista[^1]);
    }

    /// <summary>v1.2.2: próximo tamanho aceito abaixo do atual; 0 = desistir (já no menor).</summary>
    public static long PedidoDownloadAposRecusa(long atual) =>
        TesteVelocidade.TamanhosDownloadAceitos.FirstOrDefault(t => t < atual, 0);

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
