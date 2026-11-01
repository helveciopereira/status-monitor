using System.IO;
using System.Net.Http;
using VelocimetroInternet.Apresentacao;
using VelocimetroInternet.Config;
using VelocimetroInternet.Medicao;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Tests;

/// <summary>
/// v1.2.0 — Onda 7: modos de exibição do widget (duas linhas / uma linha / só download) e as
/// contas puras do teste de velocidade. O teste contra o servidor real depende da rede da
/// máquina — fica na validação manual.
/// </summary>
public sealed class V120ModosETesteTests
{
    // Barra de 2560 px a 150 % (a mesma das ondas anteriores)
    private static readonly NativeMethods.RECT Barra = new() { Left = 0, Top = 1528, Right = 2560, Bottom = 1600 };

    // ---------------------------------------------------------------- ModosExibicao

    [Theory]
    [InlineData(ModoExibicaoWidget.DuasLinhas, "duas-linhas")]
    [InlineData(ModoExibicaoWidget.UmaLinha, "uma-linha")]
    [InlineData(ModoExibicaoWidget.SoDownload, "so-download")]
    public void Modos_CodigoIdaEVolta(ModoExibicaoWidget modo, string codigo)
    {
        Assert.Equal(codigo, ModosExibicao.Codigo(modo));
        Assert.Equal(modo, ModosExibicao.DeCodigo(codigo));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tres-linhas")]
    public void Modos_CodigoDesconhecido_VoltaParaDuasLinhas(string? codigo) =>
        Assert.Equal(ModoExibicaoWidget.DuasLinhas, ModosExibicao.DeCodigo(codigo));

    [Fact]
    public void Configuracoes_ModoPadraoENormalizacao()
    {
        Assert.Equal("duas-linhas", new Configuracoes().ModoExibicao);

        var invalido = new Configuracoes { ModoExibicao = "gigante" };
        invalido.Normalizar();
        Assert.Equal("duas-linhas", invalido.ModoExibicao);

        var valido = new Configuracoes { ModoExibicao = "so-download" };
        valido.Normalizar();
        Assert.Equal("so-download", valido.ModoExibicao);
    }

    // ---------------------------------------------------------------- Larguras e tamanho preferido

    [Fact]
    public void Preferido_UmaLinha_EhALinhaCompacta()
    {
        var (largura, estado) = TaskbarLayout.Preferido(false, 1.0, ModoExibicaoWidget.UmaLinha);
        Assert.Equal(TaskbarLayout.LarguraLogicaCompactaPx, largura);
        Assert.Equal(EstadoColisaoBarra.CompactoUmaLinha, estado);
    }

    [Theory]
    [InlineData(ModoExibicaoWidget.DuasLinhas)]
    [InlineData(ModoExibicaoWidget.SoDownload)]
    public void Preferido_DuasLinhasESoDownload_UsamABlocoNormal(ModoExibicaoWidget modo)
    {
        foreach (bool sistema in new[] { false, true })
        {
            var (largura, estado) = TaskbarLayout.Preferido(sistema, 1.0, modo);
            Assert.Equal(TaskbarLayout.LarguraLogicaNormal(sistema, 1.0), largura);
            Assert.Equal(EstadoColisaoBarra.NormalDuasLinhas, estado);
        }
    }

    [Fact]
    public void LinhaCompacta_SoDownload_EhMaisEstreita()
    {
        Assert.Equal(TaskbarLayout.LarguraLogicaCompactaSoDownloadPx,
            TaskbarLayout.LarguraLogicaCompacta(false, 1.0, ModoExibicaoWidget.SoDownload));
        Assert.True(TaskbarLayout.LarguraLogicaCompacta(true, 1.0, ModoExibicaoWidget.SoDownload)
            < TaskbarLayout.LarguraLogicaCompacta(true, 1.0, ModoExibicaoWidget.DuasLinhas));
        // Os outros modos mantêm a largura antiga (sem regressão)
        Assert.Equal(TaskbarLayout.LarguraLogicaCompacta(true, 1.2),
            TaskbarLayout.LarguraLogicaCompacta(true, 1.2, ModoExibicaoWidget.UmaLinha));
    }

    // ---------------------------------------------------------------- Vaga do Clima por modo

    [Fact]
    public void Vaga_UmaLinhaComEspacoSobrando_FicaNaLinhaUnicaComALarguraDela()
    {
        PosicaoWidgetBarra p = TaskbarLayout.CalcularVagaDoClima(Barra, 1000, 2200, 1.5, false, 1.0, ModoExibicaoWidget.UmaLinha);
        Assert.Equal(EstadoColisaoBarra.CompactoUmaLinha, p.Estado);
        Assert.Equal((int)Math.Round(TaskbarLayout.LarguraLogicaCompactaPx * 1.5), p.LarguraFisicaPx);
        // Encostado no '^' com a margem de 8 px lógicos
        Assert.Equal(2200 - p.LarguraFisicaPx - 12, p.XTelaPx);
    }

    [Fact]
    public void Vaga_DuasLinhasNoMesmoEspaco_ContinuaNormal()
    {
        PosicaoWidgetBarra p = TaskbarLayout.CalcularVagaDoClima(Barra, 1000, 2200, 1.5);
        Assert.Equal(EstadoColisaoBarra.NormalDuasLinhas, p.Estado);
        Assert.Equal((int)Math.Round(TaskbarLayout.LarguraLogicaNormalPx * 1.5), p.LarguraFisicaPx);
    }

    [Fact]
    public void Vaga_SoDownloadApertado_CaiNaLinhaUnicaEstreita()
    {
        // Normal (sem CPU/RAM) = 129 px físicos; 110 px livres ≥ 75 % de 129 + 12 → compacto
        PosicaoWidgetBarra p = TaskbarLayout.CalcularVagaDoClima(Barra, 2090, 2200, 1.5, false, 1.0, ModoExibicaoWidget.SoDownload);
        Assert.Equal(EstadoColisaoBarra.CompactoUmaLinha, p.Estado);
        Assert.True(p.LarguraFisicaPx <= (int)Math.Round(TaskbarLayout.LarguraLogicaCompactaSoDownloadPx * 1.5));
        Assert.True(p.LarguraFisicaPx <= 110 - 12);
    }

    [Fact]
    public void Vaga_SemEspaco_OcultaEmQualquerModo()
    {
        foreach (ModoExibicaoWidget modo in Enum.GetValues<ModoExibicaoWidget>())
        {
            PosicaoWidgetBarra p = TaskbarLayout.CalcularVagaDoClima(Barra, 2180, 2200, 1.5, true, 1.0, modo);
            Assert.Equal(EstadoColisaoBarra.OcultoPorColisao, p.Estado);
        }
    }

    // ---------------------------------------------------------------- CalculoTeste

    [Fact]
    public void Teste_BitsPorSegundo_ProtegeTempoInvalido()
    {
        Assert.Equal(80_000_000, CalculoTeste.BitsPorSegundo(10_000_000, 1), 6);
        Assert.Equal(0, CalculoTeste.BitsPorSegundo(10_000_000, 0));
        Assert.Equal(0, CalculoTeste.BitsPorSegundo(10_000_000, double.NaN));
    }

    [Fact]
    public void Teste_DescontaOAquecimento()
    {
        // 10 MB em 5 s, sendo 1 MB no 1º segundo → (9 MB × 8) ÷ 4 s = 18 Mbps
        Assert.Equal(18_000_000, CalculoTeste.VelocidadeSemAquecimento(10_000_000, 5, 1_000_000, 1), 3);
    }

    [Fact]
    public void Teste_EtapaCurtaDemais_UsaOTotal()
    {
        // Terminou pelo teto logo após o aquecimento (janela de 0,2 s): não divide por quase zero
        Assert.Equal(CalculoTeste.BitsPorSegundo(1_000_000, 1.2),
            CalculoTeste.VelocidadeSemAquecimento(1_000_000, 1.2, 500_000, 1), 3);
        // Sem bytes no aquecimento: idem
        Assert.Equal(CalculoTeste.BitsPorSegundo(4_000_000, 4),
            CalculoTeste.VelocidadeSemAquecimento(4_000_000, 4, 0, 1), 3);
    }

    [Fact]
    public void Teste_Mediana()
    {
        Assert.Equal(0, CalculoTeste.Mediana([]));
        Assert.Equal(20, CalculoTeste.Mediana([30, 10, 20]));
        Assert.Equal(15, CalculoTeste.Mediana([40, 10, 20, 10]));
        // Um pico isolado não puxa a mediana (é por isso que ela é usada na latência)
        Assert.Equal(12, CalculoTeste.Mediana([11, 12, 13, 900, 12]));
    }

    [Theory]
    [InlineData(94_300_000, "94,3", "Mbps")]
    [InlineData(512_000_000, "512", "Mbps")]
    [InlineData(1_250_000_000, "1,25", "Gbps")]
    [InlineData(0, "0,0", "Mbps")]
    [InlineData(double.NaN, "0,0", "Mbps")]
    [InlineData(-5, "0,0", "Mbps")]
    public void Teste_FormatarMbps(double bps, string numero, string unidade)
    {
        var (n, u) = CalculoTeste.FormatarMbps(bps);
        Assert.Equal(numero, n);
        Assert.Equal(unidade, u);
    }

    // ---------------------------------------------------------------- v1.2.1: download dava 0

    [Fact]
    public void Pedido_NuncaPassaDe8MB_ERecusaDivideAteDesistir()
    {
        // Padrão (100 MB, 4 conexões) era 12,5 MB por pedido — tamanho que o servidor recusava
        Assert.Equal(TesteVelocidade.TamanhoMaximoPedido, CalculoTeste.TamanhoDoPedido(100_000_000, 4));
        Assert.Equal(64 * 1024, CalculoTeste.TamanhoDoPedido(10_000, 4));
        Assert.Equal(4_000_000, CalculoTeste.PedidoAposRecusa(8_000_000));
        Assert.Equal(TesteVelocidade.TamanhoMinimoPedido, CalculoTeste.PedidoAposRecusa(1_500_000));
        Assert.Equal(0, CalculoTeste.PedidoAposRecusa(TesteVelocidade.TamanhoMinimoPedido));
    }

    [Fact]
    public async Task Teste_ServidorQueRecusaPedidosGrandes_AindaMedeODownload()
    {
        // Imita o speed.cloudflare.com de 2026-11-01: 403 acima de 10 MB
        using var http = new HttpClient(new ServidorFalso(recusarAcimaDe: 10_000_000));
        var t = new TesteVelocidade(http, "https://falso.test",
            new TesteVelocidade.Limites(Conexoes: 4, SegundosPorEtapa: 1, TetoDownloadBytes: 100_000_000,
                TetoUploadBytes: 2_000_000, AmostrasLatencia: 3, SegundosAquecimento: 0.2));

        ResultadoTeste r = await t.ExecutarAsync(null, CancellationToken.None);

        Assert.True(r.BytesBaixados > 0, "o download não pode zerar");
        Assert.True(r.DownloadBps > 0);
        Assert.True(r.UploadBps > 0);
    }

    [Fact]
    public async Task Teste_ServidorQueRecusaTudo_ViraErroENaoZero()
    {
        using var http = new HttpClient(new ServidorFalso(recusarAcimaDe: -1));
        var t = new TesteVelocidade(http, "https://falso.test",
            new TesteVelocidade.Limites(Conexoes: 2, SegundosPorEtapa: 1, AmostrasLatencia: 2));

        await Assert.ThrowsAsync<HttpRequestException>(() => t.ExecutarAsync(null, CancellationToken.None));
    }

    /// <summary>
    /// Servidor de teste em memória: /__down devolve N zeros (403 se N passar do limite, exceto
    /// N = 0, a "latência"); /__up lê o corpo e responde 200. -1 = recusa todo download com dados.
    /// </summary>
    private sealed class ServidorFalso(long recusarAcimaDe) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct)
        {
            if (pedido.Method == HttpMethod.Post)
            {
                await pedido.Content!.CopyToAsync(Stream.Null, ct);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            }

