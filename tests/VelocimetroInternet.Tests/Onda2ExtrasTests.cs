using System.IO; // projeto WPF não traz System.IO nos usings implícitos
using VelocimetroInternet.Apresentacao;
using VelocimetroInternet.Config;
using VelocimetroInternet.Medicao;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Tests;

/// <summary>
/// v0.8.0 — itens adiados da Onda 2: intervalo, fonte/tamanho, ordem das linhas,
/// posição escolhida por Ctrl+arrastar e o histórico com capacidade variável.
/// </summary>
public sealed class Onda2ExtrasTests
{
    private static NativeMethods.RECT R(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    // ------------------------------------------------------------------ configurações

    [Fact]
    public void Configuracoes_PadroesDaV080_MantemOComportamentoAntigo()
    {
        var c = new Configuracoes();

        // Quem atualiza da 0.7 não pode ver nada mudar sozinho
        Assert.Equal(1.0, c.IntervaloSegundos);
        Assert.Equal("normal", c.TamanhoTexto);
        Assert.Equal("segoe", c.Fonte);
        Assert.False(c.DownloadEmCima);
        Assert.False(c.MostrarEmTodasAsBarras);
        Assert.Empty(c.PosicoesManuais);
    }

    [Fact]
    public void Configuracoes_Normalizar_DescartaValoresDesconhecidosEPosicoesAbsurdas()
    {
        var c = new Configuracoes
        {
            IntervaloSegundos = 3, // não está na lista 0,5/1/2/5
            TamanhoTexto = "gigante",
            Fonte = "comic-sans",
            PosicoesManuais = new Dictionary<string, int> { ["principal"] = -5, ["monitor:3840,0"] = 900, ["x"] = 50_000 }
        };

        c.Normalizar();

        Assert.Equal(1.0, c.IntervaloSegundos);
        Assert.Equal("normal", c.TamanhoTexto);
        Assert.Equal("segoe", c.Fonte);
        Assert.Equal(new Dictionary<string, int> { ["monitor:3840,0"] = 900 }, c.PosicoesManuais);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(2.0)]
    [InlineData(5.0)]
    public void Configuracoes_Normalizar_AceitaOsIntervalosDoEstudio(double intervalo)
    {
        var c = new Configuracoes { IntervaloSegundos = intervalo };
        c.Normalizar();
        Assert.Equal(intervalo, c.IntervaloSegundos);
    }

    [Fact]
    public void Configuracoes_Clonar_CopiaAsPosicoesManuaisEmProfundidade()
    {
        var original = new Configuracoes { PosicoesManuais = new Dictionary<string, int> { ["principal"] = 400 } };

        Configuracoes copia = original.Clonar();
        copia.PosicoesManuais["principal"] = 10;

        // Mexer no rascunho do estúdio não pode alterar o que está valendo
        Assert.Equal(400, original.PosicoesManuais["principal"]);
    }

    // ------------------------------------------------------------------ larguras com fator de texto

    [Fact]
    public void Layout_FatorUm_ReproduzAsLargurasDaVersaoAnterior()
    {
        Assert.Equal(TaskbarLayout.LarguraLogicaNormalPx, TaskbarLayout.LarguraLogicaNormal(false, 1.0));
        Assert.Equal(TaskbarLayout.LarguraLogicaNormalPx + TaskbarLayout.LarguraLogicaSistemaNormalPx,
            TaskbarLayout.LarguraLogicaNormal(true, 1.0));
        Assert.Equal(TaskbarLayout.LarguraLogicaCompactaPx, TaskbarLayout.LarguraLogicaCompacta(false, 1.0));
        Assert.Equal(TaskbarLayout.LarguraLogicaNormal(true), TaskbarLayout.LarguraLogicaNormal(true, 1.0));
    }

    [Fact]
    public void Layout_FatorMaior_AlargaOWidgetEColideAntes()
    {
        Assert.True(TaskbarLayout.LarguraLogicaNormal(false, 1.2) > TaskbarLayout.LarguraLogicaNormal(false, 1.0));

        // Espaço que cabe exatamente o normal no fator 1 (86*1,5 = 129 + 2*12 de margem = 153)
        var barra = R(0, 1548, 2560, 1620);
        PosicaoWidgetBarra cabe = TaskbarLayout.CalcularVagaDoClima(barra, 1000, 1153, 1.5);
        PosicaoWidgetBarra grande = TaskbarLayout.CalcularVagaDoClima(barra, 1000, 1153, 1.5, fatorTexto: 1.2);

        Assert.Equal(EstadoColisaoBarra.NormalDuasLinhas, cabe.Estado);
        Assert.NotEqual(EstadoColisaoBarra.NormalDuasLinhas, grande.Estado);
    }

    // ------------------------------------------------------------------ posição manual (Ctrl + arrastar)

    [Fact]
    public void PosicaoManual_EspacoLivre_UsaOXSalvoEmPixelsLogicos()
    {
        var barra = R(0, 1548, 2560, 1620);
        var zonas = new List<(int, int)> { (0, 1217) }; // Iniciar + apps

        PosicaoWidgetBarra? p = TaskbarLayout.CalcularPosicaoManual(barra, 1000, zonas, 1941, 1.5);

        Assert.NotNull(p);
        Assert.Equal(1500, p.Value.XTelaPx); // 1000 lógicos × 1,5
        Assert.Equal(EstadoColisaoBarra.NormalDuasLinhas, p.Value.Estado);
        Assert.Equal(129, p.Value.LarguraFisicaPx);
    }

    [Fact]
    public void PosicaoManual_SobreOsBotoes_DevolveNullParaVoltarAoAutomatico()
    {
        var barra = R(0, 1548, 2560, 1620);
        var zonas = new List<(int, int)> { (0, 1217) };

        // x = 700 lógicos = 1050 físicos: cai em cima dos apps
        Assert.Null(TaskbarLayout.CalcularPosicaoManual(barra, 700, zonas, 1941, 1.5));
        // Encostado demais: a margem de 12 px também conta (1217 + 12 > 1225)
        Assert.Null(TaskbarLayout.CalcularPosicaoManual(barra, 817, zonas, 1941, 1.5));
    }

    [Fact]
    public void PosicaoManual_AlemDaBandeja_EhLimitadaAntesDoChevron()
    {
        var barra = R(0, 1548, 2560, 1620);

        PosicaoWidgetBarra? p = TaskbarLayout.CalcularPosicaoManual(barra, 5000, [], 1941, 1.5);

        Assert.NotNull(p);
        Assert.Equal(1941 - 129 - 12, p.Value.XTelaPx);
    }

    [Fact]
    public void PosicaoManual_BarraSecundaria_ContaAPartirDaBordaDela()
    {
        // Monitor à direita: a barra começa em X = 3840
        var barra = R(3840, 1548, 6720, 1620);

        PosicaoWidgetBarra? p = TaskbarLayout.CalcularPosicaoManual(barra, 400, [], 6603, 1.5);

        Assert.NotNull(p);
        Assert.Equal(3840 + 600, p.Value.XTelaPx);
        Assert.Equal(600, p.Value.XRelativoBarraPx);
    }

    [Fact]
    public void Arrasto_NaoSaiDaBarraNemInvadeABandeja()
    {
        var barra = R(0, 1548, 2560, 1620);

        Assert.Equal(12, TaskbarLayout.LimitarXDuranteArrasto(barra, -300, 129, 1941, 1.5));
        Assert.Equal(1800, TaskbarLayout.LimitarXDuranteArrasto(barra, 5000, 129, 1941, 1.5));
        // No meio pode passar por cima dos botões (só ao soltar é que se julga)
        Assert.Equal(500, TaskbarLayout.LimitarXDuranteArrasto(barra, 500, 129, 1941, 1.5));
    }

    [Fact]
    public void SobrepoeAlgumaZona_IgnoraZonaVaziaEDetectaEncosto()
    {
        Assert.False(TaskbarLayout.SobrepoeAlgumaZona(100, 50, 0, [(200, 200)])); // zona vazia
        Assert.True(TaskbarLayout.SobrepoeAlgumaZona(100, 50, 0, [(140, 300)]));
        Assert.False(TaskbarLayout.SobrepoeAlgumaZona(100, 50, 0, [(150, 300)])); // [100,150) só encosta
        Assert.True(TaskbarLayout.SobrepoeAlgumaZona(100, 50, 1, [(150, 300)]));  // a margem faz tocar
    }

    // ------------------------------------------------------------------ histórico com intervalo variável

    [Theory]
    [InlineData(0.5, 120)]
    [InlineData(1.0, 60)]
    [InlineData(2.0, 30)]
    [InlineData(5.0, 12)]
    [InlineData(0.0, 60)]   // inválido → padrão
    [InlineData(100.0, 2)]  // mínimo para haver linha
    public void Historico_CapacidadePara_CobreSempreSessentaSegundos(double intervalo, int esperado) =>
        Assert.Equal(esperado, HistoricoRede.CapacidadePara(intervalo));

    [Fact]
    public void Historico_DefinirIntervalo_RecomecaOGraficoMasNaoOConsumo()
    {
        string caminho = Path.Combine(Path.GetTempPath(), $"consumo-teste-{Guid.NewGuid():N}.json");
        try
        {
            var h = new HistoricoRede(caminho);
            for (int i = 0; i < 70; i++)
            {
                h.Registrar(100, 200, 10, 20, "Wi-Fi", 0);
            }

            Assert.Equal(60, h.Capturar().Pontos.Count);

            h.DefinirIntervalo(5);
            InstantaneoHistorico depois = h.Capturar();
            Assert.Empty(depois.Pontos);
            Assert.Equal(12, depois.Capacidade);
            Assert.Equal(700ul, depois.BytesEnviadosHoje);
            Assert.Equal(1400ul, depois.BytesRecebidosHoje);

            for (int i = 0; i < 20; i++)
            {
                h.Registrar(1, 1, 0, 0, "Wi-Fi", 0);
            }

            Assert.Equal(12, h.Capturar().Pontos.Count); // anel girou na capacidade nova

            // Mesmo intervalo de novo: não apaga nada
            h.DefinirIntervalo(5);
            Assert.Equal(12, h.Capturar().Pontos.Count);
        }
        finally
        {
            File.Delete(caminho);
        }
    }

    // ------------------------------------------------------------------ aparência

    [Fact]
    public void Aparencia_Padrao_TemFatorExatamenteUm()
    {
        Assert.Equal(1.0, AparenciaWidget.Padrao.FatorLargura());
        Assert.Equal(10.5, AparenciaWidget.Padrao.TamanhoValor);
    }

    [Fact]
    public void Aparencia_TamanhoMudaOFatorNoSentidoCerto()
    {
        double grande = new AparenciaWidget("segoe", "grande", false).FatorLargura();
        double pequeno = new AparenciaWidget("segoe", "pequeno", false).FatorLargura();

        Assert.True(grande > 1.0, $"grande = {grande}");
        Assert.True(pequeno < 1.0, $"pequeno = {pequeno}");
        Assert.InRange(grande, 0.8, 1.6);
        Assert.InRange(pequeno, 0.8, 1.6);
    }

    [Fact]
    public void Aparencia_OrdemNaoMudaLargura()
    {
        Assert.Equal(1.0, new AparenciaWidget("segoe", "normal", DownloadEmCima: true).FatorLargura());
    }
}
