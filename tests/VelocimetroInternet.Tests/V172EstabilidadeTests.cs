using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Tests;

/// <summary>
/// v1.7.2 — tratador global de exceções: a <see cref="PoliticaFalhas"/> decide entre "o app segue"
/// (erro isolado) e "encerra" (tempestade de erros). Regra pura com o instante passado de fora.
/// </summary>
public sealed class V172EstabilidadeTests
{
    private static readonly DateTime T0 = new(2026, 11, 3, 15, 0, 0);

    [Fact]
    public void ErroIsolado_AppSegue()
    {
        var politica = new PoliticaFalhas();
        Assert.True(politica.RegistrarEContinuar(T0));
        Assert.Equal(1, politica.ErrosNaJanela);
    }

    [Fact]
    public void AteOLimiteNaJanela_Segue_AcimaEncerra()
    {
        var politica = new PoliticaFalhas();
        // Os 5 primeiros em 10 s: continua
        for (int i = 0; i < PoliticaFalhas.MaximoPorJanela; i++)
        {
            Assert.True(politica.RegistrarEContinuar(T0.AddSeconds(i * 2)));
        }

        // O 6º dentro do mesmo minuto: tempestade → encerra
        Assert.False(politica.RegistrarEContinuar(T0.AddSeconds(20)));
        Assert.Equal(PoliticaFalhas.MaximoPorJanela + 1, politica.ErrosNaJanela);
    }

    [Fact]
    public void ErrosEspacados_NuncaEncerram()
    {
        // Um erro a cada 30 s (ex.: o usuário clicando de novo num botão quebrado): no máximo 3 por
        // minuto deslizante → o app nunca é derrubado por isso.
        var politica = new PoliticaFalhas();
        for (int i = 0; i < 50; i++)
        {
            Assert.True(politica.RegistrarEContinuar(T0.AddSeconds(i * 30)));
        }

        Assert.True(politica.ErrosNaJanela <= 3);
    }

    [Fact]
    public void JanelaDesliza_ErrosAntigosSaoEsquecidos()
    {
        var politica = new PoliticaFalhas();
        for (int i = 0; i < PoliticaFalhas.MaximoPorJanela; i++)
        {
            politica.RegistrarEContinuar(T0.AddSeconds(i));
        }

        // Passado mais de 1 min desde todos os anteriores, o próximo é "o primeiro" de novo
        Assert.True(politica.RegistrarEContinuar(T0.AddSeconds(61 + PoliticaFalhas.MaximoPorJanela)));
        Assert.Equal(1, politica.ErrosNaJanela);
    }

    [Fact]
    public void LacoDeErro_ParaEmPoucosSegundos()
    {
        // Simula uma falha a cada 100 ms (binding quebrado redisparando): o encerramento tem de vir
        // antes de 1 s, não depois de encher o log por um minuto.
        var politica = new PoliticaFalhas();
        int n = 0;
        while (politica.RegistrarEContinuar(T0.AddMilliseconds(n * 100)))
        {
            n++;
            Assert.True(n < 100, "a política não encerrou o laço de erro");
        }

        Assert.Equal(PoliticaFalhas.MaximoPorJanela, n);
    }
}
