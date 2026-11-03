using System.IO;
using VelocimetroInternet.Apresentacao;
using VelocimetroInternet.Config;
using VelocimetroInternet.Medicao;

namespace VelocimetroInternet.Tests;

/// <summary>
/// v1.8.1 — Onda 14 (anexos e aviso de queda) + "sem internet" no widget. Cobre: o evento
/// <see cref="ServicoQualidade.QuedaTerminou"/> (só quando a queda termina AGORA), o texto do aviso,
/// o CSV de testes e quedas, a frase "o Wi-Fi não limitou" e a dica do widget. O "⚠" na barra e a
/// notificação de verdade são conferidos por render/execução (ver walkthrough).
/// </summary>
public sealed class V181QuedasEAnexosTests
{
    private static readonly DateTime T0 = new(2026, 11, 10, 9, 0, 0);

    /// <summary>Serviço com relógio injetado e arquivo temporário (o JSON gravado não importa aqui).</summary>
    private static (ServicoQualidade Servico, Func<DateTime> Agora, Action<int> Avancar) ServicoDeTeste()
    {
        DateTime agora = T0;
        string arquivo = Path.Combine(Path.GetTempPath(), $"sm-qualidade-{Guid.NewGuid():N}.json");
        var s = new ServicoQualidade(arquivo, () => agora);
        return (s, () => agora, segundos => agora = agora.AddSeconds(segundos));
    }

    // ---------------------------------------------------------------- evento "queda terminou"

    [Fact]
    public void QuedaTerminou_DisparaQuandoAInternetVoltaAgora_ComInicioEFim()
    {
        (ServicoQualidade s, Func<DateTime> agora, Action<int> avancar) = ServicoDeTeste();
        var recebidas = new List<Queda>();
        s.QuedaTerminou += recebidas.Add;

        s.Observar(EstadoInternet.Online);
        avancar(5);
        DateTime inicio = agora();
        for (int i = 0; i < 24; i++) // 2 min sem internet, de 5 em 5 s
        {
            s.Observar(EstadoInternet.SemInternet);
            avancar(5);
        }

        DateTime fim = agora();
        s.Observar(EstadoInternet.Online);

        Queda q = Assert.Single(recebidas);
        Assert.Equal(EstadoInternet.SemInternet, q.Tipo);
        Assert.Equal(inicio, q.Inicio);
        Assert.Equal(fim, q.Fim);
    }

    [Fact]
    public void QuedaTerminou_NaoDisparaParaQuedaCurta_NemParaQuedaFechadaPorBuraco()
    {
        (ServicoQualidade s, _, Action<int> avancar) = ServicoDeTeste();
        int disparos = 0;
        s.QuedaTerminou += _ => disparos++;

        // Piscada de 30 s: descartada (DuracaoMinimaQueda = 60 s)
        s.Observar(EstadoInternet.Online);
        for (int i = 0; i < 6; i++) { avancar(5); s.Observar(EstadoInternet.SemInternet); }
        avancar(5);
        s.Observar(EstadoInternet.Online);
        Assert.Equal(0, disparos);

        // Queda de 2 min seguida de um buraco de 10 min (PC suspenso): a queda é GUARDADA (fim = último
        // instante visto), mas ninguém sabe quando a internet voltou → sem aviso
        for (int i = 0; i < 24; i++) { avancar(5); s.Observar(EstadoInternet.SemInternet); }
        avancar(600);
        s.Observar(EstadoInternet.Online);
        Assert.Equal(0, disparos);
    }

    [Fact]
    public void TextoVolta_DizQuantoTempoEOHorario_PorTipo()
    {
        var internet = new Queda(new DateTime(2026, 11, 10, 9, 0, 0), new DateTime(2026, 11, 10, 9, 12, 0), EstadoInternet.SemInternet);
        var rede = new Queda(new DateTime(2026, 11, 10, 22, 58, 0), new DateTime(2026, 11, 10, 23, 1, 0), EstadoInternet.SemRede);

        Assert.Equal("A internet voltou após 12 min (das 09:00 às 09:12).", CalculoRelatorio.TextoVolta(internet));
        Assert.Equal("A rede voltou após 3 min sem conexão (das 22:58 às 23:01).", CalculoRelatorio.TextoVolta(rede));
    }

