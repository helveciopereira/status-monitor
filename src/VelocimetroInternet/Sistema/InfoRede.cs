using System.Net.NetworkInformation;
using System.Net.Sockets;
using VelocimetroInternet.Medicao;

namespace VelocimetroInternet.Sistema;

/// <summary>
/// v1.0.0 (Onda 5): dados de rede que só a DICA do widget usa (IP local). Lido na hora em que a
/// dica abre — nunca no loop de 1 s, porque GetAllNetworkInterfaces aloca bastante.
/// </summary>
public static class InfoRede
{
    /// <summary>
    /// IPv4 da interface cujo apelido é <paramref name="alias"/> ("Wi-Fi", "Ethernet"), ou null.
    /// Ignora endereços de link local (169.254.x.x): eles indicam "sem DHCP", não um IP útil.
    /// </summary>
    public static string? IpLocal(string? alias)
    {
        if (string.IsNullOrEmpty(alias))
        {
            return null;
        }

        try
        {
            NetworkInterface? nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, alias, StringComparison.OrdinalIgnoreCase));
            return nic?.GetIPProperties().UnicastAddresses
                .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(u => u.Address.ToString())
                .FirstOrDefault(ip => !ip.StartsWith("169.254.", StringComparison.Ordinal));
        }
        catch (NetworkInformationException)
        {
            return null; // pilha de rede reiniciando: a dica sai sem o IP
        }
    }

    /// <summary>
    /// v1.4.0: "Cabo", "Wi-Fi" ou o próprio apelido da placa — vai em cada teste guardado, porque
    /// a operadora (e a Anatel) dão mais peso a medição por cabo.
    /// </summary>
    public static string TipoConexao(string? alias)
    {
        if (string.IsNullOrEmpty(alias))
        {
            return "—";
        }

        try
        {
            NetworkInterface? nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, alias, StringComparison.OrdinalIgnoreCase));
            return nic?.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Wireless80211 => "Wi-Fi",
                NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
                    or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit => "Cabo",
                _ => alias,
            };
        }
        catch (NetworkInformationException)
        {
            return alias;
        }
    }

    /// <summary>
    /// v1.5.0 (Onda 10): como o PC está ligado ao roteador AGORA — chamado uma vez por teste, logo
    /// depois da fase de download (com o Wi-Fi sob carga a taxa do link já subiu; parado, o Windows
    /// economiza energia e mostra uma taxa menor que a real).
    /// </summary>
    /// <param name="alias">Placa principal da medição ("Wi-Fi", "Ethernet").</param>
    /// <param name="linkBps">Maior velocidade de link da placa, lida pelo medidor (0/desconhecido = sem link).</param>
    public static ConexaoNoTeste ConexaoAtual(string? alias, ulong linkBps)
    {
        string tipo = TipoConexao(alias);
        double? link = linkBps is > 0 and < ulong.MaxValue ? linkBps / 1e6 : null;
        if (tipo != "Wi-Fi")
        {
            // Cabo: a porta tem a mesma velocidade nos dois sentidos (100 Mbps, 1 Gbps, 2,5 Gbps…)
            return new ConexaoNoTeste(tipo, link, link);
        }

        // Wi-Fi: recepção e transmissão diferem (aqui 432 × 216 Mbps), mais sinal e banda.
        // Se o Windows não deixar ler (permissão de localização), fica a velocidade da placa.
        LeituraWiFi? w = InfoWiFi.Ler(GuidDaPlaca(alias));
        return w is null
            ? new ConexaoNoTeste(tipo, link)
            : new ConexaoNoTeste(tipo, w.RecepcaoMbps > 0 ? w.RecepcaoMbps : link, w.TransmissaoMbps > 0 ? w.TransmissaoMbps : null,
                w.SinalPct, w.RssiDbm, w.Banda);
    }

    /// <summary>GUID da placa pelo apelido (é o mesmo GUID que a WLAN API usa), ou null.</summary>
    private static Guid? GuidDaPlaca(string? alias)
    {
        try
        {
            NetworkInterface? nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => string.Equals(n.Name, alias, StringComparison.OrdinalIgnoreCase));
            return nic is not null && Guid.TryParse(nic.Id, out Guid g) ? g : null;
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }
}
