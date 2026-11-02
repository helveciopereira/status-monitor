using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using VelocimetroInternet.Medicao;

namespace VelocimetroInternet.Tests;

/// <summary>
/// v1.6.0 — Onda 11: teste de velocidade pelo servidor do fast.com (Netflix), com a Cloudflare de
/// reserva. Os servidores falsos imitam o que foi MEDIDO no fast.com real em 2026-11-02: lista de
/// servidores pela API, range/0-N devolve N+1 bytes, upload em partes (chunked) leva 406.
/// </summary>
public sealed class V160ServidorTests
{
    private const string UrlOca = "https://oca.falso.test/speedtest?c=br&n=28573&v=43&e=1793626625&t=assinatura";

    private static readonly TesteVelocidade.Limites Rapido = new(
        Conexoes: 2, SegundosPorEtapa: 1, TetoDownloadBytes: 4_000_000, TetoUploadBytes: 2_000_000,
        AmostrasLatencia: 3, SegundosAquecimento: 0.2);

    private static string Api(params (string Url, string? Cidade)[] alvos) =>
        "{\"client\":{\"ip\":\"0.0.0.0\",\"location\":{\"city\":\"Sao Luis\",\"country\":\"BR\"}},\"targets\":[" +
        string.Join(",", alvos.Select(a => $"{{\"name\":\"{a.Url}\",\"url\":\"{a.Url}\",\"location\":{(a.Cidade is null ? "{}" : $"{{\"city\":\"{a.Cidade}\",\"country\":\"BR\"}}")}}}")) +
        "]}";

    // ------------------------------------------------------------------ partes puras

    [Theory]
    [InlineData("https://x.test/speedtest?c=br&t=1", "0-0", "https://x.test/speedtest/range/0-0?c=br&t=1")]
    [InlineData("https://x.test/speedtest?c=br", "0-26214399", "https://x.test/speedtest/range/0-26214399?c=br")]
    [InlineData("https://x.test/speedtest", "0-9", "https://x.test/speedtest/range/0-9")]
    [InlineData("https://x.test/outra?c=1", "0-0", "https://x.test/outra?c=1")] // sem "/speedtest": intacta
    public void Faixa_InsereORangeComoOSite(string url, string faixa, string esperado) =>
        Assert.Equal(esperado, ServidorFast.Faixa(url, faixa));