    // ---------------------------------------------------------------- configuração

    [Fact]
    public void AvisarQuandoInternetVoltar_DesligadoPorPadrao_ESobreviveAoClone()
    {
        var cfg = new Configuracoes();
        Assert.False(cfg.AvisarQuandoInternetVoltar);
        cfg.AvisarQuandoInternetVoltar = true;
        Assert.True(cfg.Clonar().AvisarQuandoInternetVoltar);
    }

    // ---------------------------------------------------------------- CSV

    [Fact]
    public void ParaCsv_TestesEQuedas_EmPtBr_ComDoisBlocos()
    {
        var r = new RegistroQualidade();
        r.AdicionarTeste(new TesteGuardado(new DateTime(2026, 11, 3, 14, 5, 0), 492.5e6, 180.25e6, 7.4, "Wi-Fi", "fast.com (São Luís)",
            new ConexaoNoTeste("Wi-Fi", 2402, 2402, 88, -55, "5 GHz")));
        r.AdicionarTeste(new TesteGuardado(new DateTime(2026, 11, 5, 21, 30, 0), 120e6, 50e6, 0, "Cabo", "speed;cloudflare.com"));
        DateTime t = new(2026, 11, 4, 8, 0, 0);
        for (int s = 0; s <= 180; s += 5)
        {
            r.Observar(t.AddSeconds(s), s < 120 ? EstadoInternet.SemInternet : EstadoInternet.Online);
        }

        ResumoMes resumo = CalculoRelatorio.Resumir(r, 2026, 11, 700);
        string csv = CalculoRelatorio.ParaCsv(resumo);
        string[] linhas = csv.Split(Environment.NewLine);

        Assert.Equal("Status Monitor;novembro de 2026;plano de 700 Mbps", linhas[0]);
        Assert.Contains("Data;Hora;Download (Mbps);Upload (Mbps);Latência (ms);Conexão;Link (Mbps);Sinal (%);Banda;Servidor;Faixa Anatel", linhas);
        // Vírgula decimal, link redondo, sinal, banda, faixa da Anatel (492,5 / 700 = 70 % → entre 40 e 80)
        Assert.Contains("2026-11-03;14:05;492,5;180,3;7;Wi-Fi;2402;88;5 GHz;fast.com (São Luís);entre 40 % e 80 %", linhas);
        // Teste antigo (sem rede): células vazias, latência 0 vazia, servidor com ";" entre aspas; 120/700 = 17 % → abaixo do mínimo
        Assert.Contains("2026-11-05;21:30;120,0;50,0;;Cabo;;;;\"speed;cloudflare.com\";abaixo do mínimo (< 40 %)", linhas);
        Assert.Contains("Início;Fim;Duração (min);Tipo", linhas);
        Assert.Contains("2026-11-04 08:00;2026-11-04 08:02;2,0;Sem internet (roteador conectado)", linhas);
        // Dois blocos separados por linha em branco, na ordem testes → quedas
        Assert.True(Array.IndexOf(linhas, "Testes de velocidade") < Array.IndexOf(linhas, "Quedas"));
    }

    [Fact]
    public void ParaCsv_SemPlano_DizIssoENaoClassifica()
    {
        var r = new RegistroQualidade();
        r.AdicionarTeste(new TesteGuardado(new DateTime(2026, 11, 3, 14, 5, 0), 100e6, 10e6, 5, "Cabo", "fast.com"));
        string csv = CalculoRelatorio.ParaCsv(CalculoRelatorio.Resumir(r, 2026, 11, 0));
        Assert.StartsWith("Status Monitor;novembro de 2026;plano não informado", csv);
        Assert.Contains("2026-11-03;14:05;100,0;10,0;5;Cabo;;;;fast.com;" + Environment.NewLine, csv);
    }

    // ---------------------------------------------------------------- "o Wi-Fi não limitou"

    private static TesteGuardado TesteWiFi(int dia, double downMbps, double? linkMbps) =>
        new(new DateTime(2026, 11, dia, 10, 0, 0), downMbps * 1e6, 50e6, 8, "Wi-Fi", "fast.com",
            linkMbps is double l ? new ConexaoNoTeste("Wi-Fi", l, l, 80, -60, "5 GHz") : null);

