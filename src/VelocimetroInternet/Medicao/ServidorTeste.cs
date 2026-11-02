using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VelocimetroInternet.Medicao;

/// <summary>
/// v1.6.0 (Onda 11): "onde" o teste de velocidade mede. O método (latência, conexões em paralelo,
/// tetos, aquecimento) é o mesmo para qualquer servidor — só mudam os endereços dos pedidos.
/// </summary>
public interface IServidorTeste
{
    /// <summary>Nome curto que vai para a tela, o log e o relatório (ex.: "fast.com · Fortaleza").</summary>
    string Nome { get; }

    /// <summary>Pedido vazio cuja ida e volta é a latência (um novo a cada amostra).</summary>
    HttpRequestMessage PedidoLatencia();

    /// <summary>Pedido que devolve exatamente <paramref name="bytes"/> bytes.</summary>
    HttpRequestMessage PedidoDownload(long bytes);

    /// <summary>Endereço que aceita (e descarta) um POST de <paramref name="bytes"/> bytes.</summary>
    Uri UrlUpload(long bytes);
}

/// <summary>
/// speed.cloudflare.com (o servidor da v1.2.0 até a v1.5.0): GET <c>/__down?bytes=N</c> devolve N
/// bytes; POST <c>/__up</c> aceita o corpo. Fica como RESERVA do fast.com. Medido em 2026-11-02 em
/// São Luís (Claro): atende do Rio (colo GIG), ~95 ms de latência — a distância pesa no resultado.
/// </summary>
public sealed class ServidorCloudflare(string endereco = ServidorCloudflare.Endereco) : IServidorTeste
{
    public const string Endereco = "https://speed.cloudflare.com";

    private readonly string _base = endereco.TrimEnd('/');

    public string Nome => new Uri(_base).Host;

    public HttpRequestMessage PedidoLatencia() => new(HttpMethod.Get, $"{_base}/__down?bytes=0");

    public HttpRequestMessage PedidoDownload(long bytes) => new(HttpMethod.Get, $"{_base}/__down?bytes={bytes}");

    public Uri UrlUpload(long bytes) => new($"{_base}/__up");
}

/// <summary>
/// v1.6.0 (Onda 11): servidores do fast.com — os caches da Netflix (Open Connect), muitas vezes
/// DENTRO da rede da operadora. Medido em 2026-11-02 nesta casa (Claro, São Luís, por Wi-Fi):
/// servidor na própria cidade, latência ~8 ms (Cloudflare: ~95 ms), upload 182 × 99 Mbps e download
/// 20–30 % maior. É o que mais se aproxima da linha contratada, sem o trânsito até outro estado.
///
/// Protocolo (o mesmo do site fast.com, lido do app-*.js dele):
/// 1. GET <c>api.fast.com/netflix/speedtest/v2?https=true&amp;token=…&amp;urlCount=5</c> → lista
///    de servidores ("targets"), do mais próximo para o mais distante, com URLs assinadas que valem
///    por algumas horas;
/// 2. download: GET <c>…/speedtest/range/0-(N-1)?…</c> devolve N bytes;
/// 3. upload: POST em <c>…/speedtest/range/0-N?…</c> — EXIGE Content-Length (envio em partes,
///    "chunked", leva 406);
/// 4. latência: POST vazio em <c>…/speedtest/range/0-0?…</c>.
///
/// O token é público (está no JavaScript do site) mas não é API documentada: se a Netflix trocar,
/// o app lê o token novo do site; se nada funcionar, o teste usa a Cloudflare (nunca fica sem teste).
/// </summary>
public sealed class ServidorFast : IServidorTeste
{
    /// <summary>Token embutido no site fast.com (visto em 2026-11-02, o mesmo há anos).</summary>
    public const string TokenConhecido = "YXNkZmFzZGxmbnNkYWZoYXNkZmhrYWxm";

    private const string Site = "https://fast.com/";
    private const string Api = "https://api.fast.com/netflix/speedtest/v2?https=true&urlCount=5&token=";

    /// <summary>Prazo para descobrir o servidor (API + plano B do token) antes de cair na Cloudflare.</summary>
    private static readonly TimeSpan PrazoDescoberta = TimeSpan.FromSeconds(8);

    private readonly string _alvo;

    /// <param name="urlAlvo">URL assinada do servidor, como a API devolve (".../speedtest?c=…&amp;t=…").</param>
    /// <param name="cidade">Cidade informada pela API (pode vir vazia ou como "CEP 65020-230").</param>
    public ServidorFast(string urlAlvo, string? cidade)
    {
        _alvo = urlAlvo;
        Nome = NomeComCidade(cidade);
    }

    public string Nome { get; }

    public HttpRequestMessage PedidoLatencia() =>
        new(HttpMethod.Post, Faixa(_alvo, "0-0")) { Content = new ByteArrayContent([]) };

    // range/0-(N-1) = N bytes (o servidor inclui o último byte da faixa: 0-1000 devolve 1001)
    public HttpRequestMessage PedidoDownload(long bytes) => new(HttpMethod.Get, Faixa(_alvo, $"0-{Math.Max(0, bytes - 1)}"));

