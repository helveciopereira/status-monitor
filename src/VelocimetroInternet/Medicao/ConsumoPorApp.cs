using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using VelocimetroInternet.Sistema;
using Windows.Networking.Connectivity;

namespace VelocimetroInternet.Medicao;

/// <summary>v1.3.0 (Onda 8): período da lista de consumo por aplicativo.</summary>
public enum PeriodoConsumoApp
{
    CincoMinutos,
    UmaHora,
    Hoje,
    EsteMes
}

/// <summary>Uma linha da lista: o app, onde está e quanto baixou/enviou no período.</summary>
public sealed record UsoApp(string Id, string Nome, string? Caminho, ulong Recebidos, ulong Enviados)
{
    public ulong Total => Recebidos + Enviados;
}

/// <summary>Leitura crua do Windows (um app em UM perfil de rede), antes de somar e dar nome.</summary>
public readonly record struct UsoBruto(string Id, string? NomeWindows, ulong Recebidos, ulong Enviados);

/// <summary>
/// v1.3.0 (Onda 8): "quem está usando a minha internet?" — contas PURAS (testáveis sem o Windows).
///
/// Fonte: a contabilidade de uso de dados por app do próprio Windows (a mesma de Configurações ›
/// Rede e Internet › Uso de dados), lida por <see cref="LeitorConsumoPorApp"/>. Medido em
/// 2026-11-01 nesta máquina: funciona SEM administrador e sem pacote; os bytes aparecem com
/// ~1 minuto de atraso (um download feito aos 15 s apareceu aos 80 s). Alternativa descartada:
/// ETW do kernel (tempo real, mas exige administrador → UAC a cada uso).
/// </summary>
public static class ConsumoPorApp
{
    /// <summary>
    /// Quantas linhas a janela mostra; o resto vira "Outros". 10 cabe sem rolagem numa tela
    /// 1080p a 150 % (a barra de rolagem padrão do WPF é clara e destoa do tema escuro).
    /// </summary>
    public const int MaximoLinhas = 10;

    /// <summary>Início e fim da consulta. "Hoje" = desde a meia-noite; "Este mês" = desde o dia 1º.</summary>
    public static (DateTimeOffset Inicio, DateTimeOffset Fim) Intervalo(PeriodoConsumoApp periodo, DateTimeOffset agora) =>
        periodo switch
        {
            PeriodoConsumoApp.CincoMinutos => (agora.AddMinutes(-5), agora),
            PeriodoConsumoApp.UmaHora => (agora.AddHours(-1), agora),
            PeriodoConsumoApp.EsteMes => (new DateTimeOffset(agora.Year, agora.Month, 1, 0, 0, 0, agora.Offset), agora),
            _ => (new DateTimeOffset(agora.Date, agora.Offset), agora)
        };