    [Fact]
    public void Resumir_TodosOsWiFiComLinkAcimaDoPlano_DizQueOWiFiNaoLimitou()
    {
        var r = new RegistroQualidade();
        r.AdicionarTeste(TesteWiFi(1, 480, 2402));
        r.AdicionarTeste(TesteWiFi(2, 510, 1729));
        ResumoMes resumo = CalculoRelatorio.Resumir(r, 2026, 11, 700);

        string frase = Assert.Single(resumo.Conclusoes, c => c.Contains("Wi-Fi", StringComparison.Ordinal));
        Assert.Equal("Todos os 2 teste(s) foram por Wi-Fi, com link entre 1,7 Gbps e 2,4 Gbps, acima do plano de 700 Mbps: o Wi-Fi não limitou estes testes.", frase);
        Assert.Equal(0, resumo.TestesLimitadosPeloLink);
    }

    [Fact]
    public void Resumir_WiFiMisturadoComCabo_FraseFalaSoDosWiFi()
    {
        var r = new RegistroQualidade();
        r.AdicionarTeste(TesteWiFi(1, 480, 2402));
        r.AdicionarTeste(new TesteGuardado(new DateTime(2026, 11, 2, 10, 0, 0), 650e6, 300e6, 5, "Cabo", "fast.com",
            new ConexaoNoTeste("Cabo", 1000, 1000)));
        ResumoMes resumo = CalculoRelatorio.Resumir(r, 2026, 11, 700);

        Assert.Contains("1 de 2 teste(s) foram por Wi-Fi, com link de 2,4 Gbps, acima do plano de 700 Mbps: o Wi-Fi não limitou esses testes.", resumo.Conclusoes);
    }

    [Fact]
    public void Resumir_AlgumWiFiSemLinkOuSemPlano_MantemOAvisoGenerico()
    {
        var r = new RegistroQualidade();
        r.AdicionarTeste(TesteWiFi(1, 480, 2402));
        r.AdicionarTeste(TesteWiFi(2, 470, null)); // gravado pela v1.4.0, sem link
        Assert.Contains(CalculoRelatorio.Resumir(r, 2026, 11, 700).Conclusoes,
            c => c.Contains("a operadora pode atribuir a lentidão ao Wi-Fi", StringComparison.Ordinal));

        var so = new RegistroQualidade();
        so.AdicionarTeste(TesteWiFi(1, 480, 2402));
        Assert.Contains(CalculoRelatorio.Resumir(so, 2026, 11, 0).Conclusoes, // sem plano
            c => c.Contains("a operadora pode atribuir a lentidão ao Wi-Fi", StringComparison.Ordinal));
    }

    [Fact]
    public void Resumir_WiFiComLinkAbaixoDoPlano_ContinuaNaFraseDeLimite()
    {
        var r = new RegistroQualidade();
        r.AdicionarTeste(TesteWiFi(1, 400, 432));
        ResumoMes resumo = CalculoRelatorio.Resumir(r, 2026, 11, 700);
        Assert.Equal(1, resumo.TestesLimitadosPeloLink);
        Assert.DoesNotContain(resumo.Conclusoes, c => c.Contains("não limitou", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- dica do widget

    [Fact]
    public void Dica_SemInternet_ExplicaOAlerta_MantendoARede()
    {
        var h = new InstantaneoHistorico([], 0, 0, 1_000, 2_000, 10_000, 20_000, 2_402_000_000, "Wi-Fi");
        string dica = DicaWidget.Montar(h, "192.168.0.10", ModoUnidadeVelocidade.BytesPorSegundo, 0, semConexao: false, semInternet: true);
        string[] linhas = dica.Split(Environment.NewLine);

        Assert.Equal("Wi-Fi · IP 192.168.0.10", linhas[0]);
        Assert.Equal("⚠ Sem internet: o PC está ligado ao roteador, mas a internet não responde", linhas[1]);

        // Sem placa, o "sem internet" não se aplica (não há roteador para estar ligado)
        string semRede = DicaWidget.Montar(h, null, ModoUnidadeVelocidade.BytesPorSegundo, 0, semConexao: true, semInternet: true);
        Assert.DoesNotContain("⚠", semRede);
    }
}
