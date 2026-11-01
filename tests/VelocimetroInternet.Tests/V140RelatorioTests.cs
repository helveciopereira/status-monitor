using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using VelocimetroInternet.Apresentacao;
using VelocimetroInternet.Medicao;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Tests;

/// <summary>
/// v1.4.0 — Onda 9: relatório para a operadora. Cobre a detecção de quedas (registro puro, com
/// relógio passado pelo teste), o resumo do mês contra as metas da Anatel e o gravador de PDF.
/// O desenho das folhas e a leitura real do Windows (NLM) são conferidos por render/execução.
/// </summary>
public sealed class V140RelatorioTests
{
    private const string Nbsp = " ";
    private static readonly DateTime T0 = new(2026, 11, 10, 9, 0, 0);

    /// <summary>Observa um estado a cada 5 s por <paramref name="segundos"/> e devolve o instante seguinte.</summary>
    private static DateTime Por(RegistroQualidade r, DateTime de, int segundos, EstadoInternet e)
    {
        for (int s = 0; s < segundos; s += 5)
        {
            r.Observar(de.AddSeconds(s), e);
        }

        return de.AddSeconds(segundos);
    }

    // ---------------------------------------------------------------- quedas

    [Fact]
    public void Queda_DeUmMinutoOuMais_EGuardadaComInicioEFim()
    {
        var r = new RegistroQualidade();
        DateTime t = Por(r, T0, 60, EstadoInternet.Online);
        DateTime fim = Por(r, t, 120, EstadoInternet.SemInternet);
        r.Observar(fim, EstadoInternet.Online);

        Queda q = Assert.Single(r.Quedas);
        Assert.Equal(EstadoInternet.SemInternet, q.Tipo);
        Assert.Equal(t, q.Inicio);
        Assert.Equal(fim, q.Fim);
    }

    [Fact]
    public void Queda_Curta_EDescartada()
    {
        var r = new RegistroQualidade();
        DateTime t = Por(r, T0, 30, EstadoInternet.SemInternet); // 30 s: piscada
        r.Observar(t, EstadoInternet.Online);
        Assert.Empty(r.Quedas);
    }

    [Fact]
    public void Queda_TrocaDeTipo_FechaUmaEAbreOutra()
    {
        var r = new RegistroQualidade();
        DateTime t = Por(r, T0, 90, EstadoInternet.SemRede);
        t = Por(r, t, 90, EstadoInternet.SemInternet);
        r.Observar(t, EstadoInternet.Online);

        Assert.Equal([EstadoInternet.SemRede, EstadoInternet.SemInternet], r.Quedas.Select(q => q.Tipo));
        Assert.Equal(r.Quedas[0].Fim, r.Quedas[1].Inicio);
    }

    [Fact]
    public void Lacuna_FechaAQuedaNoUltimoInstanteVisto_ENaoContaTempo()
    {
        var r = new RegistroQualidade();
        DateTime t = Por(r, T0, 120, EstadoInternet.SemInternet);
        DateTime ultimo = t.AddSeconds(-5);
        // PC suspenso por 2 h: a próxima observação vem bem depois
        r.Observar(t.AddHours(2), EstadoInternet.Online);

        Queda q = Assert.Single(r.Quedas);
        Assert.Equal(ultimo, q.Fim); // nunca "2 h sem internet" que ninguém viu
        Assert.Equal(115, r.SegundosObservados[DateOnly.FromDateTime(T0)], 3);
    }

    [Fact]
    public void Encerrar_FechaAQuedaAberta()
    {
        var r = new RegistroQualidade();
        Por(r, T0, 300, EstadoInternet.SemInternet);
        Assert.NotNull(r.QuedaAberta(T0.AddMinutes(5)));
        Assert.True(r.Encerrar());
        Assert.Single(r.Quedas);
        Assert.Null(r.QuedaAberta(T0.AddMinutes(5)));
    }

    [Fact]
    public void TempoObservado_VaiParaODiaCerto()
    {
        var r = new RegistroQualidade();
        DateTime t = Por(r, new DateTime(2026, 11, 10, 23, 59, 0), 120, EstadoInternet.Online);
        Assert.Equal(55, r.SegundosObservados[new DateOnly(2026, 11, 10)], 3);
        Assert.Equal(60, r.SegundosObservados[new DateOnly(2026, 11, 11)], 3);
        Assert.Equal(new DateTime(2026, 11, 11, 0, 1, 0), t);
    }

