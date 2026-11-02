using System.Windows.Media;
using VelocimetroInternet.Apresentacao;
using VelocimetroInternet.Medicao;
using Xunit.Abstractions;

namespace VelocimetroInternet.Tests;

/// <summary>
/// v1.7.1 — pedido do dono: "quando a CPU bater 80% os números e a porcentagem fiquem vermelhos,
/// assim como a RAM". Cobre o limite (pelo valor mostrado na tela) e a cor: vermelho de verdade e
/// legível (contraste mínimo da paleta) nos fundos reais, nas 5 paletas e nos dois temas da barra.
/// </summary>
public sealed class V171AlertaSistemaTests(ITestOutputHelper saida)
{
    [Theory]
    [InlineData(null, false)]       // sem leitura: nada de alerta
    [InlineData(0.0, false)]
    [InlineData(79.4, false)]       // mostrado como "79%"
    [InlineData(79.5, true)]        // mostrado como "80%": vermelho junto com o número
    [InlineData(80.0, true)]
    [InlineData(100.0, true)]
    public void EmAlerta_APartirDe80PeloValorMostrado(double? percentual, bool esperado)
    {
        Assert.Equal(esperado, MedidorSistema.EmAlerta(percentual));
        // Coerência com o texto: alerta ⇔ número exibido ≥ 80
        if (percentual is { } p)
        {
            int mostrado = int.Parse(MedidorSistema.Formatar(p).TrimEnd('%'));
            Assert.Equal(mostrado >= MedidorSistema.LimiteAlertaPercentual, MedidorSistema.EmAlerta(p));
        }
    }

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
    public void Alerta_EVermelhoELegivelNosFundosReais(string id, bool barraClara)
    {
        PaletaCores paleta = Paletas.Predefinidas.Single(p => p.Id == id);
        CoresResolvidas c = Paletas.Resolver(paleta, barraClara);
        Color barra = barraClara ? Paletas.BarraClara : Paletas.BarraEscura;

        // Fundos montados aqui de forma independente: pílula de CPU/RAM (véu neutro 0x1A, o mesmo
        // de TaskbarWidget.AplicarCores) e o fundo do modo uma linha
        Color neutro = barraClara ? Colors.Black : Colors.White;
        Color pilula = Paletas.Misturar(barra, neutro, 0x1A);
        Color compacto = Paletas.Misturar(barra, c.FundoCompacto, c.FundoCompacto.A);

        double rPilula = Paletas.RazaoContraste(c.Alerta, pilula);
        double rCompacto = Paletas.RazaoContraste(c.Alerta, compacto);
        saida.WriteLine($"{id} {(barraClara ? "clara" : "escura")}: {Paletas.ParaHex(c.Alerta)} " +
                        $"pílula {rPilula:0.00}, uma linha {rCompacto:0.00}");

        Assert.Equal(0xFF, c.Alerta.A); // opaco, como o texto normal
        Assert.True(rPilula >= paleta.ContrasteMinimo, $"pílula {rPilula:0.00}");
        Assert.True(rCompacto >= paleta.ContrasteMinimo, $"uma linha {rCompacto:0.00}");

        // Continua VERMELHO depois da calibração: o canal R domina com folga
        Assert.True(c.Alerta.R > c.Alerta.G + 60 && c.Alerta.R > c.Alerta.B + 60,
            $"não parece vermelho: {Paletas.ParaHex(c.Alerta)}");
    }
}
