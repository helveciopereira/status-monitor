using VelocimetroInternet.Config;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Tests;

/// <summary>
/// v1.9.1 — memória: desenho pela CPU (padrão ligado) e reinício automático por teto. Cobre os padrões
/// e a guarda da configuração, a regra pura que PEDE o reinício (opção, tempo mínimo aberto, teto) e os
/// argumentos que a cópia nova recebe para esperar a antiga sair antes de disputar o mutex.
/// </summary>
public sealed class V191MemoriaTests
{
    private const long Mb = 1024L * 1024L;

    // ------------------------------------------------------------------ configuração

    [Fact]
    public void Padroes_DesenhoPelaCpuEReinicioLigados_Teto200()
    {
        var c = new Configuracoes();
        Assert.True(c.RenderizarPorSoftware);
        Assert.True(c.ReinicioAutomatico);
        Assert.Equal(200, c.ReinicioTetoMb);
    }

    [Theory]
    [InlineData(0, 200)]      // 0 reiniciaria sem parar → padrão
    [InlineData(-5, 200)]
    [InlineData(99, 200)]     // abaixo do mínimo
    [InlineData(100, 100)]    // limites inclusivos
    [InlineData(350, 350)]
    [InlineData(4000, 4000)]
    [InlineData(4001, 200)]   // acima do máximo
    public void Normalizar_TetoForaDaFaixaVoltaAoPadrao(int informado, int esperado)
    {
        var c = new Configuracoes { ReinicioTetoMb = informado };
        c.Normalizar();
        Assert.Equal(esperado, c.ReinicioTetoMb);
    }

    [Fact]
    public void Clonar_LevaOsCamposDeMemoria()
    {
        var c = new Configuracoes { RenderizarPorSoftware = false, ReinicioAutomatico = false, ReinicioTetoMb = 512 };
        Configuracoes copia = c.Clonar();
        Assert.False(copia.RenderizarPorSoftware);
        Assert.False(copia.ReinicioAutomatico);
        Assert.Equal(512, copia.ReinicioTetoMb);
    }

    // ------------------------------------------------------------------ regra do pedido

    [Theory]
    [InlineData(true, 200, 201, 2.0, true)]    // acima do teto, aberto há 2 h → pede
    [InlineData(true, 200, 200, 2.0, false)]   // igual ao teto não é "acima"
    [InlineData(true, 200, 199, 2.0, false)]
    [InlineData(true, 200, 300, 0.5, false)]   // aquecimento: menos de 1 h aberto nunca pede
    [InlineData(true, 200, 300, 1.0, true)]    // exatamente 1 h já vale
    [InlineData(false, 200, 900, 10.0, false)] // desligado no estúdio: nunca
    [InlineData(true, 4000, 3999, 10.0, false)]
    public void DevePedir_SoLigadoAposUmaHoraEAcimaDoTeto(bool ligado, int tetoMb, int privadaMb, double horas, bool esperado) =>
        Assert.Equal(esperado, ReinicioMemoria.DevePedir(ligado, tetoMb, privadaMb * Mb, TimeSpan.FromHours(horas)));

    [Fact]
    public void TempoMinimoAberto_EhUmaHora() => Assert.Equal(TimeSpan.FromHours(1), ReinicioMemoria.TempoMinimoAberto);

    // ------------------------------------------------------------------ argumentos da cópia nova

    [Fact]
    public void PidAguardar_LeOPidAposOArgumento()
    {
        Assert.Equal(1234, ReinicioMemoria.PidAguardar(["--aguardar-pid", "1234", "--reiniciado-memoria"]));
        Assert.Equal(77, ReinicioMemoria.PidAguardar(["--painel", "--AGUARDAR-PID", "77"])); // sem distinguir maiúsculas
    }

    // Os argumentos vêm numa string só (o C# não aceita array aninhado em InlineData) e são separados por espaço
    [Theory]
    [InlineData("")]
    [InlineData("--aguardar-pid")]          // sem o número
    [InlineData("--aguardar-pid abc")]      // ilegível
    [InlineData("--aguardar-pid 0")]        // PID 0 não é um processo do usuário
    [InlineData("1234 --aguardar-pid")]     // ordem errada
    public void PidAguardar_NullQuandoAusenteOuIlegivel(string linha) =>
        Assert.Null(ReinicioMemoria.PidAguardar(linha.Split(' ', StringSplitOptions.RemoveEmptyEntries)));

    [Fact]
    public void FoiReiniciado_SoComOArgumento()
    {
        Assert.True(ReinicioMemoria.FoiReiniciado(["--aguardar-pid", "1", "--reiniciado-memoria"]));
        Assert.False(ReinicioMemoria.FoiReiniciado(["--painel"]));
        Assert.False(ReinicioMemoria.FoiReiniciado([]));
    }

    [Fact]
    public void AguardarSaida_ProcessoInexistenteContaComoEncerrado()
    {
        // PID absurdo: GetProcessById lança ArgumentException → "já saiu" (true), e volta na hora
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(ReinicioMemoria.AguardarSaida(4_000_000, TimeSpan.FromSeconds(5)));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void AguardarSaida_ProcessoVivoDevolveFalseNoPrazo()
    {
        // O próprio processo de teste está vivo: o prazo curto estoura e a resposta é false (a nova cópia segue)
        Assert.False(ReinicioMemoria.AguardarSaida(Environment.ProcessId, TimeSpan.FromMilliseconds(200)));
    }
}
