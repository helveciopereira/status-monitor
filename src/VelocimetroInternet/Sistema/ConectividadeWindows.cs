using System.Runtime.InteropServices;

namespace VelocimetroInternet.Sistema;

/// <summary>
/// v1.4.0 (Onda 9): "o Windows acha que há internet?" — lido do Network List Manager (COM), a
/// mesma fonte do ícone de rede com "Sem internet". O Windows já testa a internet sozinho
/// (NCSI); o app só LÊ o resultado: zero tráfego extra, ~0,5 ms por consulta (medido nesta
/// máquina em 2026-11-01).
///
/// Por que COM e não a API WinRT (NetworkInformation): o WinRT carrega a projeção do SDK do
/// Windows (~25 MB mapeados) no processo do widget, que fica aberto 24 h; o COM do NLM é nativo
/// e não carrega nada.
///
/// Na dúvida (COM indisponível, erro), responde null — quem chama trata como "com internet":
/// melhor deixar de registrar uma queda do que acusar a operadora de uma queda que não houve.
/// </summary>
public sealed class ConectividadeWindows
{
    /// <summary>NLM_CONNECTIVITY: bits de "tem internet" em IPv4 e IPv6.</summary>
    private const int Ipv4Internet = 0x40;
    private const int Ipv6Internet = 0x400;

    private static readonly Guid ClsidNetworkListManager = new("DCB00C01-570F-4A9B-8D69-199FDBA5723B");

    private INetworkListManager? _nlm;
    private bool _falhou;

    /// <summary>true = há internet; false = conectado sem internet; null = não deu para saber.</summary>
    public bool? TemInternet()
    {
        if (_falhou)
        {
            return null;
        }

        try
        {
            _nlm ??= (INetworkListManager)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidNetworkListManager, throwOnError: true)!)!;
            int c = _nlm.GetConnectivity();
            return (c & (Ipv4Internet | Ipv6Internet)) != 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or TypeLoadException or InvalidComObjectException)
        {
            // Serviço de lista de redes parado/ausente: desiste nesta sessão (não tenta a cada 5 s)
            _falhou = true;
            _nlm = null;
            return null;
        }
    }

    /// <summary>
    /// INetworkListManager (netlistmgr.h). Interface DUAL: os 4 métodos do IDispatch vêm antes no
    /// vtable (InterfaceIsDual cuida disso). Os 6 primeiros métodos só ocupam a posição — nunca são
    /// chamados, então a assinatura deles não importa; só a ORDEM importa.
    /// </summary>
    [ComImport]
    [Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface INetworkListManager
    {
        void GetNetworks();
        void GetNetwork();
        void GetNetworkConnections();
        void GetNetworkConnection();
        void get_IsConnectedToInternet();
        void get_IsConnected();

        /// <summary>Flags NLM_CONNECTIVITY da máquina inteira.</summary>
        int GetConnectivity();
    }
}