    [Fact]
    public void Pedidos_DownloadPedeNMenos1_UploadEOLatenciaComoOSite()
    {
        var s = new ServidorFast(UrlOca, "Fortaleza");
        using HttpRequestMessage down = s.PedidoDownload(26_214_400);
        Assert.Equal(HttpMethod.Get, down.Method);
        Assert.Contains("/speedtest/range/0-26214399?", down.RequestUri!.ToString()); // 0-N devolve N+1
        Assert.Contains("/speedtest/range/0-8000000?", s.UrlUpload(8_000_000).ToString());
        using HttpRequestMessage lat = s.PedidoLatencia();
        Assert.Equal(HttpMethod.Post, lat.Method); // o site mede a latência com POST vazio
        Assert.Contains("/speedtest/range/0-0?", lat.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("Fortaleza", "fast.com · Fortaleza")]
    [InlineData("Belém", "fast.com · Belém")]
    [InlineData("CEP 65020-230", "fast.com")] // o servidor de São Luís veio assim da API real
    [InlineData("", "fast.com")]
    [InlineData(null, "fast.com")]
    public void Nome_SoComCidadeDeVerdade(string? cidade, string esperado) =>
        Assert.Equal(esperado, ServidorFast.NomeComCidade(cidade));

    [Fact]
    public void Interpretar_PegaOPrimeiroHttps_DaListaDaApi()
    {
        string json = Api(("http://sem-tls.test/speedtest?a=1", "Recife"), (UrlOca, "Fortaleza"), ("https://b.test/speedtest?b=2", "Belém"));
        ServidorFast s = Assert.IsType<ServidorFast>(ServidorFast.Interpretar(json));
        Assert.Equal("fast.com · Fortaleza", s.Nome);
        Assert.StartsWith("https://oca.falso.test/speedtest/range/", s.UrlUpload(1).ToString());
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("{}")]
    [InlineData("{\"targets\":{}}")]
    [InlineData("{\"targets\":[]}")]
    [InlineData("{\"targets\":[1,\"x\",{\"url\":5},{\"url\":\"https://sem-caminho.test/\"}]}")]
    public void Interpretar_RespostaEstranha_ViraNull(string json) =>
        Assert.Null(ServidorFast.Interpretar(json));

    [Fact]
    public void Token_EScript_LidosDoSiteComoEle_EStaHoje()
    {
        // Trechos reais do fast.com em 2026-11-02
        Assert.Equal("app-0bffe1.js", ServidorFast.AcharScriptDoApp("<script src=\"/app-0bffe1.js\"></script>"));
        Assert.Equal(ServidorFast.TokenConhecido, ServidorFast.AcharToken(
            "DEFAULT_PARAMS={https:!0,token:\"YXNkZmFzZGxmbnNkYWZoYXNkZmhrYWxm\",urlCount:3,endpoint:\"api-global\"}"));
        Assert.Null(ServidorFast.AcharScriptDoApp("<html>sem script</html>"));
        Assert.Null(ServidorFast.AcharToken("var x = 1;"));
    }

    [Fact]
    public void ServidoresDosTestes_JuntaOsDoFastEListaOsOutros()
    {
        TesteGuardado T(string servidor) => new(new DateTime(2026, 11, 2), 1e8, 1e7, 10, "Cabo", servidor);
        Assert.Equal("", CalculoRelatorio.ServidoresDosTestes([]));
        Assert.Equal("fast.com (Netflix)", CalculoRelatorio.ServidoresDosTestes([T("fast.com"), T("fast.com · Fortaleza")]));
        Assert.Equal("fast.com (Netflix) e speed.cloudflare.com",
            CalculoRelatorio.ServidoresDosTestes([T("speed.cloudflare.com"), T("fast.com · Belém"), T("speed.cloudflare.com")]));
    }

    // ------------------------------------------------------------------ teste inteiro com servidores falsos

    [Fact]
    public async Task Automatico_MedeNoFast_SemTocarNaCloudflare_EComTamanhoDeclarado()
    {
        var rede = new RedeFalsa { RespostaApi = Api((UrlOca, "Fortaleza")) };
        using var http = new HttpClient(rede);
        var t = new TesteVelocidade((IServidorTeste?)null, http, Rapido);

        ResultadoTeste r = await t.ExecutarAsync(null, CancellationToken.None);

        Assert.Equal("fast.com · Fortaleza", r.Servidor);
        Assert.Null(t.MotivoDaReserva);
        Assert.True(r.DownloadBps > 0 && r.UploadBps > 0);
        Assert.Equal(0, rede.PedidosCloudflare);
        Assert.Equal(0, rede.UploadsSemTamanho); // o fast.com real dá 406 para envio em partes
        Assert.True(rede.UploadsAceitos > 0);
    }

    [Fact]
    public async Task Upload_NuncaPassaDoTeto()
    {
        var rede = new RedeFalsa { RespostaApi = Api((UrlOca, null)) };
        using var http = new HttpClient(rede);
        var limites = Rapido with { Conexoes = 4, SegundosPorEtapa = 3, TetoUploadBytes = 3_000_000 };

        ResultadoTeste r = await new TesteVelocidade((IServidorTeste?)null, http, limites).ExecutarAsync(null, CancellationToken.None);

        // O envio tem tamanho declarado e não é cortado: o teto vem da reserva antes de enviar
        Assert.True(r.BytesEnviados <= 3_000_000, $"enviou {r.BytesEnviados}");
        Assert.True(r.BytesEnviados > 0);
    }

    [Fact]
    public async Task FastSemApiNemSite_UsaACloudflare_EDizPorQue()
    {
        var rede = new RedeFalsa { RespostaApi = null }; // API 500, site 404
        using var http = new HttpClient(rede);
        var t = new TesteVelocidade((IServidorTeste?)null, http, Rapido);

        ResultadoTeste r = await t.ExecutarAsync(null, CancellationToken.None);

        Assert.Equal("speed.cloudflare.com", r.Servidor);
        Assert.NotNull(t.MotivoDaReserva);
        Assert.True(r.BytesBaixados > 0);
    }

    [Fact]
    public async Task ServidorDoFastForaDoAr_TrocaNaLatencia_AntesDeGastarDados()
    {
        var rede = new RedeFalsa { RespostaApi = Api((UrlOca, "Fortaleza")), OcaForaDoAr = true };
        using var http = new HttpClient(rede);
        var t = new TesteVelocidade((IServidorTeste?)null, http, Rapido);

        ResultadoTeste r = await t.ExecutarAsync(null, CancellationToken.None);

        Assert.Equal("speed.cloudflare.com", r.Servidor);
        Assert.Contains("não respondeu", t.MotivoDaReserva);
        Assert.Equal(0, rede.BytesEntreguesPeloOca);
    }

    [Fact]
    public async Task TokenTrocado_LeOTokenNovoDoSite()
    {
        var rede = new RedeFalsa
        {
            RespostaApi = Api((UrlOca, "Belém")),
            TokenValido = "TokenNovoDaNetflix123",
            PaginaFast = "<html><script src=\"/app-abc123.js\"></script></html>",
            ScriptFast = "x={https:!0,token:\"TokenNovoDaNetflix123\",urlCount:3}",
        };
        using var http = new HttpClient(rede);

        ResultadoTeste r = await new TesteVelocidade((IServidorTeste?)null, http, Rapido).ExecutarAsync(null, CancellationToken.None);

        Assert.Equal("fast.com · Belém", r.Servidor);
    }

    [Fact]
    public async Task CancelarDuranteAEscolha_CancelaOTeste_NaoViraReserva()
    {
        var rede = new RedeFalsa { RespostaApi = Api((UrlOca, null)), AtrasoApi = TimeSpan.FromSeconds(5) };
        using var http = new HttpClient(rede);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new TesteVelocidade((IServidorTeste?)null, http, Rapido).ExecutarAsync(null, cts.Token));
        Assert.Equal(0, rede.PedidosCloudflare);
    }

