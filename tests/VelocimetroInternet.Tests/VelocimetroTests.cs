using VelocimetroInternet.Apresentacao;
using VelocimetroInternet.Medicao;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Tests;

public sealed class VelocimetroTests
{
    [Fact]
    public void SpeedFormatter_PadraoBytesPorSegundo_UsaVirgulaBrasileiraETresAlgarismos()
    {
        var formatter = new SpeedFormatter(ModoUnidadeVelocidade.BytesPorSegundo);

        // 1,24 MB/s = 1.24 * 1024 * 1024 = 1_300_234 bytes/s
        string upload = formatter.FormatarComSeta("↑", 1.24 * 1024 * 1024);
        Assert.Equal("↑ 1,24 MB/s", upload);

        // 38,7 MB/s = 38.7 * 1024 * 1024 bytes/s
        string download = formatter.FormatarComSeta("↓", 38.7 * 1024 * 1024);
        Assert.Equal("↓ 38,7 MB/s", download);
    }

    [Fact]
    public void SpeedFormatter_AlternativaBitsPorSegundo_FormataMbpsCorretamente()
    {
        var formatter = new SpeedFormatter(ModoUnidadeVelocidade.BitsPorSegundo);

        // 100 Mbps = 12_500_000 bytes/s
        string resultado = formatter.FormatarComSeta("↓", 12_500_000);
        Assert.Equal("↓ 100 Mbps", resultado);
    }

    [Fact]
    public void SpeedFormatter_Histerese_SoDesceDeUnidadeAposDuasLeiturasAbaixoDoLimiar()
    {
        var formatter = new SpeedFormatter(ModoUnidadeVelocidade.BytesPorSegundo);

        // Sobe imediatamente para MB/s
        Assert.Equal("1,50 MB/s", formatter.FormatarValorEUnidade(1.5 * 1024 * 1024));

        // 1ª leitura em 500 KB/s (~0,49 MB/s): histerese segura na unidade MB/s
        Assert.Equal("0,49 MB/s", formatter.FormatarValorEUnidade(500 * 1024));

        // 2ª leitura consecutiva em 500 KB/s: agora desce para KB/s
        Assert.Equal("500 KB/s", formatter.FormatarValorEUnidade(500 * 1024));
    }

    [Fact]
    public void SpeedCalculator_DescartaPrimeiraAmostraEDeltaNegativo_CalculaEmaCorretamente()
    {
        var calc = new SpeedCalculator(fatorSuavizacao: 0.5);

        // 1ª chamada apenas fixa a base
        VelocidadeRede v0 = calc.CalcularProximaAmostra(1_000_000, 500_000, TimeSpan.FromSeconds(1));
        Assert.False(v0.AmostraValida);

        // 2ª chamada: +2 MB recebidos e +1 MB enviados em 1s (com EMA 0.5 a partir de 0 -> 1 MB/s e 0.5 MB/s)
        VelocidadeRede v1 = calc.CalcularProximaAmostra(3_000_000, 1_500_000, TimeSpan.FromSeconds(1));
        Assert.True(v1.AmostraValida);
        Assert.Equal(1_000_000, v1.DownloadBytesPorSegundo, precision: 1);
        Assert.Equal(500_000, v1.UploadBytesPorSegundo, precision: 1);

        // Contador zerou (reconexão do Wi-Fi -> delta negativo): deve ser descartado sem corromper EMA
        VelocidadeRede vReset = calc.CalcularProximaAmostra(100, 50, TimeSpan.FromSeconds(1));
        Assert.False(vReset.AmostraValida);
        Assert.Equal(1_000_000, vReset.DownloadBytesPorSegundo, precision: 1);
    }

