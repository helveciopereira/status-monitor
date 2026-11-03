using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Velopack.Sources;

namespace VelocimetroInternet.Sistema;

/// <summary>
/// v1.8.0: o cliente HTTP da atualização automática — UM <see cref="HttpClient"/> para o processo
/// inteiro, no lugar do <c>HttpClientFileDownloader</c> padrão do Velopack, que cria um
/// <see cref="HttpClient"/> (e um <c>HttpClientHandler</c>, com pool de conexões, buffers e threads
/// próprios) a CADA pedido.
///
/// Medido no harness da varredura de 2026-11-03 (30 consultas do <c>releases.win.json</c>): o padrão
/// deixava ~0,5 handle de kernel e ~0,3 MB por pedido mesmo depois de GC completo + finalizadores.
/// Com 288 consultas/dia (até a v1.7.1) isso era parte do crescimento de memória do app
/// (+5,5 MB/h no log). A recomendação oficial do .NET é a mesma: um HttpClient de vida longa com
/// <see cref="SocketsHttpHandler.PooledConnectionLifetime"/> para o DNS não "grudar".
///
/// Implementa só os três métodos que o Velopack pede: texto (o feed), bytes e arquivo com progresso.
/// Os erros saem como <see cref="HttpRequestException"/> com <c>StatusCode</c> preenchido, que é o
/// que <see cref="ServicoAtualizacao.Classificar"/> lê para explicar a falha ("nenhuma versão
/// publicada ainda", "o GitHub limitou as consultas"...).
/// </summary>
public sealed class BaixadorHttp : IFileDownloader
{
    /// <summary>Um só para o processo: o pool de conexões vive aqui e é reutilizado entre as consultas.</summary>
    private static readonly HttpClient Cliente = CriarCliente();

    private static HttpClient CriarCliente()
    {
        var handler = new SocketsHttpHandler
        {
            // O GitHub responde .../releases/latest/download/<arquivo> com redirecionamento 302
            // para objects.githubusercontent.com — o mesmo que o Velopack seguia.
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = DecompressionMethods.All,
            // Conexão reciclada a cada 5 min (DNS atualizado); ociosa some em 1 min — entre duas
            // consultas de 1 h nada fica aberto.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
        };
        var cliente = new HttpClient(handler)
        {
            // O tempo limite é por pedido (parâmetro do Velopack), via CancellationToken abaixo
            Timeout = Timeout.InfiniteTimeSpan,
        };
        string versao = typeof(BaixadorHttp).Assembly.GetName().Version?.ToString(3) ?? "0";
        cliente.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StatusMonitor", versao));
        return cliente;
    }

    /// <summary>Monta o pedido com os cabeçalhos extras que o Velopack passar (ex.: Authorization).</summary>
    private static HttpRequestMessage Pedido(string url, IDictionary<string, string>? cabecalhos)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Get, url);
        if (cabecalhos is not null)
        {
            foreach (KeyValuePair<string, string> c in cabecalhos)
            {
                pedido.Headers.TryAddWithoutValidation(c.Key, c.Value);
            }
        }

        return pedido;
    }

    /// <summary>Token que cancela quando o tempo limite (em MINUTOS, como o Velopack conta) estourar.</summary>
    private static CancellationTokenSource ComTempoLimite(double minutos, CancellationToken externo)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(externo);
        if (minutos > 0)
        {
            cts.CancelAfter(TimeSpan.FromMinutes(minutos));
        }

        return cts;
    }

    /// <inheritdoc/>
    public async Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30)
    {
        using CancellationTokenSource cts = ComTempoLimite(timeout, CancellationToken.None);
        using HttpRequestMessage pedido = Pedido(url, headers);
        using HttpResponseMessage resposta = await Cliente.SendAsync(pedido, cts.Token).ConfigureAwait(false);
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30)
    {
        using CancellationTokenSource cts = ComTempoLimite(timeout, CancellationToken.None);
        using HttpRequestMessage pedido = Pedido(url, headers);
        using HttpResponseMessage resposta = await Cliente.SendAsync(pedido, cts.Token).ConfigureAwait(false);
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DownloadFile(string url, string targetFile, Action<int> progress,
        IDictionary<string, string>? headers = null, double timeout = 30, CancellationToken cancelToken = default)
    {
        using CancellationTokenSource cts = ComTempoLimite(timeout, cancelToken);
        using HttpRequestMessage pedido = Pedido(url, headers);
        // Cabeçalhos primeiro: o pacote (dezenas de MB) é copiado em pedaços, não carregado inteiro
        using HttpResponseMessage resposta = await Cliente
            .SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
        resposta.EnsureSuccessStatusCode();

        long? total = resposta.Content.Headers.ContentLength;
        await using Stream origem = await resposta.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        await using var destino = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

        byte[] buffer = new byte[1 << 16];
        long copiados = 0;
        int ultimoPercentual = -1;
        int lidos;
        while ((lidos = await origem.ReadAsync(buffer, cts.Token).ConfigureAwait(false)) > 0)
        {
            await destino.WriteAsync(buffer.AsMemory(0, lidos), cts.Token).ConfigureAwait(false);
            copiados += lidos;
            if (total is > 0)
            {
                int percentual = (int)(copiados * 100 / total.Value);
                if (percentual != ultimoPercentual)
                {
                    ultimoPercentual = percentual;
                    progress?.Invoke(percentual);
                }
            }
        }

        progress?.Invoke(100);
    }
}
