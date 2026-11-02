using VelocimetroInternet.Medicao;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Tests;

/// <summary>
/// v1.5.0 — Onda 10: Wi-Fi explicado no teste e no relatório. Cobre os textos e a regra "o link
/// limita o download", a compatibilidade com o qualidade.json da v1.4.0 e o resumo do mês com cabo ×
/// Wi-Fi. A leitura REAL do Wi-Fi (WLAN API) foi conferida contra o `netsh wlan show interfaces`.
/// </summary>
public sealed class V150ConexaoTests
{
    private const string Nbsp = " ";
    private static readonly ConexaoNoTeste WiFi432 = new("Wi-Fi", 432.4, 216, 66, -67, "5 GHz");
    private static readonly ConexaoNoTeste Cabo1G = new("Cabo", 1000, 1000);

    [Theory]
    [InlineData(432.4, "432 Mbps")]
    [InlineData(99.6, "100 Mbps")]
    [InlineData(1000, "1 Gbps")]
    [InlineData(2402, "2,4 Gbps")]
    [InlineData(2500, "2,5 Gbps")]
    public void Link_FormataComoNumeroRedondo(double mbps, string esperado) =>
        Assert.Equal(esperado, ConexaoNoTeste.Link(mbps));

    [Fact]
    public void Descricao_WiFiCompleto_CaboEAntigo()
    {
        Assert.Equal($"Wi-Fi 5 GHz · sinal 66{Nbsp}% · link 432 Mbps", WiFi432.Descricao());
        Assert.Equal("Cabo · link 1 Gbps", Cabo1G.Descricao());
        Assert.Equal("Wi-Fi", new ConexaoNoTeste("Wi-Fi").Descricao()); // teste da v1.4.0: só o tipo
    }

    [Fact]
    public void LimitaODownload_SoComLinkConhecidoAbaixoDoPlano()
    {
        Assert.True(WiFi432.LimitaODownload(700));
        Assert.False(WiFi432.LimitaODownload(400));                     // link acima do plano
        Assert.False(WiFi432.LimitaODownload(0));                       // sem plano: não desculpa ninguém
        Assert.False(new ConexaoNoTeste("Wi-Fi").LimitaODownload(700)); // link desconhecido
        Assert.False(new ConexaoNoTeste("Cabo", 700).LimitaODownload(700)); // igual ao plano não limita
        Assert.True(new ConexaoNoTeste("Cabo", 100, 100).LimitaODownload(700));
    }

    [Fact]
    public void AvisoDeLimite_DizDeQuemEOLimiteEOQueFazer()
    {
        Assert.Null(Cabo1G.AvisoDeLimite(700));
        string wifi = WiFi432.AvisoDeLimite(700)!;
        Assert.Contains("700 Mbps", wifi);
        Assert.Contains("teste por cabo", wifi);
        string cabo = new ConexaoNoTeste("Cabo", 100, 100).AvisoDeLimite(700)!;
        Assert.Contains("100 Mbps", cabo); // dica do cabo antigo
        Assert.DoesNotContain("Wi-Fi", cabo);
    }

    [Fact]
    public void Json_DaV140SemRede_ContinuaLido()
    {
        // Formato exato gravado pela v1.4.0 (sem o campo "Rede")
        const string v140 = """
            {"Versao":1,"Testes":[{"Quando":"2026-11-01T20:00:00","DownloadBps":5E8,"UploadBps":2E8,"LatenciaMs":12,"Conexao":"Wi-Fi","Servidor":"GRU"}],"Quedas":[],"SegundosObservados":{}}
            """;
        TesteGuardado t = Assert.Single(RegistroQualidade.DeJson(v140).Testes);
        Assert.Null(t.Rede);
        Assert.Equal("Wi-Fi", t.ConexaoCompleta.Tipo);
        Assert.Null(t.ConexaoCompleta.LinkDownMbps);
    }

    [Fact]
    public void Json_ComRede_IdaEVolta_SemCamposCalculados()
    {
        var r = new RegistroQualidade();
        r.AdicionarTeste(new TesteGuardado(new DateTime(2026, 11, 2, 8, 0, 0), 371e6, 180e6, 14, "Wi-Fi", "GRU", WiFi432));
        string json = r.ParaJson();
        Assert.DoesNotContain("ConexaoCompleta", json);
        Assert.DoesNotContain("EhWiFi", json);
        TesteGuardado lido = Assert.Single(RegistroQualidade.DeJson(json).Testes);
        Assert.Equal(WiFi432, lido.Rede);
    }

