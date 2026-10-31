using System.IO;
using System.Windows.Media;
using Microsoft.Win32;
using VelocimetroInternet.Apresentacao;
using VelocimetroInternet.Config;
using VelocimetroInternet.Medicao;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Tests;

/// <summary>
/// Testes das peças das Ondas 1–3 (v0.2.0): motor de paletas/WCAG, detector de tela cheia,
/// configurações, início com o Windows (em subchave de TESTE), histórico/consumo e formatações
/// do painel rápido. Nenhum teste toca a configuração real do usuário.
/// </summary>
public sealed class Onda1a3Tests : IDisposable
{
    private readonly string _pastaTemp = Path.Combine(Path.GetTempPath(), "VelocimetroTestes_" + Guid.NewGuid().ToString("N"));

    public Onda1a3Tests() => Directory.CreateDirectory(_pastaTemp);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_pastaTemp, recursive: true);
        }
        catch
        {
            // Pasta temporária: sobra não atrapalha
        }
    }

    // ---------------------------------------------------------------- paletas / WCAG

    [Fact]
    public void Wcag_ContrastePretoNoBrancoE21()
    {
        Assert.Equal(21.0, Paletas.RazaoContraste(Colors.Black, Colors.White), 2);
        Assert.Equal(1.0, Paletas.RazaoContraste(Colors.White, Colors.White), 2);
    }

    [Theory]
    [InlineData(7.0, "AAA")]
    [InlineData(4.5, "AA")]
    [InlineData(3.2, "AA texto grande")]
    [InlineData(2.0, "Insuficiente")]
    public void Wcag_NivelPorRazao(double razao, string esperado) => Assert.Equal(esperado, Paletas.NivelWcag(razao));

    [Fact]
    public void Paletas_TodasPassamNoMinimoNasDuasBarras()
    {
        // Promessa do design: toda paleta, resolvida para a barra escura OU clara, fica legível
        foreach (PaletaCores p in Paletas.Predefinidas)
        {
            foreach (bool clara in new[] { false, true })
            {
                CoresResolvidas c = Paletas.Resolver(p, clara);
                Assert.True(c.ContrasteUpload >= p.ContrasteMinimo, $"{p.Id} upload barra {(clara ? "clara" : "escura")}: {c.ContrasteUpload:F2}");
                Assert.True(c.ContrasteDownload >= p.ContrasteMinimo, $"{p.Id} download barra {(clara ? "clara" : "escura")}: {c.ContrasteDownload:F2}");
            }
        }
    }

    [Fact]
    public void Paletas_PersonalizadaIlegivelEhCorrigidaNaBarraClara()
    {
        // Amarelo puro numa barra clara é ilegível (~1,07:1) → o motor escurece até passar
        PaletaCores p = Paletas.Obter(Paletas.IdPersonalizada, "#FFFF00", "#FFFFFF");
        CoresResolvidas c = Paletas.Resolver(p, barraClara: true);
        Assert.True(c.ContrasteUpload >= 4.5);
        Assert.True(c.ContrasteDownload >= 4.5);
        Assert.NotEqual(Paletas.Hex("#FFFF00"), c.Upload);
    }

    [Fact]
    public void Paletas_ClassicoEscuroMantemCoresDoDesign()
    {
        CoresResolvidas c = Paletas.Resolver(Paletas.Obter("classico", "", ""), barraClara: false);
        Assert.Equal("#4CC2FF", Paletas.ParaHex(c.Upload));
        Assert.Equal("#FF6B6B", Paletas.ParaHex(c.Download));
    }

    [Theory]
    [InlineData("#4CC2FF", true)]
    [InlineData("4cc2ff", true)]
    [InlineData("#4CC2F", false)]
    [InlineData("#GGGGGG", false)]
    [InlineData("", false)]
    public void Paletas_TentarHex(string texto, bool valido) => Assert.Equal(valido, Paletas.TentarHex(texto, out _));

    [Fact]
    public void Paletas_IdDesconhecidoOuPersonalizadaInvalidaCaiNoClassico()
    {
        Assert.Equal("classico", Paletas.Obter("nao-existe", "", "").Id);
        Assert.Equal("classico", Paletas.Obter(Paletas.IdPersonalizada, "lixo", "#FF0000").Id);
    }

    // ---------------------------------------------------------------- tela cheia

    private static NativeMethods.RECT R(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    [Fact]
    public void TelaCheia_JanelaQueCobreOMonitorConta()
    {
        Assert.True(DetectorTelaCheia.CobreMonitorInteiro(R(0, 0, 2560, 1440), R(0, 0, 2560, 1440), "Chrome_WidgetWin_1"));
        // F11 de navegador às vezes passa 1 px da borda
        Assert.True(DetectorTelaCheia.CobreMonitorInteiro(R(-1, -1, 2561, 1441), R(0, 0, 2560, 1440), "Chrome_WidgetWin_1"));
    }

    [Fact]
    public void TelaCheia_JanelaMaximizadaComBarraNaoConta()
    {
        // Maximizada normal deixa a barra de fora (área de trabalho, não o monitor)
        Assert.False(DetectorTelaCheia.CobreMonitorInteiro(R(0, 0, 2560, 1392), R(0, 0, 2560, 1440), "Chrome_WidgetWin_1"));
    }

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    public void TelaCheia_DesktopEShellNaoContam(string classe) =>
        Assert.False(DetectorTelaCheia.CobreMonitorInteiro(R(0, 0, 2560, 1440), R(0, 0, 2560, 1440), classe));

    // ---------------------------------------------------------------- configurações

    [Fact]
    public void Config_AusenteCriaPadroesEGravaAtomico()
    {
        string caminho = Path.Combine(_pastaTemp, "settings.json");
        var s = new ServicoConfiguracoes(caminho);
        Assert.NotNull(s.Carregar());
        Assert.True(File.Exists(caminho));
        Assert.Equal("bytes", s.Atual.Unidade);
        Assert.True(s.Atual.FixarNaBarra);

        Configuracoes? recebida = null;
        s.Alteradas += c => recebida = c;
        s.Alterar(c => c.Unidade = "bits");
        Assert.Equal("bits", recebida?.Unidade);

        var outra = new ServicoConfiguracoes(caminho);
        Assert.Null(outra.Carregar());
        Assert.Equal("bits", outra.Atual.Unidade);
        Assert.False(File.Exists(caminho + ".tmp"));
    }

    [Fact]
    public void Config_CorrompidaViraBakEPadroes()
    {
        string caminho = Path.Combine(_pastaTemp, "settings.json");
        File.WriteAllText(caminho, "{ isto não é json");
        var s = new ServicoConfiguracoes(caminho);
        string? aviso = s.Carregar();
        Assert.Contains("inválido", aviso);
        Assert.True(File.Exists(caminho + ".bak"));
        Assert.Equal("classico", s.Atual.Paleta);
    }

    [Fact]
    public void Config_NormalizaValoresForaDaFaixa()
    {
        var c = new Configuracoes { Unidade = "xyz", Suavizacao = 5, VelocidadeContratadaMbps = -3, Paleta = "  NEON " };
        c.Normalizar();
        Assert.Equal("bytes", c.Unidade);
        Assert.Equal(0.9, c.Suavizacao);
        Assert.Equal(0, c.VelocidadeContratadaMbps);
        Assert.Equal("neon", c.Paleta);
    }

    [Fact]
    public void Config_ClonarEhProfundo()
    {
        var c = new Configuracoes { InterfacesExcluir = ["Ethernet 2"] };
        Configuracoes copia = c.Clonar();
        copia.InterfacesExcluir.Add("Wi-Fi 3");
        Assert.Single(c.InterfacesExcluir);
    }

    // ---------------------------------------------------------------- início com o Windows

    [Fact]
    public void InicioComWindows_LigaDesligaERespeitaStartupApproved()
    {
        string raiz = @"Software\VelocimetroInternetTestes_" + Guid.NewGuid().ToString("N");
        var inicio = new InicioComWindows(raiz + @"\Run", raiz + @"\Aprovado");
        try
        {
            Assert.False(inicio.EstaLigado());
            inicio.Definir(true, @"C:\Apps\Velocimetro.exe");
            Assert.True(inicio.EstaLigado());
            Assert.False(inicio.ApontaParaOutroCaminho(@"C:\Apps\Velocimetro.exe"));
            Assert.True(inicio.ApontaParaOutroCaminho(@"D:\Outro\Velocimetro.exe"));

            // Usuário desligou no Gerenciador de Tarefas (1º byte ímpar)
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(raiz + @"\Aprovado"))
            {
                k.SetValue(InicioComWindows.NomeValor, new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
            }

            Assert.False(inicio.EstaLigado());

            inicio.Definir(false, @"C:\Apps\Velocimetro.exe");
            Assert.False(inicio.EstaLigado());
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(raiz, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void InicioComWindows_ComandoTemAspasEMarcador() =>
        Assert.Equal("\"C:\\A B\\v.exe\" --autostart", InicioComWindows.MontarComando(@"C:\A B\v.exe"));

    // ---------------------------------------------------------------- histórico / consumo

    [Fact]
    public void Historico_GuardaSo60SegundosECalculaPicos()
    {
        var h = new HistoricoRede(Path.Combine(_pastaTemp, "consumo.json"));
        for (int i = 1; i <= 75; i++)
        {
            h.Registrar(i, i * 10, 1, 2, "Wi-Fi", 300_000_000);
        }

        InstantaneoHistorico s = h.Capturar();
        Assert.Equal(60, s.Pontos.Count);
        Assert.Equal(16, s.Pontos[0].UploadBps);   // os 15 primeiros saíram da janela
        Assert.Equal(75, s.UploadAtual);
        Assert.Equal(750, s.PicoDownload);
        Assert.Equal(75UL, s.BytesEnviadosHoje);
        Assert.Equal(150UL, s.BytesRecebidosHoje);
        Assert.Equal("Wi-Fi", s.NomeInterface);
    }

    [Fact]
    public void Historico_ConsumoSobreviveAoReinicioNoMesmoDiaEZeraNoDiaSeguinte()
    {
        string caminho = Path.Combine(_pastaTemp, "consumo.json");
        var dia = new DateOnly(2026, 10, 31);
        var h = new HistoricoRede(caminho, () => dia);
        h.Registrar(0, 0, 1000, 5000, "Ethernet", 0);
        h.SalvarConsumo();

        var reaberto = new HistoricoRede(caminho, () => dia);
        Assert.Equal(5000UL, reaberto.Capturar().BytesRecebidosHoje);

        var amanha = new HistoricoRede(caminho, () => dia.AddDays(1));
        Assert.Equal(0UL, amanha.Capturar().BytesRecebidosHoje);

        // Virada do dia com o app aberto
        DateOnly relogio = dia;
        var aberto = new HistoricoRede(caminho, () => relogio);
        relogio = dia.AddDays(1);
        aberto.Registrar(0, 0, 7, 7, "Ethernet", 0);
        Assert.Equal(7UL, aberto.Capturar().BytesRecebidosHoje);
    }

    // ---------------------------------------------------------------- formatações do painel

    [Theory]
    [InlineData(0UL, "0 B")]
    [InlineData(512UL, "512 B")]
    [InlineData(1_288_490_189UL, "1,2 GB")]
    [InlineData(15_891_378_995UL, "14,8 GB")]
    [InlineData(157_286_400UL, "150 MB")]
    public void Formato_QuantidadeConsumida(ulong bytes, string esperado) =>
        Assert.Equal(esperado, SpeedFormatter.FormatarQuantidade(bytes));

    [Theory]
    [InlineData(300_000_000UL, "300 Mbps")]
    [InlineData(1_000_000_000UL, "1 Gbps")]
    [InlineData(2_400_000_000UL, "2,4 Gbps")]
    public void Formato_Link(ulong bps, string esperado) => Assert.Equal(esperado, SpeedFormatter.FormatarLink(bps));

    [Fact]
    public void Formato_SepararNumeroEUnidade() =>
        Assert.Equal(("38,7", "MB/s"), SpeedFormatter.SepararNumeroEUnidade("38,7 MB/s"));
}
