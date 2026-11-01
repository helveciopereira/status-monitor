using VelocimetroInternet.Medicao;

namespace VelocimetroInternet.Tests;

/// <summary>
/// v1.3.0 — Onda 8: consumo por aplicativo. Só as contas puras (período, caminho de
/// dispositivo, nome, soma entre redes, "Outros"). A leitura do Windows em si depende da
/// máquina — conferida por render com dados reais e na validação manual.
/// </summary>
public sealed class V130ConsumoPorAppTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 15, 14, 30, 0, TimeSpan.FromHours(-3));

    // ---------------------------------------------------------------- Intervalo

    [Fact]
    public void Intervalo_CadaPeriodo()
    {
        Assert.Equal((Agora.AddMinutes(-5), Agora), ConsumoPorApp.Intervalo(PeriodoConsumoApp.CincoMinutos, Agora));
        Assert.Equal((Agora.AddHours(-1), Agora), ConsumoPorApp.Intervalo(PeriodoConsumoApp.UmaHora, Agora));
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 0, 0, 0, Agora.Offset), ConsumoPorApp.Intervalo(PeriodoConsumoApp.Hoje, Agora).Inicio);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, Agora.Offset), ConsumoPorApp.Intervalo(PeriodoConsumoApp.EsteMes, Agora).Inicio);
    }

    // ---------------------------------------------------------------- Caminho de dispositivo

    private static readonly Dictionary<string, string> Mapa = new(StringComparer.OrdinalIgnoreCase)
    {
        [@"\device\harddiskvolume1"] = "D:",
        [@"\device\harddiskvolume4"] = "C:",
    };

    [Fact]
    public void Caminho_TrocaODispositivoPelaUnidade()
    {
        Assert.Equal(@"C:\program files\app.exe",
            ConsumoPorApp.CaminhoDeDispositivo(@"\device\harddiskvolume4\program files\app.exe", Mapa));
        // Maiúsculas/minúsculas não importam (o Windows devolve tudo em minúsculas)
        Assert.Equal(@"D:\Jogos\x.exe", ConsumoPorApp.CaminhoDeDispositivo(@"\Device\HarddiskVolume1\Jogos\x.exe", Mapa));
    }

    [Fact]
    public void Caminho_VolumeTemQueCasarInteiro()
    {
        // "harddiskvolume1" NÃO pode casar com "harddiskvolume14"
        Assert.Null(ConsumoPorApp.CaminhoDeDispositivo(@"\device\harddiskvolume14\a.exe", Mapa));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Microsoft.StartExperiencesApp_8wekyb3d8bbwe")]
    [InlineData(@"C:\ja\e\normal.exe")]
    public void Caminho_ForaDoFormato_DevolveNull(string id) => Assert.Null(ConsumoPorApp.CaminhoDeDispositivo(id, Mapa));

    // ---------------------------------------------------------------- Nome amigável

    [Theory]
    [InlineData(@"\device\x\msedge.exe", "Nome do Windows", "Microsoft Edge", "Nome do Windows")]
    [InlineData(@"\device\x\msedge.exe", null, "Microsoft Edge", "Microsoft Edge")]
    [InlineData(@"\device\x\meuapp.exe", "  ", "", "meuapp")]
    [InlineData("Microsoft.StartExperiencesApp_8wekyb3d8bbwe", null, null, "StartExperiencesApp")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0", null, null, "SpotifyMusic")]
    [InlineData("", null, null, "Sistema (não identificado)")]
    public void Nome_OrdemDePreferencia(string id, string? nomeWindows, string? descricao, string esperado) =>
        Assert.Equal(esperado, ConsumoPorApp.NomeAmigavel(id, nomeWindows, descricao));

    // ---------------------------------------------------------------- Agrupar

    private static (string?, string?) SemArquivo(string _) => (null, null);

    [Fact]
    public void Agrupar_SomaOMesmoAppEmRedesDiferentes_EOrdena()
    {
        UsoBruto[] brutos =
        [
            new(@"\device\v\edge.exe", null, 100, 10),   // Wi-Fi de casa
            new(@"\DEVICE\V\EDGE.EXE", null, 50, 5),     // Wi-Fi do trabalho (mesmo app)
            new(@"\device\v\curl.exe", null, 400, 0),
            new(@"\device\v\parado.exe", null, 0, 0),    // não trafegou: some
        ];

        List<UsoApp> r = ConsumoPorApp.Agrupar(brutos, SemArquivo);

        Assert.Equal(2, r.Count);
        Assert.Equal("curl", r[0].Nome);
        Assert.Equal("edge", r[1].Nome);
        Assert.Equal(150UL, r[1].Recebidos);
        Assert.Equal(15UL, r[1].Enviados);
    }

    [Fact]
    public void Agrupar_EmpateNoTotal_OrdemAlfabetica()
    {
        UsoBruto[] brutos = [new(@"\d\v\zeta.exe", null, 10, 0), new(@"\d\v\alfa.exe", null, 10, 0)];
        List<UsoApp> r = ConsumoPorApp.Agrupar(brutos, SemArquivo);
        Assert.Equal(["alfa", "zeta"], r.Select(u => u.Nome));
    }

    [Fact]
    public void Agrupar_UsaODescricaoDoArquivoECaminho()
    {
        List<UsoApp> r = ConsumoPorApp.Agrupar([new(@"\d\v\msedge.exe", null, 1, 1)],
            _ => (@"C:\Edge\msedge.exe", "Microsoft Edge"));
        Assert.Equal("Microsoft Edge", r[0].Nome);
        Assert.Equal(@"C:\Edge\msedge.exe", r[0].Caminho);
    }

    // ---------------------------------------------------------------- Resumir ("Outros")

    private static List<UsoApp> Apps(int n) =>
        [.. Enumerable.Range(1, n).Select(i => new UsoApp($"id{i}", $"App {i}", null, (ulong)(1000 - i), (ulong)i))];

    [Fact]
    public void Resumir_ListaCurta_VoltaInteira() => Assert.Equal(10, ConsumoPorApp.Resumir(Apps(10)).Count);

    [Fact]
    public void Resumir_ListaLonga_JuntaOrestoEmOutros()
    {
        List<UsoApp> todos = Apps(25);
        List<UsoApp> r = ConsumoPorApp.Resumir(todos);

        Assert.Equal(ConsumoPorApp.MaximoLinhas, r.Count);
        UsoApp outros = r[^1];
        Assert.Equal("Outros (16 apps)", outros.Nome);
        Assert.Null(outros.Caminho);
        // Nada se perde: a soma da lista resumida = a soma da lista inteira
        Assert.Equal(todos.Sum(u => (decimal)u.Recebidos), r.Sum(u => (decimal)u.Recebidos));
        Assert.Equal(todos.Sum(u => (decimal)u.Enviados), r.Sum(u => (decimal)u.Enviados));
    }
}
