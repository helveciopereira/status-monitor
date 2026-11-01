using System.IO;
using VelocimetroInternet.Apresentacao;
using VelocimetroInternet.Config;
using VelocimetroInternet.Medicao;

namespace VelocimetroInternet.Tests;

/// <summary>
/// v1.1.0 — Onda 6: janelas de 1 min / 5 min / 1 h do gráfico do painel (com média e pico) e
/// latência opcional (texto, configuração e dica). O ping em si não é testado aqui: depende da
/// rede da máquina — fica na validação manual.
/// </summary>
public sealed class V110JanelasELatenciaTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), $"sm-v110-{Guid.NewGuid():N}");

    public V110JanelasELatenciaTests() => Directory.CreateDirectory(_pasta);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_pasta, recursive: true);
        }
        catch (IOException)
        {
            // pasta temporária: sobra não quebra o teste
        }
    }

    private HistoricoRede NovoHistorico() =>
        new(Path.Combine(_pasta, $"consumo-{Guid.NewGuid():N}.json"), () => new DateOnly(2026, 10, 31));

    // ---------------------------------------------------------------- JanelaAgregada

    [Fact]
    public void Agregada_PontoEhBytesDoBaldeDivididoPeloTempo()
    {
        var j = new JanelaAgregada(5);
        // 5 medições de 1 s: 1000, 2000, 3000, 4000, 5000 bytes recebidos → 15000 / 5 s = 3000 B/s
        for (int i = 1; i <= 5; i++)
        {
            j.Somar(1, (ulong)(i * 10), (ulong)(i * 1000));
        }

        PontoHistorico p = Assert.Single(j.Pontos());
        Assert.Equal(3000, p.DownloadBps, 6);
        Assert.Equal(30, p.UploadBps, 6);
    }

    [Fact]
    public void Agregada_BaldeIncompletoNaoViraPonto()
    {
        var j = new JanelaAgregada(60);
        for (int i = 0; i < 59; i++)
        {
            j.Somar(1, 1, 1);
        }

        Assert.Empty(j.Pontos());
        j.Somar(1, 1, 1);
        Assert.Single(j.Pontos());
    }

    [Fact]
    public void Agregada_MeioSegundoEmPontoFlutuanteFechaOBaldeNaHoraCerta()
    {
        // 10 × 0,5 s em double pode somar 4,999…: a tolerância fecha o balde de 5 s mesmo assim
        var j = new JanelaAgregada(5);
        for (int i = 0; i < 10; i++)
        {
            j.Somar(0.5, 0, 500);
        }

        Assert.Equal(1000, Assert.Single(j.Pontos()).DownloadBps, 6);
    }

    [Fact]
    public void Agregada_GuardaSo60PontosEDescartaOsMaisAntigos()
    {
        var j = new JanelaAgregada(1);
        for (int i = 1; i <= 75; i++)
        {
            j.Somar(1, 0, (ulong)i);
        }

        PontoHistorico[] pontos = j.Pontos();
        Assert.Equal(JanelaAgregada.Capacidade, pontos.Length);
        Assert.Equal(16, pontos[0].DownloadBps, 6);   // 1..15 saíram
        Assert.Equal(75, pontos[^1].DownloadBps, 6);  // o mais novo fica no fim
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Agregada_TempoInvalidoEhIgnorado(double segundos)
    {
        var j = new JanelaAgregada(1);
        j.Somar(segundos, 1_000_000, 1_000_000);
        j.Somar(1, 0, 10);

        Assert.Equal(10, Assert.Single(j.Pontos()).DownloadBps, 6);
    }

    // ---------------------------------------------------------------- SerieGrafico

    [Fact]
    public void Serie_MediaEPicoDeCadaSentido()
    {
        var s = SerieGrafico.De([new(10, 100), new(30, 300), new(20, 200)], 60);

        Assert.Equal(20, s.MediaUpload, 6);
        Assert.Equal(200, s.MediaDownload, 6);
        Assert.Equal(30, s.PicoUpload, 6);
        Assert.Equal(300, s.PicoDownload, 6);
        Assert.Equal(60, s.Capacidade);
    }

    [Fact]
    public void Serie_VaziaTemMediaZeroSemDividirPorZero()
    {
        var s = SerieGrafico.De([], 60);
        Assert.Equal(0, s.MediaUpload);
        Assert.Equal(0, s.MediaDownload);
    }

    // ---------------------------------------------------------------- HistoricoRede.CapturarJanela

    [Fact]
    public void Historico_JanelasLongasUsamBytesReaisNaoAVelocidadeSuavizada()
    {
        HistoricoRede h = NovoHistorico();
        h.DefinirIntervalo(1);
        // Velocidade "suavizada" 999 (de propósito errada); bytes reais = 2000/s
        for (int i = 0; i < 5; i++)
        {
            h.Registrar(999, 999, 0, 2000, "Wi-Fi", 0);
        }

        SerieGrafico cinco = h.CapturarJanela(JanelaGrafico.CincoMinutos);
        Assert.Equal(2000, Assert.Single(cinco.Pontos).DownloadBps, 6);
        Assert.Equal(JanelaAgregada.Capacidade, cinco.Capacidade);

        // A de 1 min continua sendo a das velocidades (o gráfico de sempre)
        SerieGrafico minuto = h.CapturarJanela(JanelaGrafico.Minuto);
        Assert.Equal(5, minuto.Pontos.Count);
        Assert.Equal(999, minuto.MediaDownload, 6);
        Assert.Equal(HistoricoRede.JanelaSegundos, minuto.Capacidade);

        // 1 h ainda não fechou o 1º minuto
        Assert.Empty(h.CapturarJanela(JanelaGrafico.Hora).Pontos);
    }

    [Fact]
    public void Historico_IntervaloDe2sContaODobroDoTempoPorRegistro()
    {
        HistoricoRede h = NovoHistorico();
        h.DefinirIntervalo(2);
        // 3 registros de 2 s = 6 s ≥ 5 s → fecha um ponto: 3 × 4000 bytes / 6 s = 2000 B/s
        for (int i = 0; i < 3; i++)
        {
            h.Registrar(0, 0, 0, 4000, "Wi-Fi", 0);
        }

        Assert.Equal(2000, Assert.Single(h.CapturarJanela(JanelaGrafico.CincoMinutos).Pontos).DownloadBps, 6);
    }

    [Fact]
    public void Historico_UmaHoraFechaUmPontoPorMinuto()
    {
        HistoricoRede h = NovoHistorico();
        h.DefinirIntervalo(1);
        for (int i = 0; i < 120; i++)
        {
            h.Registrar(0, 0, 60, 600, "Wi-Fi", 0);
        }

        SerieGrafico hora = h.CapturarJanela(JanelaGrafico.Hora);
        Assert.Equal(2, hora.Pontos.Count);
        Assert.Equal(600, hora.MediaDownload, 6);
        Assert.Equal(60, hora.MediaUpload, 6);
    }

    // ---------------------------------------------------------------- códigos e configuração

    [Theory]
    [InlineData("1min", JanelaGrafico.Minuto)]
    [InlineData("5min", JanelaGrafico.CincoMinutos)]
    [InlineData("1h", JanelaGrafico.Hora)]
    [InlineData("lixo", JanelaGrafico.Minuto)]
    [InlineData(null, JanelaGrafico.Minuto)]
    public void Janelas_CodigoIdaEVolta(string? codigo, JanelaGrafico esperada)
    {
        Assert.Equal(esperada, JanelasGrafico.DeCodigo(codigo));
        Assert.Equal(esperada, JanelasGrafico.DeCodigo(JanelasGrafico.Codigo(esperada)));
    }

    [Fact]
    public void Config_PadroesDaOnda6_LatenciaDesligadaEJanelaDe1Min()
    {
        var c = new Configuracoes();
        Assert.False(c.MedirLatencia);
        Assert.Equal("1min", c.JanelaGrafico);
    }

    [Fact]
    public void Config_JanelaDesconhecidaVoltaAoPadrao()
    {
        var c = new Configuracoes { JanelaGrafico = "10min" };
        c.Normalizar();
        Assert.Equal("1min", c.JanelaGrafico);

        c.JanelaGrafico = "1h";
        c.Normalizar();
        Assert.Equal("1h", c.JanelaGrafico);
    }

    // ---------------------------------------------------------------- latência: texto e dica

    [Fact]
    public void Latencia_TextoComESemResposta()
    {
        Assert.Equal("Roteador 3 ms · Internet 18 ms", new LatenciaAtual(3, 18, true).Texto());
        Assert.Equal("Roteador — · Internet 18 ms", new LatenciaAtual(null, 18, true).Texto());
        Assert.Equal("medindo…", LatenciaAtual.Nenhuma.Texto());
    }

    [Fact]
    public void Dica_MostraLatenciaSoQuandoMedida()
    {
        HistoricoRede h = NovoHistorico();
        InstantaneoHistorico inst = h.Capturar();

        string sem = DicaWidget.Montar(inst, null, ModoUnidadeVelocidade.BytesPorSegundo, 0, false);
        string medindo = DicaWidget.Montar(inst, null, ModoUnidadeVelocidade.BytesPorSegundo, 0, false, LatenciaAtual.Nenhuma);
        string com = DicaWidget.Montar(inst, null, ModoUnidadeVelocidade.BytesPorSegundo, 0, false, new LatenciaAtual(2, 25, true));

        Assert.DoesNotContain("Latência", sem);
        Assert.DoesNotContain("Latência", medindo);
        Assert.Contains("Latência: Roteador 2 ms · Internet 25 ms", com);
    }

    [Fact]
    public void Medidor_ComecaDesligadoESemMedicao()
    {
        using var m = new MedidorLatencia(() => null);
        Assert.False(m.Ligado);
        Assert.False(m.Atual.Medido);
    }
}