    [Fact]
    public void Json_IdaEVolta_EPoda()
    {
        var r = new RegistroQualidade();
        DateTime t = Por(r, T0, 120, EstadoInternet.SemInternet);
        r.Observar(t, EstadoInternet.Online);
        r.AdicionarTeste(new TesteGuardado(T0, 500e6, 200e6, 12, "Cabo", "GRU"));
        r.AdicionarTeste(new TesteGuardado(T0.AddDays(-500), 1e6, 1e6, 1, "Wi-Fi", "GRU")); // velho

        RegistroQualidade lido = RegistroQualidade.DeJson(r.ParaJson());
        Assert.Equal(2, lido.Testes.Count);
        Assert.Equal(r.Quedas, lido.Quedas);
        Assert.Equal(r.SegundosObservados[DateOnly.FromDateTime(T0)], lido.SegundosObservados[DateOnly.FromDateTime(T0)]);

        lido.Podar(T0);
        Assert.Equal("Cabo", Assert.Single(lido.Testes).Conexao);
    }

    [Fact]
    public void Servico_GravaTesteNoDisco_ERelêNaProximaAbertura()
    {
        string arq = Path.Combine(Path.GetTempPath(), $"qualidade-{Guid.NewGuid():N}.json");
        try
        {
            var s = new ServicoQualidade(arq, () => T0);
            s.AdicionarTeste(new TesteGuardado(T0, 350e6, 100e6, 9, "Cabo", "GRU"));

            var s2 = new ServicoQualidade(arq, () => T0);
            ResumoMes m = s2.Resumir(2026, 11, 700);
            Assert.Single(m.Testes);
            Assert.Equal([(2026, 11)], s2.MesesComDados());
        }
        finally
        {
            File.Delete(arq);
        }
    }

    // ---------------------------------------------------------------- Anatel

    [Theory]
    [InlineData(560e6, 700, FaixaAnatel.Dentro)]         // exatamente 80 %
    [InlineData(559.9e6, 700, FaixaAnatel.AbaixoDaMedia)]
    [InlineData(280e6, 700, FaixaAnatel.AbaixoDaMedia)]  // exatamente 40 %
    [InlineData(279.9e6, 700, FaixaAnatel.AbaixoDoMinimo)]
    [InlineData(500e6, 0, FaixaAnatel.SemPlano)]
    public void Classificar_LimitesDaAnatel(double bps, double plano, FaixaAnatel esperada) =>
        Assert.Equal(esperada, CalculoRelatorio.Classificar(bps, plano));

    [Fact]
    public void Pct_ArredondaParaBaixo_ComEspacoInseparavel()
    {
        Assert.Equal($"79{Nbsp}%", CalculoRelatorio.Pct(0.7999));
        Assert.Equal($"100{Nbsp}%", CalculoRelatorio.Pct(1));
    }

    [Theory]
    [InlineData(45, "45 s")]
    [InlineData(12 * 60, "12 min")]
    [InlineData(2 * 3600 + 5 * 60, "2 h 05 min")]
    [InlineData(180 * 3600, "180 h 00 min")]
    public void Duracao_Formatos(int segundos, string esperado) =>
        Assert.Equal(esperado, CalculoRelatorio.Duracao(TimeSpan.FromSeconds(segundos)));

    // ---------------------------------------------------------------- resumo do mês

    private static RegistroQualidade MesDeExemplo()
    {
        var r = new RegistroQualidade();
        r.AdicionarTeste(new TesteGuardado(new DateTime(2026, 11, 2, 20, 0, 0), 630e6, 300e6, 10, "Cabo", "GRU"));  // 90 %
        r.AdicionarTeste(new TesteGuardado(new DateTime(2026, 11, 3, 21, 0, 0), 210e6, 300e6, 30, "Wi-Fi", "GRU")); // 30 %
        r.AdicionarTeste(new TesteGuardado(new DateTime(2026, 10, 31, 23, 0, 0), 1e6, 1e6, 1, "Cabo", "GRU"));      // outro mês
        // 1 h observada em 05/11 com 6 min sem internet e 10 min sem rede
        DateTime t = Por(r, new DateTime(2026, 11, 5, 10, 0, 0), 44 * 60, EstadoInternet.Online);
        t = Por(r, t, 6 * 60, EstadoInternet.SemInternet);
        t = Por(r, t, 10 * 60, EstadoInternet.SemRede);
        r.Observar(t, EstadoInternet.Online);
        return r;
    }

    [Fact]
    public void Resumo_ContaSoOMes_EComparaComOPlano()
    {
        ResumoMes m = CalculoRelatorio.Resumir(MesDeExemplo(), 2026, 11, 700);

        Assert.Equal(2, m.Testes.Count);
        Assert.Equal(420e6, m.MediaDownloadBps!.Value, 3);
        Assert.Equal(0.6, m.FracaoMedia!.Value, 6);
        Assert.Equal(1, m.TestesAbaixoDoMinimo);
        Assert.Equal(1, m.TestesPorWiFi);
        Assert.Single(m.QuedasInternet);
        Assert.Single(m.QuedasRede);
        Assert.Equal(TimeSpan.FromMinutes(6), m.TempoSemInternet);
        // Disponibilidade sobre o tempo COM rede local: 1 - 6 / (60 - 10)
        Assert.Equal(1 - 6.0 / 50, m.Disponibilidade!.Value, 2);
        Assert.Contains(m.Conclusoes, c => c.Contains("ABAIXO dos 80", StringComparison.Ordinal));
        Assert.Contains(m.Conclusoes, c => c.StartsWith("1 de 2 teste(s) ficaram abaixo do mínimo", StringComparison.Ordinal));
        Assert.Contains(m.Conclusoes, c => c.Contains("por Wi-Fi", StringComparison.Ordinal));
    }

