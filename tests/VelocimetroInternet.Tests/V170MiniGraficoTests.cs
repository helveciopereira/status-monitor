using System.Windows.Media;
using VelocimetroInternet.Apresentacao;
using VelocimetroInternet.Config;
using Xunit.Abstractions;

namespace VelocimetroInternet.Tests;

/// <summary>
/// v1.7.0 — Onda 13: mini gráfico dos últimos 60 s atrás dos números da barra. Cobre a série
/// curta (janela por TEMPO, escala, piso, buracos) e a regra de cor: o gráfico nunca pode deixar o
/// número abaixo do contraste mínimo da paleta, em nenhuma paleta nem tema da barra.
/// </summary>
public sealed class V170MiniGraficoTests(ITestOutputHelper saida)
{
    // ---------------------------------------------------------------- série curta

    [Fact]
    public void Serie_MenosDeDoisPontos_NadaADesenhar()
    {
        var s = new SerieCurta();
        Assert.Empty(s.Pontos(0));
        s.Adicionar(1_000, 500_000);
        Assert.Empty(s.Pontos(1_000));
    }

    [Fact]
    public void Serie_PosicionaPeloTempo_AgoraNaDireita()
    {
        var s = new SerieCurta();
        s.Adicionar(0, 0);
        s.Adicionar(30_000, 1_000_000);
        s.Adicionar(60_000, 1_000_000);

        IReadOnlyList<(double X, double Y)> p = s.Pontos(60_000);
        Assert.Equal(3, p.Count);
        Assert.Equal(0.0, p[0].X, 6);   // 60 s atrás = borda esquerda
        Assert.Equal(0.5, p[1].X, 6);   // 30 s atrás = meio
        Assert.Equal(1.0, p[2].X, 6);   // agora = borda direita
        // Escala = máximo × 1,1: o pico fica em ~91 % da altura, sem encostar no topo
        Assert.Equal(1 / 1.1, p[2].Y, 6);
        Assert.Equal(0.0, p[0].Y, 6);
    }

    [Fact]
    public void Serie_DescartaVelhas_MasGuardaUmaAncoraNaBordaEsquerda()
    {
        var s = new SerieCurta();
        for (long ms = 0; ms <= 120_000; ms += 1_000)
        {
            s.Adicionar(ms, 100_000);
        }

        // 60 s de janela a 1 amostra/s = 61 pontos (de 60.000 a 120.000 ms, inclusive)
        Assert.Equal(61, s.Quantidade);
        IReadOnlyList<(double X, double Y)> p = s.Pontos(120_000);
        Assert.Equal(0.0, p[0].X, 6);

        // Buraco de 5 min (PC suspenso): sobra a âncora velha + a nova; a velha fica presa no X = 0
        s.Adicionar(420_000, 100_000);
        p = s.Pontos(420_000);
        Assert.Equal(2, p.Count);
        Assert.Equal(0.0, p[0].X, 6);
        Assert.Equal(1.0, p[1].X, 6);
    }

    [Fact]
    public void Serie_PisoDaEscala_TrafegoDeFundoFicaBaixo()
    {
        var s = new SerieCurta();
        s.Adicionar(0, 2_000);       // 2 KB/s: o "parado" de um PC comum
        s.Adicionar(1_000, 3_000);
        IReadOnlyList<(double X, double Y)> p = s.Pontos(1_000);
        // Sem o piso, 3 KB/s seria o máximo e encheria a pílula; com ele, fica abaixo de 5 %
        Assert.True(p.Max(x => x.Y) < 0.05, $"Y = {p.Max(x => x.Y)}");
    }

    [Fact]
    public void Serie_ValoresInvalidosViramZero_ERelogioQueVoltaZera()
    {
        var s = new SerieCurta();
        s.Adicionar(0, double.NaN);
        s.Adicionar(1_000, -50);
        Assert.All(s.Pontos(1_000), p => Assert.Equal(0.0, p.Y));

        s.Adicionar(500, 1_000); // instante anterior ao último: recomeça
        Assert.Equal(1, s.Quantidade);
    }

    [Fact]
    public void Serie_Limpar_EsqueceTudo()
    {
        var s = new SerieCurta();
        s.Adicionar(0, 1);
        s.Adicionar(1_000, 2);
        s.Limpar();
        Assert.Equal(0, s.Quantidade);
        Assert.Empty(s.Pontos(2_000));
    }

    // ---------------------------------------------------------------- cor e contraste

    public static TheoryData<string, bool> PaletasETemas()
    {
        var dados = new TheoryData<string, bool>();
        foreach (PaletaCores p in Paletas.Predefinidas)
        {
            dados.Add(p.Id, false);
            dados.Add(p.Id, true);
        }

        return dados;
    }

    [Theory]
    [MemberData(nameof(PaletasETemas))]
    public void Grafico_NuncaDerrubaOContrasteDoNumero(string id, bool barraClara)
    {
        PaletaCores paleta = Paletas.Predefinidas.Single(p => p.Id == id);
        CoresResolvidas c = Paletas.Resolver(paleta, barraClara);
        Color barra = barraClara ? Paletas.BarraClara : Paletas.BarraEscura;

        // A borracha é a cor da barra, translúcida
        Assert.Equal((barra.R, barra.G, barra.B), (c.MascaraGrafico.R, c.MascaraGrafico.G, c.MascaraGrafico.B));
        Assert.Equal(Paletas.AlfaMascaraGrafico, c.MascaraGrafico.A);

        foreach ((string nome, Color texto, Color fundoPilula) in new[]
                 {
                     ("↑", c.Upload, c.FundoPilulaUpload),
                     ("↓", c.Download, c.FundoPilulaDownload)
                 })
        {
            // Abaixo da curva: o fundo de sempre (barra + véu). Acima: + a borracha.
            Color abaixo = Paletas.Misturar(barra, fundoPilula, fundoPilula.A);
            Color acima = Paletas.Misturar(abaixo, c.MascaraGrafico, c.MascaraGrafico.A);
            double rAbaixo = Paletas.RazaoContraste(texto, abaixo);
            double rAcima = Paletas.RazaoContraste(texto, acima);
            saida.WriteLine($"{id} {(barraClara ? "clara" : "escura")} {nome}: abaixo {rAbaixo:0.00}, acima {rAcima:0.00}, " +
                            $"diferença de fundo {Paletas.RazaoContraste(abaixo, acima):0.000}");

            Assert.True(rAbaixo >= paleta.ContrasteMinimo, $"{nome} abaixo: {rAbaixo:0.00}");
            Assert.True(rAcima >= rAbaixo, $"{nome}: a borracha baixou o contraste ({rAcima:0.00} < {rAbaixo:0.00})");
        }
    }

    [Fact]
    public void Config_MiniGraficoDesligadoPorPadrao_EArquivoAntigoLido()
    {
        Assert.False(new Configuracoes().MiniGrafico);
        Configuracoes antigo = System.Text.Json.JsonSerializer.Deserialize<Configuracoes>("""{"Unidade":"bits"}""")!;
        Assert.False(antigo.MiniGrafico);
        Assert.True(new Configuracoes { MiniGrafico = true }.Clonar().MiniGrafico);
    }
}
