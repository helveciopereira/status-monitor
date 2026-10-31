namespace VelocimetroInternet.Medicao;

/// <summary>
/// Representa os metadados mínimos de uma interface de rede lida da tabela MIB-II (GetIfTable2)
/// para decisão pura de filtragem (testável sem depender do hardware da máquina).
/// </summary>
public readonly record struct AmostraInterfaceRede(
    ulong InterfaceLuid,
    string Alias,
    string Descricao,
    uint Tipo,
    bool EhHardware,
    bool EstaOperacional,
    bool MidiaConectada,
    ulong BytesRecebidos,
    ulong BytesEnviados);

/// <summary>
/// Filtra interfaces de rede para somar apenas tráfego real de placas físicas (Ethernet e Wi-Fi),
/// evitando a dupla contagem causada por túneis de VPN (Tailscale, Radmin VPN, WireGuard) e
/// switches virtuais (WSL, Hyper-V vEthernet, VirtualBox, VMware).
/// Referência: 01-ANALISE.md §2.3 e 02-ARQUITETURA.md §3.
/// </summary>
public static class InterfaceFilter
{
    public const uint IF_TYPE_ETHERNET_CSMACD = 6;
    public const uint IF_TYPE_IEEE80211 = 71;

    /// <summary>
    /// Termos conhecidos de adaptadores virtuais, VPNs e filtros intermediários do NDIS que
    /// às vezes se reportam como Ethernet (Type = 6), mas não são placas físicas reais.
    /// Inclui Tailscale e Radmin VPN (verificados ativos nesta máquina em 01-ANALISE.md §2.3).
    /// </summary>
    private static readonly string[] PalavrasChaveVirtuaisOuFiltros =
    [
        "tailscale",
        "radmin",
        "wireguard",
        "wintun",
        "tap-windows",
        "openvpn",
        "zerotier",
        "hamachi",
        "vethernet",
        "hyper-v",
        "wsl",
        "virtualbox",
        "vmware",
        "npcap",
        "loopback",
        "teredo",
        "isatap",
        "bluetooth",
        "qos packet scheduler",
        "lightweight filter",
        "wfp ",
        "ndis capture",
        "virtual adapter",
        "miniport"
    ];

    /// <summary>
    /// Decide se uma interface de rede deve ter seus bytes contabilizados no velocímetro.
    /// </summary>
    public static bool DeveContabilizar(
        in AmostraInterfaceRede iface,
        IReadOnlyCollection<string>? sempreIncluir = null,
        IReadOnlyCollection<string>? sempreExcluir = null)
    {
        // 1. Lista manual de exclusão do usuário tem prioridade máxima
        if (sempreExcluir is { Count: > 0 } && CorrespondeListaUsuario(in iface, sempreExcluir))
        {
            return false;
        }

        // 2. Lista manual de inclusão do usuário permite forçar uma interface específica, desde que ativa
        if (sempreIncluir is { Count: > 0 } && CorrespondeListaUsuario(in iface, sempreIncluir))
        {
            return iface.EstaOperacional && iface.MidiaConectada;
        }

        // 3. Precisa estar operacional (IfOperStatusUp = 1) e com mídia conectada (MediaConnectStateConnected = 1)
        if (!iface.EstaOperacional || !iface.MidiaConectada)
        {
            return false;
        }

        // 4. Precisa ser marcada pelo NDIS como interface de hardware físico real (bit 0 de InterfaceAndOperStatusFlags)
        // Nota: no Windows, filtros leves (WFP/QoS) herdam os metadados da placa pai, por isso checamos também a descrição.
        if (!iface.EhHardware)
        {
            return false;
        }

        // 5. Apenas Ethernet (6) ou Wi-Fi IEEE 802.11 (71)
        if (iface.Tipo != IF_TYPE_ETHERNET_CSMACD && iface.Tipo != IF_TYPE_IEEE80211)
        {
            return false;
        }

        // 6. Bloqueia túneis VPN (Tailscale, Radmin VPN, etc.), adaptadores virtuais e filtros NDIS duplicados
        if (ContemTermoVirtualOuFiltro(iface.Alias) || ContemTermoVirtualOuFiltro(iface.Descricao))
        {
            return false;
        }

        return true;
    }

    private static bool CorrespondeListaUsuario(in AmostraInterfaceRede iface, IReadOnlyCollection<string> lista)
    {
        foreach (string item in lista)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            if (iface.Alias.Contains(item, StringComparison.OrdinalIgnoreCase) ||
                iface.Descricao.Contains(item, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContemTermoVirtualOuFiltro(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return false;
        }

        foreach (string termo in PalavrasChaveVirtuaisOuFiltros)
        {
            if (texto.Contains(termo, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
