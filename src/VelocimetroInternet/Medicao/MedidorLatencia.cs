using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace VelocimetroInternet.Medicao;

/// <summary>
/// v1.1.0: última medição de latência. null = sem resposta (tempo esgotado, sem rede, sem
/// roteador conhecido). <see cref="Medido"/> = false enquanto nenhuma rodada terminou.
/// </summary>
public sealed record LatenciaAtual(long? RoteadorMs, long? InternetMs, bool Medido)
{
    public static readonly LatenciaAtual Nenhuma = new(null, null, false);

    /// <summary>"Roteador 3 ms · Internet 18 ms" (sem resposta → "—").</summary>
    public string Texto() =>
        !Medido ? "medindo…" : $"Roteador {Ms(RoteadorMs)} · Internet {Ms(InternetMs)}";

    private static string Ms(long? ms) => ms is long v ? $"{v} ms" : "—";
}

/// <summary>
/// v1.1.0 (Onda 6): latência OPCIONAL (desligada por padrão — é o único recurso do app que manda
/// pacotes para fora). Um ping ICMP a cada 5 s para o roteador (gateway da interface medida) e
/// para 1.1.1.1, com 1 s de limite. Custo desprezível: 2 pacotes de 32 bytes a cada 5 s.
///
/// "Internet lenta" muitas vezes é latência, não banda: roteador baixo + internet alta = o
/// problema está na operadora; roteador alto = o problema é o Wi-Fi/rede da casa.
/// </summary>
public sealed class MedidorLatencia : IDisposable
{
    public const int IntervaloSegundos = 5;
    public const int LimiteMs = 1000;
    private static readonly IPAddress AlvoInternet = IPAddress.Parse("1.1.1.1");

    private readonly Func<string?> _nomeInterface;
    private CancellationTokenSource? _cts;
    private volatile LatenciaAtual _atual = LatenciaAtual.Nenhuma;

    /// <param name="nomeInterface">Apelido da interface medida agora ("Wi-Fi"), para achar o gateway.</param>
    public MedidorLatencia(Func<string?> nomeInterface) => _nomeInterface = nomeInterface;

    public LatenciaAtual Atual => _atual;

    public bool Ligado => _cts is not null;

    /// <summary>Liga/desliga. Ao desligar, esquece a última medição (o painel some a linha).</summary>
    public void Definir(bool ligar)
    {
        if (ligar == Ligado)
        {
            return;
        }

        if (ligar)
        {
            _cts = new CancellationTokenSource();
            _ = LacoAsync(_cts.Token);
        }
        else
        {
            _cts!.Cancel();
            _cts.Dispose();
            _cts = null;
            _atual = LatenciaAtual.Nenhuma;
        }
    }

    private async Task LacoAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(IntervaloSegundos));
        try
        {
            do
            {
                IPAddress? gateway = Gateway(_nomeInterface());
                // Os dois em paralelo: a rodada inteira leva no máximo 1 s
                Task<long?> roteador = gateway is null ? Task.FromResult<long?>(null) : PingarAsync(gateway);
                Task<long?> internet = PingarAsync(AlvoInternet);
                await Task.WhenAll(roteador, internet).ConfigureAwait(false);
                if (!ct.IsCancellationRequested)
                {
                    _atual = new LatenciaAtual(roteador.Result, internet.Result, true);
                }
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Desligado pelo usuário ou app fechando
        }
    }

    private static async Task<long?> PingarAsync(IPAddress alvo)
    {
        try
        {
            using var ping = new Ping();
            PingReply r = await ping.SendPingAsync(alvo, LimiteMs).ConfigureAwait(false);
            return r.Status == IPStatus.Success ? r.RoundtripTime : null;
        }
        catch (Exception ex) when (ex is PingException or SocketException or InvalidOperationException)
        {
            // Sem rede / pilha reiniciando: conta como "sem resposta", nunca derruba o app
            return null;
        }
    }

    /// <summary>Gateway IPv4 da interface pelo apelido (a mesma que o medidor está contando).</summary>
    private static IPAddress? Gateway(string? alias)
    {
        if (string.IsNullOrEmpty(alias))
        {
            return null;
        }

        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, alias, StringComparison.OrdinalIgnoreCase))?
                .GetIPProperties().GatewayAddresses
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    public void Dispose() => Definir(false);
}
