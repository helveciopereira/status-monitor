using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using VelocimetroInternet.Apresentacao;
using VelocimetroInternet.Config;
using VelocimetroInternet.Medicao;
using VelocimetroInternet.Recursos;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Tests;

/// <summary>
/// v1.9.0 — acesso amplo: interface em inglês. Cobre a escolha do idioma (auto/pt-BR/en), a guarda
/// da configuração, a PARIDADE dos dois arquivos de recursos (toda chave pt-BR existe em inglês e
/// vice-versa, com os mesmos marcadores {n}) e os formatadores que recebem a cultura por parâmetro.
///
/// Regra: NUNCA chamar <see cref="Idioma.Aplicar"/> aqui — a cultura é global e o xunit roda as
/// classes em paralelo; os formatadores recebem a cultura explicitamente.
/// </summary>
public sealed class V190IdiomaTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    // ------------------------------------------------------------------ escolha do idioma

    [Theory]
    [InlineData("auto", "pt-BR", "pt-BR")]  // Windows em português → português
    [InlineData("auto", "pt-PT", "pt-BR")]  // qualquer português → o nosso (não há textos pt-PT)
    [InlineData("auto", "en-GB", "en-US")]
    [InlineData("auto", "fr-FR", "en-US")]  // idioma sem tradução → inglês (melhor que português)
    [InlineData("pt-BR", "en-US", "pt-BR")] // forçado ignora o Windows
    [InlineData("en", "pt-BR", "en-US")]
    [InlineData("lixo", "de-DE", "en-US")]  // valor inválido = automático
    [InlineData(null, "pt-BR", "pt-BR")]
    public void Resolver_SegueAEscolha_OuOWindowsNoAutomatico(string? escolha, string windows, string esperado) =>
        Assert.Equal(esperado, Idioma.Resolver(escolha, CultureInfo.GetCultureInfo(windows)).Name);

    [Theory]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("en", "en")]
    [InlineData(" en ", "en")]
    [InlineData("auto", "auto")]
    [InlineData("en-US", "auto")] // só os dois códigos exatos valem
    [InlineData("", "auto")]
    [InlineData(null, "auto")]
    public void Normalizar_SoAceitaOsTresValores(string? entrada, string esperado) =>
        Assert.Equal(esperado, Idioma.Normalizar(entrada));

    [Fact]
    public void Configuracoes_Idioma_PadraoAutomatico_EGuardaContraValorInvalido()
    {
        var cfg = new Configuracoes();
        Assert.Equal("auto", cfg.Idioma);

        cfg.Idioma = "klingon";
        cfg.Normalizar();
        Assert.Equal("auto", cfg.Idioma);

        cfg.Idioma = "en";
        cfg.Normalizar();
        Assert.Equal("en", cfg.Idioma);
    }

    // ------------------------------------------------------------------ paridade dos recursos

    private static Dictionary<string, string> Chaves(CultureInfo cultura, bool tentarPai)
    {
        ResourceSet? conjunto = Textos.ResourceManager.GetResourceSet(cultura, createIfNotExists: true, tryParents: tentarPai);
        Assert.NotNull(conjunto);
        return conjunto!.Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    [Fact]
    public void Recursos_TodaChaveExisteNosDoisIdiomas_SemValorVazio()
    {
        Dictionary<string, string> pt = Chaves(CultureInfo.InvariantCulture, tentarPai: true);
        Dictionary<string, string> en = Chaves(CultureInfo.GetCultureInfo("en"), tentarPai: false);

        Assert.True(pt.Count > 250, $"pt-BR com só {pt.Count} chaves — o Designer tem 274");
        Assert.Empty(pt.Keys.Except(en.Keys)); // falta em inglês
        Assert.Empty(en.Keys.Except(pt.Keys)); // sobra em inglês (chave morta)
        Assert.All(pt.Values, v => Assert.False(string.IsNullOrWhiteSpace(v)));
        Assert.All(en.Values, v => Assert.False(string.IsNullOrWhiteSpace(v)));
    }

    [Fact]
    public void Recursos_MarcadoresDeFormato_IguaisNosDoisIdiomas()
    {
        Dictionary<string, string> pt = Chaves(CultureInfo.InvariantCulture, tentarPai: true);
        Dictionary<string, string> en = Chaves(CultureInfo.GetCultureInfo("en"), tentarPai: false);
        var marcador = new Regex(@"\{(\d+)[^}]*\}");

        foreach ((string chave, string textoPt) in pt)
        {
            var mPt = marcador.Matches(textoPt).Select(m => m.Groups[1].Value).OrderBy(x => x).ToList();
            var mEn = marcador.Matches(en[chave]).Select(m => m.Groups[1].Value).OrderBy(x => x).ToList();
            Assert.True(mPt.SequenceEqual(mEn), $"{chave}: pt usa {{{string.Join(",", mPt)}}}, en usa {{{string.Join(",", mEn)}}}");
        }
    }

    [Fact]
    public void Recursos_TextoPorCultura_EChaveQuandoFalta()
    {
        Assert.Equal("Fechar", Textos.Obter("Comum_Fechar", PtBr));
        Assert.Equal("Close", Textos.Obter("Comum_Fechar", En));
        Assert.Equal("Chave_Inexistente", Textos.Obter("Chave_Inexistente", En)); // nunca null na tela
        // O padrão (sem Aplicar) é pt-BR: o comportamento de sempre
        Assert.Equal("pt-BR", Textos.CulturaAtual.Name);
        Assert.Equal("Fechar", Textos.Comum_Fechar);
    }

    // ------------------------------------------------------------------ formatadores com cultura

    [Fact]
    public void Formatadores_PontoDecimalEmIngles_VirgulaEmPortugues()
    {
        // Quantidades são em base 1024 (como o Explorador): 1,5 GB = 1,5 × 1024³ bytes
        const ulong umGbEMeio = 1_610_612_736;
        Assert.Equal("1.5 GB", SpeedFormatter.FormatarQuantidade(umGbEMeio, En));
        Assert.Equal("1,5 GB", SpeedFormatter.FormatarQuantidade(umGbEMeio, PtBr));

        (string n, string u) = CalculoTeste.FormatarMbps(94_300_000, En);
        Assert.Equal(("94.3", "Mbps"), (n, u));
        Assert.Equal("94,3", CalculoTeste.FormatarMbps(94_300_000, PtBr).Numero);
        Assert.Equal("94,3", CalculoTeste.FormatarMbps(94_300_000).Numero); // padrão = pt-BR

        Assert.Equal("2.4 Gbps", ConexaoNoTeste.Link(2402, En));
        Assert.Equal("2,4 Gbps", ConexaoNoTeste.Link(2402, PtBr));
    }

    [Fact]
    public void Percentual_InglesSemEspaco_PortuguesComEspaco()
    {
        Assert.Equal("82%", DicaWidget.TextoPercentual(0.82, En));
        Assert.Equal("82 %", DicaWidget.TextoPercentual(0.82, PtBr));
        Assert.Equal("99.9%", DicaWidget.TextoPercentual(0.99996, En, casas: 1)); // para baixo, 1 casa
        Assert.Equal("99,9 %", DicaWidget.TextoPercentual(0.99996, PtBr, casas: 1));
    }

    [Fact]
    public void ConexaoDescricao_EmIngles_ENomeDoMes()
    {
        var wifi = new ConexaoNoTeste("Wi-Fi", 432.4, 216, 66, -67, "5 GHz");
        Assert.Equal("Wi-Fi 5 GHz · signal 66% · link 432 Mbps", wifi.Descricao(En));
        Assert.Equal($"Wi-Fi 5 GHz · sinal 66 % · link 432 Mbps", wifi.Descricao(PtBr));

        Assert.Equal("novembro de 2026", CalculoRelatorio.NomeMes(2026, 11));       // o do PDF (pt-BR)
        Assert.Equal("November 2026", CalculoRelatorio.NomeMes(2026, 11, En));      // o da janela em inglês
    }

    [Fact]
    public void Atualizacao_MotivoTraduzido_ELogContinuaEmPortugues()
    {
        var ex = new TimeoutException();
        Assert.Equal(ServicoAtualizacao.MotivoFalha.TempoEsgotado, ServicoAtualizacao.ClassificarMotivo(ex));
        Assert.Equal("tempo esgotado", ServicoAtualizacao.Classificar(ex)); // o log/teste da Onda 4
        Assert.Equal("timed out", ServicoAtualizacao.TextoMotivo(ServicoAtualizacao.MotivoFalha.TempoEsgotado, null, En));
        Assert.Equal("failed (InvalidOperationException)",
            ServicoAtualizacao.TextoMotivo(ServicoAtualizacao.MotivoFalha.Outro, nameof(InvalidOperationException), En));
    }
}
