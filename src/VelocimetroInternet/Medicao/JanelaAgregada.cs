namespace VelocimetroInternet.Medicao;

/// <summary>Janelas do gráfico do painel (v1.1.0, Onda 6).</summary>
public enum JanelaGrafico
{
    /// <summary>Últimos 60 s, um ponto por medição (o gráfico de sempre).</summary>
    Minuto,

    /// <summary>Últimos 5 min, um ponto a cada 5 s.</summary>
    CincoMinutos,

    /// <summary>Última hora, um ponto por minuto.</summary>
    Hora
}

/// <summary>v1.1.0: conversão entre a janela e o código gravado nas configurações.</summary>
public static class JanelasGrafico
{
    public static JanelaGrafico DeCodigo(string? codigo) => codigo switch
    {
        "5min" => JanelaGrafico.CincoMinutos,
        "1h" => JanelaGrafico.Hora,
        _ => JanelaGrafico.Minuto
    };

    public static string Codigo(JanelaGrafico janela) => janela switch
    {
        JanelaGrafico.CincoMinutos => "5min",
        JanelaGrafico.Hora => "1h",
        _ => "1min"
    };

    /// <summary>Texto enquanto a janela ainda não tem 2 pontos (as longas demoram a encher).</summary>
    public static string TextoAguardando(JanelaGrafico janela) => janela switch
    {
        JanelaGrafico.CincoMinutos => "Juntando dados (1 ponto a cada 5 s)…",
        JanelaGrafico.Hora => "Juntando dados (1 ponto por minuto)…",
        _ => "Aguardando amostras…"
    };
}

/// <summary>
/// v1.1.0: o que o gráfico do painel desenha para uma janela — pontos (do mais antigo ao mais
/// novo), quantos pontos a janela comporta (largura do eixo) e a média/pico de cada sentido.
/// </summary>
public sealed record SerieGrafico(
    IReadOnlyList<PontoHistorico> Pontos, int Capacidade,
    double MediaUpload, double MediaDownload, double PicoUpload, double PicoDownload)
{
    public static SerieGrafico De(IReadOnlyList<PontoHistorico> pontos, int capacidade)
    {
        double somaUp = 0, somaDown = 0, picoUp = 0, picoDown = 0;
        foreach (PontoHistorico p in pontos)
        {
            somaUp += p.UploadBps;
            somaDown += p.DownloadBps;
            picoUp = Math.Max(picoUp, p.UploadBps);
            picoDown = Math.Max(picoDown, p.DownloadBps);
        }

        int n = pontos.Count;
        return new SerieGrafico(pontos, capacidade,
            n > 0 ? somaUp / n : 0, n > 0 ? somaDown / n : 0, picoUp, picoDown);
    }
}

/// <summary>
/// v1.1.0 (Onda 6): anel de 60 pontos em que cada ponto é a MÉDIA REAL de um "balde" de tempo
/// (5 s ou 60 s) — calculada pelos BYTES contados no balde ÷ segundos do balde, não pela média das
/// velocidades suavizadas (que esconderia rajadas curtas e dependeria da suavização escolhida).
///
/// Classe pura, sem trava: o <see cref="HistoricoRede"/> chama tudo sob a trava dele.
/// Tempo sem medição (pausa, sem rede) não entra no balde: o gráfico "encolhe" esses buracos em vez
/// de desenhar zeros falsos.
/// </summary>
public sealed class JanelaAgregada
{
    public const int Capacidade = 60;

    private readonly double _segundosPorPonto;
    private readonly PontoHistorico[] _anel = new PontoHistorico[Capacidade];
    private int _inicio;
    private int _quantidade;

    // Balde em andamento
    private double _segundosNoBalde;
    private double _bytesEnviadosNoBalde;
    private double _bytesRecebidosNoBalde;

    public JanelaAgregada(double segundosPorPonto) =>
        _segundosPorPonto = segundosPorPonto > 0 ? segundosPorPonto : 1;

    public double SegundosPorPonto => _segundosPorPonto;

    /// <summary>Soma um intervalo medido; fecha o balde (vira ponto) ao completar o tempo do ponto.</summary>
    public void Somar(double segundos, ulong bytesEnviados, ulong bytesRecebidos)
    {
        if (!(segundos > 0) || !double.IsFinite(segundos))
        {
            return;
        }

        _segundosNoBalde += segundos;
        _bytesEnviadosNoBalde += bytesEnviados;
        _bytesRecebidosNoBalde += bytesRecebidos;

        // Tolerância de 1 ms: 10 × 0,5 s em ponto flutuante pode dar 4,9999…
        if (_segundosNoBalde + 0.001 < _segundosPorPonto)
        {
            return;
        }

        var ponto = new PontoHistorico(_bytesEnviadosNoBalde / _segundosNoBalde, _bytesRecebidosNoBalde / _segundosNoBalde);
        _anel[(_inicio + _quantidade) % Capacidade] = ponto;
        if (_quantidade < Capacidade)
        {
            _quantidade++;
        }
        else
        {
            _inicio = (_inicio + 1) % Capacidade;
        }

        _segundosNoBalde = 0;
        _bytesEnviadosNoBalde = 0;
        _bytesRecebidosNoBalde = 0;
    }

    /// <summary>Pontos fechados, do mais antigo ao mais novo.</summary>
    public PontoHistorico[] Pontos()
    {
        var r = new PontoHistorico[_quantidade];
        for (int i = 0; i < _quantidade; i++)
        {
            r[i] = _anel[(_inicio + i) % Capacidade];
        }

        return r;
    }
}
