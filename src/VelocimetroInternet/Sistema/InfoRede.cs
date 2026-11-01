using System.Net.NetworkInformation;
using System.Net.Sockets;

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
}