    [Fact]
    public void Resumo_QuedaQueAtravessaOMes_ERecortada()
    {
        var r = new RegistroQualidade();
        DateTime t = Por(r, new DateTime(2026, 10, 31, 23, 50, 0), 30 * 60, EstadoInternet.SemInternet);
        r.Observar(t, EstadoInternet.Online);

        ResumoMes nov = CalculoRelatorio.Resumir(r, 2026, 11, 700);
        Assert.Equal(new DateTime(2026, 11, 1), Assert.Single(nov.QuedasInternet).Inicio);
        Assert.Equal(TimeSpan.FromMinutes(20), nov.TempoSemInternet);
    }

    [Fact]
    public void Resumo_SemTestes_SemPlano_DizOQueFazer()
    {
        ResumoMes vazio = CalculoRelatorio.Resumir(new RegistroQualidade(), 2026, 11, 700);
        Assert.Null(vazio.MediaDownloadBps);
        Assert.Null(vazio.Disponibilidade);
        Assert.StartsWith("Nenhum teste", Assert.Single(vazio.Conclusoes));

        var r = new RegistroQualidade();
        r.AdicionarTeste(new TesteGuardado(T0, 300e6, 100e6, 10, "Cabo", "GRU"));
        ResumoMes semPlano = CalculoRelatorio.Resumir(r, 2026, 11, 0);
        Assert.Null(semPlano.FracaoMedia);
        Assert.Contains("Informe a velocidade do plano", semPlano.Conclusoes[0]);
    }

    [Fact]
    public void Resumo_QuedaEmAndamento_Entra()
    {
        var r = new RegistroQualidade();
        Por(r, T0, 600, EstadoInternet.SemInternet);
        ResumoMes m = CalculoRelatorio.Resumir(r, 2026, 11, 700, r.QuedaAberta(T0.AddMinutes(10)));
        Assert.Equal(TimeSpan.FromMinutes(10), m.TempoSemInternet);
    }

    [Fact]
    public void Meses_MaisRecentePrimeiro_ComOAtual()
    {
        List<(int, int)> m = CalculoRelatorio.MesesComDados(MesDeExemplo(), new DateTime(2027, 1, 5));
        Assert.Equal([(2027, 1), (2026, 11), (2026, 10)], m);
    }

    // ---------------------------------------------------------------- PDF

    private static PaginaRgb Pagina(int l, int a, byte cor) => new(l, a, Enumerable.Repeat(cor, l * a * 3).ToArray());

    [Fact]
    public void Pdf_EstruturaValida_XrefApontaParaCadaObjeto()
    {
        byte[] pdf = PdfDeImagens.Gerar([Pagina(40, 60, 255), Pagina(40, 60, 0)], "Relatório — teste");
        string s = Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF-1.4\n", s);
        Assert.EndsWith("%%EOF\n", s);
        Assert.Contains("/Count 2", s);
        Assert.Equal(2, Regex.Matches(s, "/Subtype /Image").Count);

        // startxref → "xref"; cada entrada "n" → "N 0 obj" no deslocamento exato
        int inicioXref = int.Parse(Regex.Match(s, @"startxref\n(\d+)\n").Groups[1].Value);
        Assert.Equal("xref", s.Substring(inicioXref, 4));
        MatchCollection entradas = Regex.Matches(s[inicioXref..], @"(\d{10}) 00000 n \n");
        Assert.Equal(9, entradas.Count); // catálogo, páginas, 3 por folha × 2, info
        for (int i = 0; i < entradas.Count; i++)
        {
            int desloc = int.Parse(entradas[i].Groups[1].Value);
            Assert.StartsWith($"{i + 1} 0 obj", s[desloc..]);
        }

        // Título em UTF-16BE: "Relatório" com acento sobrevive
        Assert.Contains("/Title <FEFF00520065006C0061007400F300720069006F", s);
    }

    [Fact]
    public void Pdf_RecusaEntradaInvalida()
    {
        Assert.Throws<ArgumentException>(() => PdfDeImagens.Gerar([], "x"));
        Assert.Throws<ArgumentException>(() => PdfDeImagens.Gerar([new PaginaRgb(10, 10, new byte[5])], "x"));
    }

    [Fact]
    public void TipoConexao_SemPlaca() => Assert.Equal("—", InfoRede.TipoConexao(null));
}