    [Fact]
    public void Resumir_SeparaCaboDeWiFi_EContaOsLimitadosPeloLink()
    {
        var r = new RegistroQualidade();
        r.AdicionarTeste(new TesteGuardado(new DateTime(2026, 11, 2, 8, 0, 0), 600e6, 300e6, 10, "Cabo", "GRU", Cabo1G));
        r.AdicionarTeste(new TesteGuardado(new DateTime(2026, 11, 3, 8, 0, 0), 500e6, 300e6, 10, "Cabo", "GRU", Cabo1G));
        r.AdicionarTeste(new TesteGuardado(new DateTime(2026, 11, 4, 8, 0, 0), 350e6, 150e6, 20, "Wi-Fi", "GRU", WiFi432));
        r.AdicionarTeste(new TesteGuardado(new DateTime(2026, 11, 5, 8, 0, 0), 330e6, 150e6, 20, "Wi-Fi", "GRU",
            WiFi432 with { LinkDownMbps = 390 }));

        ResumoMes m = CalculoRelatorio.Resumir(r, 2026, 11, 700);

        Assert.Equal(550e6, m.MediaCaboBps);
        Assert.Equal(340e6, m.MediaWiFiBps);
        Assert.Equal(2, m.TestesLimitadosPeloLink);
        Assert.Contains(m.Conclusoes, c => c.StartsWith("Por cabo: média 550 Mbps (78" + Nbsp + "% do plano) em 2 teste(s). Por Wi-Fi: 340 Mbps (48" + Nbsp + "%) em 2.", StringComparison.Ordinal));
        Assert.Contains(m.Conclusoes, c => c.Contains("2 de 4 teste(s)") && c.Contains("entre 390 Mbps e 432 Mbps pelo Wi-Fi"));
        // A frase genérica de Wi-Fi da v1.4.0 dá lugar à explicação do link
        Assert.DoesNotContain(m.Conclusoes, c => c.Contains("podem atribuir") || c.Contains("pode atribuir"));
    }

    [Fact]
    public void Resumir_TestesAntigosSemLink_MantemOAvisoGenericoDeWiFi()
    {
        var r = new RegistroQualidade();
        r.AdicionarTeste(new TesteGuardado(new DateTime(2026, 11, 2, 8, 0, 0), 300e6, 100e6, 10, "Wi-Fi", "GRU"));

        ResumoMes m = CalculoRelatorio.Resumir(r, 2026, 11, 700);

        Assert.Equal(0, m.TestesLimitadosPeloLink);
        Assert.Null(m.MediaCaboBps);
        Assert.Contains(m.Conclusoes, c => c.Contains("pode atribuir a lentidão ao Wi-Fi"));
        Assert.DoesNotContain(m.Conclusoes, c => c.StartsWith("Por cabo", StringComparison.Ordinal)); // só existe com os dois tipos
    }

    [Fact]
    public void Resumir_SemPlano_NaoMarcaNenhumTesteComoLimitado()
    {
        var r = new RegistroQualidade();
        r.AdicionarTeste(new TesteGuardado(new DateTime(2026, 11, 2, 8, 0, 0), 300e6, 100e6, 10, "Wi-Fi", "GRU", WiFi432));
        Assert.Equal(0, CalculoRelatorio.Resumir(r, 2026, 11, 0).TestesLimitadosPeloLink);
    }

    [Theory]
    [InlineData(2_437_000u, "2,4 GHz")]
    [InlineData(5_220_000u, "5 GHz")]
    [InlineData(5_955_000u, "6 GHz")]
    [InlineData(0u, null)]
    public void Banda_PelaFrequenciaCentral(uint khz, string? esperado) =>
        Assert.Equal(esperado, InfoWiFi.Banda(khz));

    [Fact]
    public void ConexaoAtual_PlacaNaoWiFi_UsaAVelocidadeDaPorta_EDescartaODesconhecido()
    {
        // Apelido que não existe → não é Wi-Fi → usa a velocidade da porta nos dois sentidos
        ConexaoNoTeste c = InfoRede.ConexaoAtual("PlacaQueNaoExiste-Teste", 1_000_000_000);
        Assert.Equal(1000, c.LinkDownMbps);
        Assert.Equal(1000, c.LinkUpMbps);
        // ulong.MaxValue = "desconhecido" na API do Windows; 0 = sem link
        Assert.Null(InfoRede.ConexaoAtual("PlacaQueNaoExiste-Teste", ulong.MaxValue).LinkDownMbps);
        Assert.Null(InfoRede.ConexaoAtual("PlacaQueNaoExiste-Teste", 0).LinkDownMbps);
    }
}