    /// <summary>
    /// "\device\harddiskvolume4\program files\app.exe" → "C:\program files\app.exe", pelo mapa
    /// dispositivo→unidade (chaves em minúsculas, sem barra no fim). null = não é caminho de
    /// dispositivo (app da Loja, serviço sem arquivo) ou unidade desconhecida.
    /// </summary>
    public static string? CaminhoDeDispositivo(string id, IReadOnlyDictionary<string, string> mapa)
    {
        if (string.IsNullOrEmpty(id) || !id.StartsWith(@"\device\", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach ((string dispositivo, string unidade) in mapa)
        {
            // Precisa casar o volume INTEIRO: "harddiskvolume1" não pode casar com "harddiskvolume10"
            if (id.Length > dispositivo.Length
                && id[dispositivo.Length] == '\\'
                && id.StartsWith(dispositivo, StringComparison.OrdinalIgnoreCase))
            {
                return unidade + id[dispositivo.Length..];
            }
        }

        return null;
    }

    /// <summary>
    /// Nome para a tela: o que o Windows informar; senão a "Descrição do arquivo" do .exe (ex.:
    /// "Microsoft Edge"); senão o nome do arquivo sem extensão; app da Loja = parte legível do
    /// nome do pacote; id vazio = tráfego que o Windows não atribuiu a nenhum programa.
    /// </summary>
    public static string NomeAmigavel(string id, string? nomeWindows, string? descricaoArquivo)
    {
        if (!string.IsNullOrWhiteSpace(nomeWindows))
        {
            return nomeWindows.Trim();
        }

        if (!string.IsNullOrWhiteSpace(descricaoArquivo))
        {
            return descricaoArquivo.Trim();
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return "Sistema (não identificado)";
        }

        if (id.Contains('\\'))
        {
            string arquivo = Path.GetFileNameWithoutExtension(id);
            return string.IsNullOrEmpty(arquivo) ? id : arquivo;
        }

        // Pacote da Loja: "Microsoft.StartExperiencesApp_8wekyb3d8bbwe" → "StartExperiencesApp"
        string semEditor = id.Split('_')[0];
        int ponto = semEditor.LastIndexOf('.');
        return ponto >= 0 && ponto < semEditor.Length - 1 ? semEditor[(ponto + 1)..] : semEditor;
    }

    /// <summary>
    /// Soma o mesmo app vindo de VÁRIOS perfis de rede (cada rede Wi-Fi é um perfil), descarta
    /// quem não trafegou e ordena do maior para o menor. <paramref name="resolver"/> devolve
    /// caminho e descrição do arquivo a partir do id (com cache, no leitor real).
    /// </summary>
    public static List<UsoApp> Agrupar(IEnumerable<UsoBruto> brutos, Func<string, (string? Caminho, string? Descricao)> resolver)
    {
        var lista = new List<UsoApp>();
        foreach (IGrouping<string, UsoBruto> g in brutos.GroupBy(b => b.Id ?? "", StringComparer.OrdinalIgnoreCase))
        {
            ulong rec = 0, env = 0;
            string? nomeWindows = null;
            foreach (UsoBruto b in g)
            {
                rec += b.Recebidos;
                env += b.Enviados;
                nomeWindows ??= string.IsNullOrWhiteSpace(b.NomeWindows) ? null : b.NomeWindows;
            }

            if (rec + env == 0)
            {
                continue;
            }

            (string? caminho, string? descricao) = resolver(g.Key);
            lista.Add(new UsoApp(g.Key, NomeAmigavel(g.Key, nomeWindows, descricao), caminho, rec, env));
        }

        // Empate no total: ordem alfabética, para a lista não "dançar" entre atualizações
        lista.Sort((a, b) => b.Total != a.Total
            ? b.Total.CompareTo(a.Total)
            : string.Compare(a.Nome, b.Nome, StringComparison.CurrentCultureIgnoreCase));
        return lista;
    }

    /// <summary>
    /// Corta a lista em <paramref name="maximo"/> linhas: as primeiras (máximo − 1) + uma linha
    /// "Outros (N apps)" com a soma do resto. Lista curta volta inteira.
    /// </summary>
    public static List<UsoApp> Resumir(IReadOnlyList<UsoApp> ordenada, int maximo = MaximoLinhas)
    {
        if (ordenada.Count <= maximo)
        {
            return [.. ordenada];
        }

        var r = new List<UsoApp>(maximo);
        r.AddRange(ordenada.Take(maximo - 1));
        IEnumerable<UsoApp> resto = ordenada.Skip(maximo - 1);
        int n = ordenada.Count - (maximo - 1);
        r.Add(new UsoApp("", $"Outros ({n} apps)", null,
            (ulong)resto.Sum(u => (decimal)u.Recebidos), (ulong)resto.Sum(u => (decimal)u.Enviados)));
        return r;
    }
}

/// <summary>
/// v1.3.0 (Onda 8): lê o consumo por app do Windows (WinRT, Windows.Networking.Connectivity).
/// ISOLADO aqui de propósito: só esta classe toca a projeção WinRT (~25 MB), que o .NET carrega
/// na primeira vez que ela é usada — o widget, que fica aberto o dia todo, não paga essa memória
/// a menos que o usuário abra a janela do consumo por app.
/// </summary>
public sealed class LeitorConsumoPorApp
{
    // id do Windows → (caminho, descrição do arquivo); ler a versão do .exe a cada 30 s seria desperdício
    private static readonly NetworkUsageStates Estados = new() { Roaming = TriStates.DoNotCare, Shared = TriStates.DoNotCare };

    private readonly Dictionary<string, (string? Caminho, string? Descricao)> _nomes = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string>? _mapaDispositivos;

    // Perfis com tráfego no mês (descobertos em segundo plano, 1 vez por janela aberta)
    private volatile List<ConnectionProfile>? _perfisAtivos;
    private Task? _descoberta;

    /// <summary>
    /// Disparado (em outra thread) quando a descoberta dos perfis termina: a leitura seguinte já
    /// soma TODAS as redes usadas no mês — a janela se refaz na hora.
    /// </summary>
    public event Action? PerfisDescobertos;

    /// <summary>
    /// Lê o consumo do período. Medido em 2026-11-01: o Windows guarda um perfil por rede Wi-Fi já
    /// usada (30 nesta máquina) e atende as consultas EM FILA (em paralelo não ganhou nada); cada
    /// perfil vazio custa 90–180 ms → ~4–5 s por leitura. Por isso: a 1ª leitura usa só a rede
    /// atual (~0,1 s) e dispara a descoberta, que acha os poucos perfis com tráfego no mês; dali
    /// em diante só eles (+ a rede atual) são consultados.
    /// </summary>
    public async Task<List<UsoApp>> LerAsync(PeriodoConsumoApp periodo, CancellationToken ct)
    {
        DateTimeOffset agora = DateTimeOffset.Now;
        (DateTimeOffset inicio, DateTimeOffset fim) = ConsumoPorApp.Intervalo(periodo, agora);

        List<ConnectionProfile> perfis = PerfisParaLer(_perfisAtivos);
        if (_perfisAtivos is null)
        {
            _descoberta ??= Task.Run(() => DescobrirAsync(agora));
        }

        IEnumerable<UsoBruto>[] porPerfil = await Task.WhenAll(
            perfis.Select(p => LerPerfilAsync(p, inicio, fim, ct))).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        return ConsumoPorApp.Agrupar(porPerfil.SelectMany(x => x), Resolver);
    }

    /// <summary>Os perfis ativos (ou nenhum, antes da descoberta) + o da rede atual, sem repetir.</summary>
    private static List<ConnectionProfile> PerfisParaLer(List<ConnectionProfile>? ativos)
    {
        var lista = new List<ConnectionProfile>(ativos ?? []);
        ConnectionProfile? atual = NetworkInformation.GetInternetConnectionProfile();
        if (atual is not null && !lista.Any(p => p.ProfileName == atual.ProfileName) && EhFisico(atual, Placas()))
        {
            lista.Add(atual);
        }

        return lista;
    }

    /// <summary>
    /// Passa por todos os perfis físicos (lento, em segundo plano) e guarda os que tiveram
    /// tráfego desde o início do mês — com 1 h de folga para trás, para "5 min"/"1 h" logo depois
    /// da meia-noite do dia 1º não perderem a rede usada no fim do mês anterior.
    /// </summary>
    private async Task DescobrirAsync(DateTimeOffset agora)
    {
        (DateTimeOffset inicioMes, _) = ConsumoPorApp.Intervalo(PeriodoConsumoApp.EsteMes, agora);
        DateTimeOffset inicio = inicioMes < agora.AddHours(-1) ? inicioMes : agora.AddHours(-1);
        var ativos = new List<ConnectionProfile>();
        foreach (ConnectionProfile p in PerfisFisicos())
        {
            if ((await LerPerfilAsync(p, inicio, agora, CancellationToken.None).ConfigureAwait(false)).Any())
            {
                ativos.Add(p);
            }
        }

        _perfisAtivos = ativos;
        PerfisDescobertos?.Invoke();
    }

    private static async Task<IEnumerable<UsoBruto>> LerPerfilAsync(
        ConnectionProfile perfil, DateTimeOffset inicio, DateTimeOffset fim, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<AttributedNetworkUsage> usos =
                await perfil.GetAttributedNetworkUsageAsync(inicio, fim, Estados).AsTask(ct).ConfigureAwait(false);
            return [.. usos.Select(u => new UsoBruto(u.AttributionId ?? "", u.AttributionName, u.BytesReceived, u.BytesSent))];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Captura ampla justificada: um perfil antigo/corrompido não pode derrubar a lista
            // inteira — os outros seguem.
            Debug.WriteLine($"Consumo por app: perfil '{perfil.ProfileName}' ignorado: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Perfis de placas FÍSICAS (Wi-Fi/cabo), pelo mesmo filtro do medidor: perfis de VPN
    /// (Tailscale, Radmin) poderiam contar o mesmo tráfego duas vezes. (Medido: hoje eles devolvem
    /// 0 — o Windows atribui ao perfil físico —, mas o filtro garante se isso mudar.)
    /// </summary>
    private static IEnumerable<ConnectionProfile> PerfisFisicos()
    {
        Dictionary<Guid, NetworkInterface> placas = Placas();
        return NetworkInformation.GetConnectionProfiles().Where(p => EhFisico(p, placas)).ToList();
    }

    private static Dictionary<Guid, NetworkInterface> Placas()
    {
        Dictionary<Guid, NetworkInterface> placas = [];
        foreach (NetworkInterface n in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (Guid.TryParse(n.Id, out Guid g))
            {
                placas[g] = n;
            }
        }

        return placas;
    }

    private static bool EhFisico(ConnectionProfile p, Dictionary<Guid, NetworkInterface> placas)
    {
        NetworkAdapter? adaptador = p.NetworkAdapter;
        if (adaptador is null)
        {
            return false; // sem adaptador não dá para saber se é físico
        }

        placas.TryGetValue(adaptador.NetworkAdapterId, out NetworkInterface? placa);
        var amostra = new AmostraInterfaceRede(
            InterfaceLuid: 0,
            Alias: placa?.Name ?? p.ProfileName,
            Descricao: placa?.Description ?? "",
            Tipo: adaptador.IanaInterfaceType,
            EhHardware: true,          // o histórico vale mesmo com a placa desconectada agora
            EstaOperacional: true,
            MidiaConectada: true,
            BytesRecebidos: 0,
            BytesEnviados: 0);
        return InterfaceFilter.DeveContabilizar(in amostra);
    }

    private (string? Caminho, string? Descricao) Resolver(string id)
    {
        if (_nomes.TryGetValue(id, out var conhecido))
        {
            return conhecido;
        }

        string? caminho = ConsumoPorApp.CaminhoDeDispositivo(id, _mapaDispositivos ??= MapaDispositivos());
        string? descricao = null;
        if (caminho is not null && File.Exists(caminho))
        {
            try
            {
                descricao = FileVersionInfo.GetVersionInfo(caminho).FileDescription;
            }
            catch (IOException)
            {
                // arquivo em uso/sumiu entre o Exists e a leitura: fica o nome do arquivo
            }
        }

        return _nomes[id] = (caminho, descricao);
    }

    /// <summary>"\device\harddiskvolume4" → "C:" para cada unidade presente (QueryDosDevice).</summary>
    private static Dictionary<string, string> MapaDispositivos()
    {
        var mapa = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var buffer = new char[1024];
        foreach (DriveInfo d in DriveInfo.GetDrives())
        {
            string unidade = d.Name.TrimEnd('\\'); // "C:"
            uint n = NativeMethods.QueryDosDevice(unidade, buffer, (uint)buffer.Length);
            if (n > 0)
            {
                // O resultado é uma lista separada por \0; o 1º item é o dispositivo atual
                string dispositivo = new string(buffer, 0, (int)n).Split('\0')[0];
                if (dispositivo.Length > 0)
                {
                    mapa[dispositivo.ToLowerInvariant()] = unidade;
                }
            }
        }

        return mapa;
    }
}