            long n = long.Parse(pedido.RequestUri!.Query.Split('=')[1]);
            if (n > 0 && (recusarAcimaDe < 0 || n > recusarAcimaDe))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden);
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(new Zeros(n)) };
        }
    }

    /// <summary>Fluxo de N zeros sem alocar N bytes.</summary>
    private sealed class Zeros(long tamanho) : Stream
    {
        private long _lidos;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => tamanho;
        public override long Position { get => _lidos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = (int)Math.Min(count, tamanho - _lidos);
            Array.Clear(buffer, offset, n);
            _lidos += n;
            return n;
        }
    }

    [Fact]
    public void Teste_ComparacaoComPlano_FaixasDaAnatel()
    {
        Assert.Null(CalculoTeste.ComparacaoComPlano(100_000_000, 0));
        Assert.Null(CalculoTeste.ComparacaoComPlano(double.NaN, 300));

        string? dentro = CalculoTeste.ComparacaoComPlano(250_000_000, 300);
        Assert.StartsWith("83 %", dentro);
        Assert.Contains("dentro do esperado", dentro);

        string? meio = CalculoTeste.ComparacaoComPlano(150_000_000, 300);
        Assert.StartsWith("50 %", meio);
        Assert.Contains("acima do mínimo", meio);

        string? abaixo = CalculoTeste.ComparacaoComPlano(60_000_000, 300);
        Assert.StartsWith("20 %", abaixo);
        Assert.Contains("ABAIXO", abaixo);
    }
}
