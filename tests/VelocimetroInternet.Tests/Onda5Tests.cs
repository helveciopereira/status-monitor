using VelocimetroInternet.Apresentacao;
using VelocimetroInternet.Config;
using VelocimetroInternet.Medicao;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Tests;

/// <summary>
/// v0.5.0: CPU e RAM em % à esquerda do upload/download — contas puras, leitura real desta
/// máquina, largura na barra e compatibilidade do settings.json antigo.
/// </summary>
public sealed class Onda5Tests
{
    // ---------------------------------------------------------------- CPU

    [Fact]
    public void Cpu_KernelIncluiOcioso_CalculaFracaoOcupada()
    {
        // Δkernel 600 (dos quais 300 ociosos) + Δuser 400 → total 1000, ocupado 700 → 70%
        var antes = new TemposCpu(Ocioso: 1_000, Kernel: 5_000, Usuario: 2_000);
        var depois = new TemposCpu(Ocioso: 1_300, Kernel: 5_600, Usuario: 2_400);
        Assert.Equal(70.0, MedidorSistema.CalcularPercentualCpu(antes, depois)!.Value, 6);
    }

    [Fact]
    public void Cpu_TotalmenteOciosa_DaZero()
    {
        var antes = new TemposCpu(0, 0, 0);
        var depois = new TemposCpu(Ocioso: 500, Kernel: 500, Usuario: 0);
        Assert.Equal(0.0, MedidorSistema.CalcularPercentualCpu(antes, depois)!.Value, 6);
    }

    [Fact]
    public void Cpu_IntervaloVazioOuContadorVoltando_NaoInventaNumero()
    {
        var t = new TemposCpu(10, 20, 30);
        Assert.Null(MedidorSistema.CalcularPercentualCpu(t, t));                          // Δtotal = 0
        Assert.Null(MedidorSistema.CalcularPercentualCpu(t, new TemposCpu(5, 40, 40)));   // ocioso voltou
    }

    // ---------------------------------------------------------------- RAM

    [Fact]
    public void Ram_UsoEhTotalMenosDisponivel()
    {
        Assert.Equal(25.0, MedidorSistema.CalcularPercentualRam(16, 12)!.Value, 6);
        Assert.Null(MedidorSistema.CalcularPercentualRam(0, 0));
        Assert.Null(MedidorSistema.CalcularPercentualRam(8, 9));
    }

    // ---------------------------------------------------------------- texto

    [Theory]
    [InlineData(0.0, "0%")]
    [InlineData(7.4, "7%")]
    [InlineData(7.5, "8%")]
    [InlineData(99.6, "100%")]
    [InlineData(100.0, "100%")]
    public void Formatar_PorcentagemInteira(double valor, string esperado)
    {
        Assert.Equal(esperado, MedidorSistema.Formatar(valor));
    }

    [Fact]
    public void Formatar_SemLeitura_MostraTraco()
    {
        Assert.Equal("—", MedidorSistema.Formatar(null));
    }

    // ---------------------------------------------------------------- leitura real

    [Fact]
    public void LeituraReal_DestaMaquina_FicaEntre0e100()
    {
        var medidor = new MedidorSistema();
        UsoSistema primeira = medidor.Ler();
        Assert.Null(primeira.CpuPercentual);                     // 1ª leitura: ainda sem base
        Assert.InRange(primeira.RamPercentual!.Value, 1, 100);  // RAM sai já na 1ª

        Thread.Sleep(250);
        UsoSistema segunda = medidor.Ler();
        Assert.InRange(segunda.CpuPercentual!.Value, 0, 100);
    }

    // ---------------------------------------------------------------- barra

    [Fact]
    public void Layout_ComCpuRam_FicaMaisLargoEContinuaNaVagaDoClima()
    {
        // Mesma geometria real do teste da Onda 0 (2560x1600 a 150%)
        var barra = new NativeMethods.RECT { Left = 0, Top = 1528, Right = 2560, Bottom = 1600 };
        PosicaoWidgetBarra sem = TaskbarLayout.CalcularVagaDoClima(barra, 1169, 1797, 1.5);
        PosicaoWidgetBarra com = TaskbarLayout.CalcularVagaDoClima(barra, 1169, 1797, 1.5, mostrarSistema: true);

        Assert.Equal(EstadoColisaoBarra.NormalDuasLinhas, com.Estado);
        Assert.Equal((int)Math.Round(TaskbarLayout.LarguraLogicaNormal(true) * 1.5), com.LarguraFisicaPx);
        Assert.True(com.LarguraFisicaPx > sem.LarguraFisicaPx);
        // Cresce para a ESQUERDA: a borda direita (encostada no '^' da bandeja) não se move
        Assert.Equal(sem.XTelaPx + sem.LarguraFisicaPx, com.XTelaPx + com.LarguraFisicaPx);
    }

    [Fact]
    public void Layout_ComCpuRam_CompactaAntesQueSem()
    {
        // 260 px físicos livres: cabe o normal SEM CPU/RAM (156+24), mas não o normal COM (252+24)
        var barra = new NativeMethods.RECT { Left = 0, Top = 1528, Right = 2560, Bottom = 1600 };
        Assert.Equal(EstadoColisaoBarra.NormalDuasLinhas,
            TaskbarLayout.CalcularVagaDoClima(barra, 1537, 1797, 1.5).Estado);
        Assert.Equal(EstadoColisaoBarra.CompactoUmaLinha,
            TaskbarLayout.CalcularVagaDoClima(barra, 1537, 1797, 1.5, mostrarSistema: true).Estado);
    }

    // ---------------------------------------------------------------- configuração

    [Fact]
    public void Config_PadraoLigado_ESobreviveAoClone()
    {
        var c = new Configuracoes();
        Assert.True(c.MostrarCpuRam);
        c.MostrarCpuRam = false;
        Assert.False(c.Clonar().MostrarCpuRam);
    }
}