    [Fact]
    public void InterfaceFilter_IncluiWifiEEthernetFisicos_BloqueiaTailscaleERadminVpn()
    {
        var wifiReal = new AmostraInterfaceRede(
            InterfaceLuid: 1UL,
            Alias: "Wi-Fi",
            Descricao: "Intel(R) Wi-Fi 6E AX211 160MHz",
            Tipo: InterfaceFilter.IF_TYPE_IEEE80211,
            EhHardware: true,
            EstaOperacional: true,
            MidiaConectada: true,
            BytesRecebidos: 1000,
            BytesEnviados: 500);

        var tailscale = new AmostraInterfaceRede(
            InterfaceLuid: 2UL,
            Alias: "Tailscale",
            Descricao: "Tailscale Tunnel",
            Tipo: InterfaceFilter.IF_TYPE_ETHERNET_CSMACD,
            EhHardware: true,
            EstaOperacional: true,
            MidiaConectada: true,
            BytesRecebidos: 1000,
            BytesEnviados: 500);

        var radmin = new AmostraInterfaceRede(
            InterfaceLuid: 3UL,
            Alias: "Radmin VPN",
            Descricao: "Famatech Radmin VPN Ethernet Adapter",
            Tipo: InterfaceFilter.IF_TYPE_ETHERNET_CSMACD,
            EhHardware: true,
            EstaOperacional: true,
            MidiaConectada: true,
            BytesRecebidos: 1000,
            BytesEnviados: 500);

        var filtroNdis = new AmostraInterfaceRede(
            InterfaceLuid: 4UL,
            Alias: "Wi-Fi-WFP",
            Descricao: "Intel(R) Wi-Fi 6E AX211 - WFP Native MAC Layer LightWeight Filter-0000",
            Tipo: InterfaceFilter.IF_TYPE_IEEE80211,
            EhHardware: true,
            EstaOperacional: true,
            MidiaConectada: true,
            BytesRecebidos: 1000,
            BytesEnviados: 500);

        Assert.True(InterfaceFilter.DeveContabilizar(in wifiReal));
        Assert.False(InterfaceFilter.DeveContabilizar(in tailscale));
        Assert.False(InterfaceFilter.DeveContabilizar(in radmin));
        Assert.False(InterfaceFilter.DeveContabilizar(in filtroNdis));
    }

    [Fact]
    public void TaskbarLayout_MedicaoRealDestaMaquina_PosicionaNaVagaDoClimaEmDuasLinhas()
    {
        // Geometria real medida nesta máquina (01-ANALISE.md §3.1): 2560x1600 a 150% DPI
        var rectBarra = new NativeMethods.RECT { Left = 0, Top = 1528, Right = 2560, Bottom = 1600 };
        PosicaoWidgetBarra pos = TaskbarLayout.CalcularVagaDoClima(
            rectBarra,
            bordaDireitaAppsPx: 1169,
            bordaEsquerdaBandejaPx: 1797,
            escalaDpi: 1.5);

        Assert.Equal(EstadoColisaoBarra.NormalDuasLinhas, pos.Estado);
        Assert.Equal(628, pos.EspacoLivreFisicoPx);
        Assert.True(pos.XTelaPx > 1169);
        Assert.True(pos.XTelaPx + pos.LarguraFisicaPx <= 1797);
    }

    [Fact]
    public void TaskbarLayout_QuandoEspacoAperta_CompactaParaUmaLinhaEDepoisOculta()
    {
        var rectBarra = new NativeMethods.RECT { Left = 0, Top = 1528, Right = 2560, Bottom = 1600 };

        // Espaço intermediário (145 px físicos a 150% DPI): deve compactar para 1 linha
        PosicaoWidgetBarra compacto = TaskbarLayout.CalcularVagaDoClima(
            rectBarra,
            bordaDireitaAppsPx: 1652,
            bordaEsquerdaBandejaPx: 1797,
            escalaDpi: 1.5);
        Assert.Equal(EstadoColisaoBarra.CompactoUmaLinha, compacto.Estado);

        // Espaço mínimo (60 px físicos): deve ocultar para não cobrir botões de apps
        PosicaoWidgetBarra oculto = TaskbarLayout.CalcularVagaDoClima(
            rectBarra,
            bordaDireitaAppsPx: 1737,
            bordaEsquerdaBandejaPx: 1797,
            escalaDpi: 1.5);
        Assert.Equal(EstadoColisaoBarra.OcultoPorColisao, oculto.Estado);
    }

    [Fact]
    public void NetworkSampler_GetIfTable2_ExecutaSemVazarEEncontraInterfaceFisica()
    {
        var sampler = new NetworkSampler();
        LeituraAcumuladaRede leitura = sampler.LerContadoresAtuais();

        Assert.True(leitura.InterfacesFisicasAtivas >= 1, $"Esperava >= 1 interface física ativa, obteve: {leitura.ResumoInterfacesAtivas}");
        Assert.True(leitura.TotalBytesRecebidos > 0);
        Assert.True(leitura.TotalBytesEnviados > 0);
    }
}
