using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Media;
using VelocimetroInternet.Apresentacao;

namespace VelocimetroInternet.Tests;

/// <summary>v0.9.0 — Onda 3: seletor visual de cor (conversão HSV e contrato do controle).</summary>
public sealed class Onda3SeletorCorTests
{
    [Theory]
    [InlineData(0xFF, 0x00, 0x00, 0, 1, 1)]       // vermelho puro
    [InlineData(0x00, 0xFF, 0x00, 120, 1, 1)]     // verde
    [InlineData(0x00, 0x00, 0xFF, 240, 1, 1)]     // azul
    [InlineData(0xFF, 0xFF, 0xFF, 0, 0, 1)]       // branco: sem saturação
    [InlineData(0x00, 0x00, 0x00, 0, 0, 0)]       // preto: sem brilho
    [InlineData(0x80, 0x80, 0x80, 0, 0, 0.502)]   // cinza médio
    public void CorHsv_De_CoresConhecidas(byte r, byte g, byte b, double h, double s, double v)
    {
        CorHsv hsv = CorHsv.De(Color.FromRgb(r, g, b));

        Assert.Equal(h, hsv.H, 1);
        Assert.Equal(s, hsv.S, 3);
        Assert.Equal(v, hsv.V, 3);
    }

    [Fact]
    public void CorHsv_IdaEVolta_PreservaTodasAsCoresDasPaletas()
    {
        // As cores que o usuário vê no estúdio não podem mudar 1 bit ao passar pelo seletor
        string[] hexes = ["#4CC2FF", "#FF6B6B", "#00F0FF", "#FF007F", "#E0E0E0", "#9E9E9E", "#FFFF00", "#10B981", "#F59E0B", "#000000", "#FFFFFF"];
        foreach (string hex in hexes)
        {
            Assert.True(Paletas.TentarHex(hex, out Color c));
            Assert.Equal(c, CorHsv.De(c).ParaCor());
        }
    }

    [Fact]
    public void CorHsv_IdaEVolta_VarreduraDoCuboRgb()
    {
        for (int r = 0; r < 256; r += 17)
        {
            for (int g = 0; g < 256; g += 17)
            {
                for (int b = 0; b < 256; b += 17)
                {
                    Color c = Color.FromRgb((byte)r, (byte)g, (byte)b);
                    Assert.Equal(c, CorHsv.De(c).ParaCor());
                }
            }
        }
    }

    [Fact]
    public void CorHsv_ParaCor_LimitaValoresForaDaFaixa()
    {
        Assert.Equal(Colors.Red, new CorHsv(360, 1, 1).ParaCor());   // 360° = 0°
        Assert.Equal(Colors.Blue, new CorHsv(-120, 1, 1).ParaCor()); // -120° = 240°
        Assert.Equal(Colors.White, new CorHsv(0, -1, 2).ParaCor());  // S < 0 → 0; V > 1 → 1
        Assert.Equal(Colors.Black, new CorHsv(double.NaN, double.NaN, double.NaN).ParaCor());
    }

    [Fact]
    public void SeletorCor_AtribuirCor_NaoDisparaEvento_ECinzaGuardaOMatiz() => EmSta(() =>
    {
        var seletor = new SeletorCor();
        int disparos = 0;
        seletor.CorAlterada += _ => disparos++;

        seletor.Cor = Color.FromRgb(0x4C, 0xC2, 0xFF);
        Assert.Equal(Color.FromRgb(0x4C, 0xC2, 0xFF), seletor.Cor);
        double matizAzul = seletor.Matiz;
        Assert.InRange(matizAzul, 195, 205);

        // Cinza: a cor muda, mas o matiz azul fica guardado — ao voltar a saturar, continua azul
        seletor.Cor = Color.FromRgb(0x80, 0x80, 0x80);
        Assert.Equal(Color.FromRgb(0x80, 0x80, 0x80), seletor.Cor);
        Assert.Equal(matizAzul, seletor.Matiz);

        Assert.Equal(0, disparos); // só mouse/teclado avisam (evita laço com o campo hex)
    });

    /// <summary>Controles WPF exigem thread STA; o xUnit roda em MTA.</summary>
    private static void EmSta(Action acao)
    {
        Exception? erro = null;
        var t = new Thread(() =>
        {
            try
            {
                acao();
            }
            catch (Exception ex)
            {
                erro = ex;
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (erro is not null)
        {
            ExceptionDispatchInfo.Capture(erro).Throw();
        }
    }
}
