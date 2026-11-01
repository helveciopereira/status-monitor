using System.IO;
using VelocimetroInternet.Apresentacao;
using VelocimetroInternet.Config;
using VelocimetroInternet.Medicao;

namespace VelocimetroInternet.Tests;

/// <summary>
/// v1.0.0 — Onda 5 (ideias de baixo esforço do 04-IDEIAS.md): consumo por dia/mês, franquia,
/// CSV, dica do widget, cor por intensidade e regra do duplo clique.
/// </summary>
public sealed class V100ConsumoEDicaTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), $"sm-v100-{Guid.NewGuid():N}");

    public V100ConsumoEDicaTests() => Directory.CreateDirectory(_pasta);

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

    // ---------------------------------------------------------------- registro diário

    [Fact]
    public void Registro_SomaPorDia_EOMesSoContaOMesCalendario()
    {
        var r = new RegistroConsumo();
        r.Somar(new DateOnly(2026, 9, 30), 100, 1000);   // setembro: fora
        r.Somar(new DateOnly(2026, 10, 1), 10, 20);
        r.Somar(new DateOnly(2026, 10, 1), 1, 2);        // mesmo dia: acumula
        r.Somar(new DateOnly(2026, 10, 31), 5, 5);

        Assert.Equal(new ConsumoDia(11, 22), r.Do(new DateOnly(2026, 10, 1)));
        Assert.Equal(new ConsumoDia(16, 27), r.DoMes(new DateOnly(2026, 10, 15)));
        Assert.Equal(43UL, r.DoMes(new DateOnly(2026, 10, 15)).Total);
        Assert.Equal(new ConsumoDia(100, 1000), r.DoMes(new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public void Registro_Podar_DescartaMaisDe400Dias()
    {
        var hoje = new DateOnly(2026, 10, 31);
        var r = new RegistroConsumo();
        r.Somar(hoje.AddDays(-RegistroConsumo.DiasMantidos - 1), 1, 1);
        r.Somar(hoje.AddDays(-RegistroConsumo.DiasMantidos), 2, 2);
        r.Podar(hoje);

        Assert.Single(r.Dias);
        Assert.Equal(new ConsumoDia(2, 2), r.Do(hoje.AddDays(-RegistroConsumo.DiasMantidos)));
    }

    [Fact]
    public void Registro_JsonIdaEVolta_PreservaDiasEAvisos()
    {
        var hoje = new DateOnly(2026, 10, 31);
        var r = new RegistroConsumo();
        r.Somar(hoje, 8_500_000_000, 0);
        Assert.Equal(80, r.MarcoFranquiaParaAvisar(hoje, 10)); // marca o aviso dos 80 %

        RegistroConsumo lido = RegistroConsumo.DeJson(r.ParaJson());
        Assert.Equal(r.Do(hoje), lido.Do(hoje));
        Assert.Null(lido.MarcoFranquiaParaAvisar(hoje, 10)); // o "já avisado" também voltou
    }

    [Fact]
    public void Registro_Json_IgnoraDataInvalida()
    {
        const string json = """{"Versao":1,"Dias":[{"Dia":"lixo","Enviados":1,"Recebidos":1},{"Dia":"2026-10-30","Enviados":3,"Recebidos":4}]}""";
        RegistroConsumo r = RegistroConsumo.DeJson(json);
        Assert.Single(r.Dias);
        Assert.Equal(new ConsumoDia(3, 4), r.Do(new DateOnly(2026, 10, 30)));
    }

    // ---------------------------------------------------------------- franquia

    [Fact]
    public void Franquia_AvisaUmaVezEm80EUmaEm100_PorMes()
    {
        var dia = new DateOnly(2026, 10, 10);
        var r = new RegistroConsumo();

        r.Somar(dia, 0, 7_900_000_000);                       // 79 % de 10 GB
        Assert.Null(r.MarcoFranquiaParaAvisar(dia, 10));
        r.Somar(dia, 100_000_000, 0);                         // 80 %
        Assert.Equal(80, r.MarcoFranquiaParaAvisar(dia, 10));
        Assert.Null(r.MarcoFranquiaParaAvisar(dia, 10));      // não repete
        r.Somar(dia, 0, 2_000_000_000);                       // 100 %
        Assert.Equal(100, r.MarcoFranquiaParaAvisar(dia, 10));
        Assert.Null(r.MarcoFranquiaParaAvisar(dia, 10));

        // Mês seguinte: consumo novo (zero) — nada a avisar até encher de novo
        Assert.Null(r.MarcoFranquiaParaAvisar(new DateOnly(2026, 11, 1), 10));
    }

    [Fact]
    public void Franquia_ComecandoAcimaDe100_AvisaSo100_ESemFranquiaNuncaAvisa()
    {
        var dia = new DateOnly(2026, 10, 10);
        var r = new RegistroConsumo();
        r.Somar(dia, 0, 50_000_000_000);

        Assert.Null(r.MarcoFranquiaParaAvisar(dia, 0));
        Assert.Null(r.MarcoFranquiaParaAvisar(dia, double.NaN));
        Assert.Equal(100, r.MarcoFranquiaParaAvisar(dia, 10));
        Assert.Null(r.MarcoFranquiaParaAvisar(dia, 10));
        Assert.Equal(5.0, r.FracaoFranquia(dia, 10), 6);
    }

    [Fact]
    public void Configuracoes_Franquia_NormalizaValoresRuins()
    {
        var c = new Configuracoes { FranquiaMensalGb = -5 };
        c.Normalizar();
        Assert.Equal(0, c.FranquiaMensalGb);

        c.FranquiaMensalGb = double.NaN;
        c.Normalizar();
        Assert.Equal(0, c.FranquiaMensalGb);

        // Arquivo antigo (sem os campos novos): padrões desligados
        Configuracoes antigo = System.Text.Json.JsonSerializer.Deserialize<Configuracoes>("""{"Unidade":"bits"}""")!;
        Assert.Equal(0, antigo.FranquiaMensalGb);
        Assert.False(antigo.CorPorIntensidade);
    }

    // ---------------------------------------------------------------- CSV

    [Fact]
    public void Csv_PtBr_SeparadorPontoEVirgulaEVirgulaDecimal()
    {
        var r = new RegistroConsumo();
        r.Somar(new DateOnly(2026, 10, 31), 1_500_000, 12_340_000);
        r.Somar(new DateOnly(2026, 10, 30), 0, 0);

        string[] linhas = r.ParaCsv().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Data;Enviado (MB);Recebido (MB);Total (MB)", linhas[0]);
        Assert.Equal("2026-10-30;0,00;0,00;0,00", linhas[1]);      // em ordem de data
        Assert.Equal("2026-10-31;1,50;12,34;13,84", linhas[2]);
    }

    // ---------------------------------------------------------------- HistoricoRede com o registro

    [Fact]
    public void Historico_MesSomaOsDias_ESobreviveAoReinicio()
    {
        string caminho = Path.Combine(_pasta, "consumo.json");
        DateOnly relogio = new(2026, 10, 30);
        var h = new HistoricoRede(caminho, () => relogio);
        h.Registrar(0, 0, 100, 1000, "Wi-Fi", 0);
        relogio = new DateOnly(2026, 10, 31);                 // virou o dia com o app aberto
        h.Registrar(0, 0, 10, 20, "Wi-Fi", 0);
        h.SalvarConsumo();

        InstantaneoHistorico s = new HistoricoRede(caminho, () => relogio).Capturar();
        Assert.Equal(10UL, s.BytesEnviadosHoje);
        Assert.Equal(20UL, s.BytesRecebidosHoje);
        Assert.Equal(110UL, s.BytesEnviadosMes);
        Assert.Equal(1020UL, s.BytesRecebidosMes);

        // Mês seguinte: hoje e mês zerados, mas o histórico continua no CSV
        var novembro = new HistoricoRede(caminho, () => new DateOnly(2026, 11, 1));
        Assert.Equal(0UL, novembro.Capturar().BytesRecebidosMes);
        Assert.Contains("2026-10-30", novembro.ExportarCsv());
    }

    [Fact]
    public void Historico_MigraOConsumoJsonAntigo()
    {
        // Formato até a v0.9.0: só o dia em que foi gravado
        string caminho = Path.Combine(_pasta, "consumo.json");
        File.WriteAllText(caminho, """{"Dia":"2026-10-29","Enviados":7,"Recebidos":9}""");

        var h = new HistoricoRede(caminho, () => new DateOnly(2026, 10, 31));
        InstantaneoHistorico s = h.Capturar();
        Assert.Equal(0UL, s.BytesRecebidosHoje);  // outro dia: não é "hoje"…
        Assert.Equal(9UL, s.BytesRecebidosMes);   // …mas agora conta no mês (antes era jogado fora)
    }

    [Fact]
    public void Historico_ComOsDoisArquivos_FicaOMaiorDeCadaCampo_SemSomar()
    {
        // A 0.9.0 rodou depois da 1.0.0 no mesmo dia: o consumo.json antigo está à frente no download
        string caminho = Path.Combine(_pasta, "consumo.json");
        var dia = new DateOnly(2026, 10, 31);
        var nova = new HistoricoRede(caminho, () => dia);
        nova.Registrar(0, 0, 500, 100, "Wi-Fi", 0);
        nova.SalvarConsumo();
        File.WriteAllText(caminho, """{"Dia":"2026-10-31","Enviados":200,"Recebidos":900}""");

        InstantaneoHistorico s = new HistoricoRede(caminho, () => dia).Capturar();
        Assert.Equal(500UL, s.BytesEnviadosHoje);   // o registro novo estava à frente no envio
        Assert.Equal(900UL, s.BytesRecebidosHoje);  // o antigo, no download — nada somado
    }

    // ---------------------------------------------------------------- dica e intensidade

    [Fact]
    public void Dica_MostraInterfaceIpConsumoEFranquia()
    {
        var h = new InstantaneoHistorico([], 0, 0, 1_500_000, 12_000_000, 1_288_490_189, 15_891_378_995, 0, "Wi-Fi",
            BytesEnviadosMes: 2_000_000_000, BytesRecebidosMes: 6_000_000_000);

        string dica = DicaWidget.Montar(h, "192.168.0.10", ModoUnidadeVelocidade.BytesPorSegundo, 0.826, semConexao: false);
        string[] linhas = dica.Split(Environment.NewLine);

        Assert.Equal("Wi-Fi · IP 192.168.0.10", linhas[0]);
        Assert.Equal("Hoje: ↑ 1,2 GB · ↓ 14,8 GB", linhas[1]);
        Assert.StartsWith("Mês: ↑ ", linhas[2]);
        Assert.EndsWith("(82 % da franquia)", linhas[2]);
        Assert.StartsWith("Pico em 60 s: ↑ ", linhas[3]);
        Assert.Contains("Duplo clique", linhas[4]);
    }

    [Fact]
    public void Dica_SemConexaoESemFranquia()
    {
        var h = new InstantaneoHistorico([], 0, 0, 0, 0, 0, 0, 0, "");
        string dica = DicaWidget.Montar(h, null, ModoUnidadeVelocidade.BitsPorSegundo, 0, semConexao: true);

        Assert.StartsWith("Sem conexão de rede", dica);
        Assert.DoesNotContain("franquia", dica);
    }

    [Theory]
    [InlineData(0.996, "99 %")]   // não arredonda para 100 antes da hora
    [InlineData(1.5, "150 %")]
    [InlineData(-1, "0 %")]
    public void Percentual_ArredondaParaBaixo(double fracao, string esperado) =>
        Assert.Equal(esperado, DicaWidget.TextoPercentual(fracao));

    [Fact]
    public void Intensidade_DeMinimoAteCheio_ERaizQuadrada()
    {
        Assert.Equal(1.0, DicaWidget.OpacidadePorIntensidade(123, 0));                     // desligada
        Assert.Equal(DicaWidget.OpacidadeMinima, DicaWidget.OpacidadePorIntensidade(0, 1000));
        Assert.Equal(1.0, DicaWidget.OpacidadePorIntensidade(5000, 1000));                 // acima: teto
        Assert.Equal(0.75, DicaWidget.OpacidadePorIntensidade(250, 1000), 6);              // √0,25 = 0,5
        Assert.Equal(1.0, DicaWidget.OpacidadePorIntensidade(double.NaN, 1000));
    }

    // ---------------------------------------------------------------- duplo clique

    [Theory]
    [InlineData(300, 1, 1, true)]
    [InlineData(600, 0, 0, false)]   // passou do tempo do Windows (500 ms)
    [InlineData(100, 3, 0, false)]   // saiu do retângulo de 4 px (± 2)
    [InlineData(100, -2, 2, true)]
    [InlineData(-5, 0, 0, false)]    // relógio "voltou": nunca é duplo
    public void DuploClique_SegueTempoEDistanciaDoWindows(long ms, int dx, int dy, bool esperado) =>
        Assert.Equal(esperado, RegraDuploClique.EhDuplo(ms, dx, dy, 500, 4, 4));
}
