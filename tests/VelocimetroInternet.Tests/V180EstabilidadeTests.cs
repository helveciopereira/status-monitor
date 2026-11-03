using System.IO;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Tests;

/// <summary>
/// v1.8.0 — regra da coleta completa por crescimento (<see cref="GuardaMemoria"/>) e o baixador
/// de um HttpClient só (<see cref="BaixadorHttp"/>) contra um servidor local.
/// </summary>
public sealed class V180EstabilidadeTests
{
    private const long MB = 1024 * 1024;

    [Fact]
    public void PrimeiraMedicao_SoFixaReferencia_NuncaColeta()
    {
        var g = new GuardaMemoria();
        Assert.False(g.DeveColetar(300 * MB));
        Assert.Equal(300 * MB, g.Base);
    }

    [Fact]
    public void ColetaSoQuandoSubiuOLimiar_EReferenciaPassaASerOQueSobrou()
    {
        var g = new GuardaMemoria();
        g.DeveColetar(100 * MB);
        Assert.False(g.DeveColetar(100 * MB + GuardaMemoria.LimiarCrescimento - 1));
        Assert.True(g.DeveColetar(100 * MB + GuardaMemoria.LimiarCrescimento));

        // Coletou e sobraram 90 MB: a próxima só com 90 + 16
        g.RegistrarAposColeta(90 * MB);
        Assert.Equal(90 * MB, g.Base);
        Assert.False(g.DeveColetar(105 * MB));
        Assert.True(g.DeveColetar(106 * MB));
    }

    [Fact]
    public void MemoriaQueCai_NaoColeta()
    {
        var g = new GuardaMemoria();
        g.DeveColetar(200 * MB);
        Assert.False(g.DeveColetar(150 * MB));
    }

    [Fact]
    public async Task BaixadorHttp_BaixaTextoBytesEArquivo_ComProgresso_ESegueRedirecionamento()
    {
        // Servidor local mínimo: /txt (texto), /bin (3 MB, para o progresso), /vai → 302 → /txt
        using var servidor = new System.Net.HttpListener();
        int porta = PortaLivre();
        servidor.Prefixes.Add($"http://127.0.0.1:{porta}/");
        servidor.Start();
        byte[] grande = new byte[3 * MB];
        new Random(1).NextBytes(grande);
        _ = Task.Run(async () =>
        {
            while (servidor.IsListening)
            {
                System.Net.HttpListenerContext ctx;
                try { ctx = await servidor.GetContextAsync(); } catch { return; }
                switch (ctx.Request.Url!.AbsolutePath)
                {
                    case "/txt":
                        byte[] t = System.Text.Encoding.UTF8.GetBytes("{\"ok\":true}");
                        ctx.Response.ContentLength64 = t.Length;
                        await ctx.Response.OutputStream.WriteAsync(t);
                        break;
                    case "/bin":
                        ctx.Response.ContentLength64 = grande.Length;
                        await ctx.Response.OutputStream.WriteAsync(grande);
                        break;
                    case "/vai":
                        ctx.Response.Redirect($"http://127.0.0.1:{porta}/txt");
                        break;
                    default:
                        ctx.Response.StatusCode = 404;
                        break;
                }

                ctx.Response.Close();
            }
        });

        var b = new BaixadorHttp();
        string base_ = $"http://127.0.0.1:{porta}";
        Assert.Equal("{\"ok\":true}", await b.DownloadString(base_ + "/txt", null, 1));
        Assert.Equal("{\"ok\":true}", await b.DownloadString(base_ + "/vai", null, 1));
        Assert.Equal(grande, await b.DownloadBytes(base_ + "/bin", null, 1));

        string arquivo = Path.Combine(Path.GetTempPath(), $"baixador-{Guid.NewGuid():N}.bin");
        var progresso = new List<int>();
        try
        {
            await b.DownloadFile(base_ + "/bin", arquivo, progresso.Add, null, 1);
            Assert.Equal(grande, await File.ReadAllBytesAsync(arquivo));
            Assert.Equal(100, progresso[^1]);
            Assert.True(progresso.Count >= 2, "o progresso deve ser relatado ao longo do download");
            Assert.Equal(progresso.OrderBy(p => p), progresso); // nunca anda para trás
        }
        finally
        {
            File.Delete(arquivo);
        }

        // 404 vira HttpRequestException com StatusCode — é o que ServicoAtualizacao.Classificar lê
        var ex = await Assert.ThrowsAsync<System.Net.Http.HttpRequestException>(() => b.DownloadString(base_ + "/nada", null, 1));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal("nenhuma versão publicada ainda", ServicoAtualizacao.Classificar(ex));
        servidor.Stop();
    }

    private static int PortaLivre()
    {
        using var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        return ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
    }
}