    /// <summary>
    /// Internet em memória: api.fast.com, fast.com, um servidor do fast.com (oca.falso.test) e a
    /// Cloudflare (/__down e /__up). Conta quem foi chamado e como.
    /// </summary>
    private sealed class RedeFalsa : HttpMessageHandler
    {
        public string? RespostaApi { get; init; }
        public string TokenValido { get; init; } = ServidorFast.TokenConhecido;
        public string? PaginaFast { get; init; }
        public string? ScriptFast { get; init; }
        public bool OcaForaDoAr { get; init; }
        public TimeSpan AtrasoApi { get; init; }

        private int _cloudflare, _semTamanho, _aceitos;
        private long _bytesOca;
        public int PedidosCloudflare => _cloudflare;
        public int UploadsSemTamanho => _semTamanho;
        public int UploadsAceitos => _aceitos;
        public long BytesEntreguesPeloOca => Interlocked.Read(ref _bytesOca);
        public ConcurrentBag<string> Urls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct)
        {
            Uri u = pedido.RequestUri!;
            Urls.Add(u.ToString());
            switch (u.Host)
            {
                case "api.fast.com":
                    await Task.Delay(AtrasoApi, ct);
                    bool tokenOk = u.Query.Contains("token=" + TokenValido, StringComparison.Ordinal);
                    return RespostaApi is null ? new(HttpStatusCode.InternalServerError)
                        : !tokenOk ? new(HttpStatusCode.Forbidden)
                        : new(HttpStatusCode.OK) { Content = new StringContent(RespostaApi) };

                case "fast.com":
                    string? corpo = u.AbsolutePath == "/" ? PaginaFast : ScriptFast;
                    return corpo is null ? new(HttpStatusCode.NotFound) : new(HttpStatusCode.OK) { Content = new StringContent(corpo) };

                case "oca.falso.test":
                    if (OcaForaDoAr)
                    {
                        return new(HttpStatusCode.ServiceUnavailable);
                    }

                    if (pedido.Method == HttpMethod.Post && u.AbsolutePath.EndsWith("/range/0-0", StringComparison.Ordinal))
                    {
                        return new(HttpStatusCode.OK); // latência (POST vazio), não conta como upload
                    }

                    if (pedido.Method == HttpMethod.Post)
                    {
                        // O real: envio em partes (sem Content-Length) → 406
                        if (pedido.Content?.Headers.ContentLength is null)
                        {
                            Interlocked.Increment(ref _semTamanho);
                            return new(HttpStatusCode.NotAcceptable);
                        }

                        await pedido.Content.CopyToAsync(Stream.Null, ct);
                        Interlocked.Increment(ref _aceitos);
                        return new(HttpStatusCode.OK);
                    }

                    // GET .../range/0-N → N+1 bytes
                    string faixa = u.AbsolutePath[(u.AbsolutePath.LastIndexOf('-') + 1)..];
                    long n = long.Parse(faixa) + 1;
                    Interlocked.Add(ref _bytesOca, n);
                    return new(HttpStatusCode.OK) { Content = new StreamContent(new Zeros(n)) };

                default: // speed.cloudflare.com
                    Interlocked.Increment(ref _cloudflare);
                    if (pedido.Method == HttpMethod.Post)
                    {
                        await pedido.Content!.CopyToAsync(Stream.Null, ct);
                        return new(HttpStatusCode.OK);
                    }

                    long bytes = long.Parse(u.Query.Split('=')[1]);
                    return new(HttpStatusCode.OK) { Content = new StreamContent(new Zeros(bytes)) };
            }
        }
    }

    /// <summary>Fluxo de N zeros sem alocar N bytes.</summary>
    private sealed class Zeros(long tamanho) : Stream
    {
        private long _lidos;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => tamanho;
        public override long Position { get => _lidos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = (int)Math.Min(count, tamanho - _lidos);
            Array.Clear(buffer, offset, n);
            _lidos += n;
            return n;
        }
    }
}