    public Uri UrlUpload(long bytes) => new(Faixa(_alvo, $"0-{bytes}"));

    /// <summary>
    /// "…/speedtest?x" → "…/speedtest/range/0-N?x" (o mesmo <c>url.replace(/speedtest/, "speedtest/range/")</c>
    /// do site). URL sem "/speedtest" volta como está (a API nunca mandou assim; o pedido apenas falharia).
    /// </summary>
    public static string Faixa(string url, string faixa)
    {
        int i = url.IndexOf("/speedtest", StringComparison.Ordinal);
        return i < 0 ? url : url.Insert(i + "/speedtest".Length, $"/range/{faixa}");
    }

    /// <summary>
    /// "fast.com · Fortaleza"; só "fast.com" quando a API manda CEP no lugar da cidade (aconteceu
    /// com o servidor de São Luís) ou nada — melhor omitir do que adivinhar a cidade.
    /// </summary>
    public static string NomeComCidade(string? cidade)
    {
        string c = (cidade ?? "").Trim();
        bool cidadeDeVerdade = c.Length > 0 && !c.Any(char.IsDigit);
        return cidadeDeVerdade ? $"fast.com · {c}" : "fast.com";
    }

    /// <summary>
    /// Descobre o servidor mais próximo. Null = fast.com indisponível agora (sem rede para ele, API
    /// recusou, resposta estranha, demorou mais de 8 s) — quem chama usa a Cloudflare.
    /// Cancelamento do USUÁRIO continua subindo (não vira "indisponível").
    /// </summary>
    public static async Task<ServidorFast?> ObterAsync(HttpClient http, CancellationToken ct)
    {
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(ct);
        prazo.CancelAfter(PrazoDescoberta);
        try
        {
            // 1º o token conhecido (1 pedido só); recusou → lê o token atual do site e tenta de novo
            string? json = await ConsultarApiAsync(http, TokenConhecido, prazo.Token).ConfigureAwait(false);
            if (json is null && await TokenDoSiteAsync(http, prazo.Token).ConfigureAwait(false) is { } token
                && token != TokenConhecido)
            {
                json = await ConsultarApiAsync(http, token, prazo.Token).ConfigureAwait(false);
            }

            return json is null ? null : Interpretar(json);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null; // estourou o prazo de 8 s (não foi o usuário)
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static async Task<string?> ConsultarApiAsync(HttpClient http, string token, CancellationToken ct)
    {
        using HttpResponseMessage r = await http.GetAsync(Api + Uri.EscapeDataString(token), ct).ConfigureAwait(false);
        return r.IsSuccessStatusCode ? await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false) : null;
    }

    /// <summary>Plano B: página do fast.com → nome do app-*.js → token dentro dele.</summary>
    private static async Task<string?> TokenDoSiteAsync(HttpClient http, CancellationToken ct)
    {
        using HttpResponseMessage pagina = await http.GetAsync(Site, ct).ConfigureAwait(false);
        if (!pagina.IsSuccessStatusCode
            || AcharScriptDoApp(await pagina.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) is not { } script)
        {
            return null;
        }

        using HttpResponseMessage js = await http.GetAsync(Site + script, ct).ConfigureAwait(false);
        return js.IsSuccessStatusCode ? AcharToken(await js.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) : null;
    }

    /// <summary>"app-0bffe1.js" no HTML do fast.com (null se o site mudar de forma).</summary>
    public static string? AcharScriptDoApp(string html) =>
        Regex.Match(html, @"app-[A-Za-z0-9]+\.js") is { Success: true } m ? m.Value : null;

    /// <summary><c>token:"…"</c> no JavaScript do fast.com.</summary>
    public static string? AcharToken(string js) =>
        Regex.Match(js, @"token:""([A-Za-z0-9+/=_-]{10,})""") is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>
    /// Resposta da API → o PRIMEIRO servidor HTTPS da lista (a API ordena do mais próximo para o
    /// mais distante). Medido: espalhar as conexões pelos 5 servidores dava MENOS (191 Mbps) do que
    /// todas no mais próximo (287 Mbps) — os outros ficavam em outros estados. Null = sem servidor.
    /// </summary>
    public static ServidorFast? Interpretar(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("targets", out JsonElement alvos) || alvos.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (JsonElement a in alvos.EnumerateArray())
            {
                // Confere o TIPO de cada campo antes de ler (TryGetProperty em não-objeto e GetString
                // em não-texto lançam InvalidOperationException — resposta estranha vira "sem servidor")
                if (a.ValueKind == JsonValueKind.Object
                    && a.TryGetProperty("url", out JsonElement u) && u.ValueKind == JsonValueKind.String
                    && u.GetString() is { } url
                    && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    && url.Contains("/speedtest", StringComparison.Ordinal))
                {
                    string? cidade = a.TryGetProperty("location", out JsonElement loc) && loc.ValueKind == JsonValueKind.Object
                                     && loc.TryGetProperty("city", out JsonElement c) && c.ValueKind == JsonValueKind.String
                        ? c.GetString() : null;
                    return new ServidorFast(url, cidade);
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
