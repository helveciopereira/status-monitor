using System.Runtime.InteropServices;
using static VelocimetroInternet.Sistema.NativeMethods;

namespace VelocimetroInternet.Sistema;

/// <summary>Sinal e link do Wi-Fi conectado, como o Windows informa (o mesmo do `netsh wlan show interfaces`).</summary>
/// <param name="RecepcaoMbps">Taxa de recepção do link (sentido do download).</param>
/// <param name="TransmissaoMbps">Taxa de transmissão do link (sentido do upload).</param>
/// <param name="SinalPct">Qualidade do sinal 0–100.</param>
/// <param name="RssiDbm">Potência em dBm, quando o Windows lista o ponto de acesso (null se não).</param>
/// <param name="Banda">"2,4 GHz", "5 GHz", "6 GHz" ou null se a frequência não veio.</param>
public sealed record LeituraWiFi(double RecepcaoMbps, double TransmissaoMbps, int SinalPct, int? RssiDbm, string? Banda);

/// <summary>
/// v1.5.0 (Onda 10): lê o Wi-Fi pela WLAN API nativa do Windows (wlanapi.dll) — sem administrador
/// e sem tráfego de rede. Chamado só UMA vez por teste de velocidade (nunca no loop de 1 s).
///
/// Estruturas lidas por DESLOCAMENTO (Marshal.ReadInt32) em vez de structs com arrays fixos:
/// menos chance de erro de alinhamento e nada a alocar além do que a própria API devolve.
/// Deslocamentos conferidos contra wlanapi.h (x64 e x86 iguais — só campos de 4 bytes e GUIDs):
/// - WLAN_CONNECTION_ATTRIBUTES: isState(0) · modo(4) · perfil WCHAR[256](8) · associação(520)
///   associação: SSID(+0, 36 B) · tipo BSS(+36) · BSSID(+40, 6 B + 2 de alinhamento) · PHY(+48) ·
///   índice PHY(+52) · qualidade do sinal(+56) · recepção kbps(+60) · transmissão kbps(+64)
/// - WLAN_BSS_LIST: tamanho(0) · quantidade(4) · entradas(8), cada WLAN_BSS_ENTRY com 360 B:
///   BSSID(+40) · RSSI(+56) · frequência central kHz(+92)
///
/// Windows 11 24H2+: os apps podem precisar da permissão de LOCALIZAÇÃO para ler o Wi-Fi. Se o
/// Windows negar (ERROR_ACCESS_DENIED), devolve null e o teste guarda só a velocidade da placa.
/// </summary>
public static class InfoWiFi
{
    private const int OpcodeConexaoAtual = 7; // wlan_intf_opcode_current_connection
    private const int EstadoConectado = 1;    // wlan_interface_state_connected
    private const int BssQualquer = 3;        // dot11_BSS_type_any

    private const int DeslocAssociacao = 520;
    private const int TamanhoEntradaBss = 360;

    /// <summary>
    /// Lê o Wi-Fi conectado. <paramref name="guidInterface"/> = a placa do teste; null = a primeira
    /// placa Wi-Fi conectada. Null quando não há Wi-Fi conectado ou o Windows não deixa ler.
    /// </summary>
    public static LeituraWiFi? Ler(Guid? guidInterface)
    {
        nint cliente = nint.Zero;
        nint lista = nint.Zero;
        try
        {
            // Versão 2 do cliente = Windows Vista em diante
            if (WlanOpenHandle(2, nint.Zero, out _, out cliente) != 0)
            {
                return null;
            }

            if (EscolherInterface(cliente, guidInterface, out lista) is not Guid guid)
            {
                return null;
            }

            return LerConexao(cliente, guid);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null; // Windows sem o serviço WLAN (ex.: Server sem o recurso Wireless LAN)
        }
        finally
        {
            if (lista != nint.Zero)
            {
                WlanFreeMemory(lista);
            }

            if (cliente != nint.Zero)
            {
                WlanCloseHandle(cliente, nint.Zero);
            }
        }
    }

    /// <summary>Acha a placa pedida (ou a 1ª conectada) na lista de placas Wi-Fi do Windows.</summary>
    private static Guid? EscolherInterface(nint cliente, Guid? pedida, out nint lista)
    {
        if (WlanEnumInterfaces(cliente, nint.Zero, out lista) != 0 || lista == nint.Zero)
        {
            return null;
        }

        // WLAN_INTERFACE_INFO_LIST: quantidade(0) · índice(4) · itens(8), cada item com 532 B:
        // GUID(0, 16 B) · descrição WCHAR[256](16) · estado(528)
        int n = Marshal.ReadInt32(lista, 0);
        Guid? primeiraConectada = null;
        for (int i = 0; i < n; i++)
        {
            nint item = lista + 8 + i * 532;
            var guid = Marshal.PtrToStructure<Guid>(item);
            bool conectada = Marshal.ReadInt32(item, 528) == EstadoConectado;
            if (!conectada)
            {
                continue;
            }

            if (pedida is null || guid == pedida)
            {
                return guid;
            }

            primeiraConectada ??= guid;
        }

        // A placa pedida não é Wi-Fi conectado (ex.: o teste foi por cabo): só cai na 1ª se nada foi pedido
        return pedida is null ? primeiraConectada : null;
    }

    private static LeituraWiFi? LerConexao(nint cliente, Guid guid)
    {
        nint dados = nint.Zero;
        try
        {
            if (WlanQueryInterface(cliente, ref guid, OpcodeConexaoAtual, nint.Zero, out _, out dados, out _) != 0
                || dados == nint.Zero
                || Marshal.ReadInt32(dados, 0) != EstadoConectado)
            {
                return null;
            }

            nint assoc = dados + DeslocAssociacao;
            byte[] bssid = new byte[6];
            Marshal.Copy(assoc + 40, bssid, 0, 6);
            int sinal = Math.Clamp(Marshal.ReadInt32(assoc, 56), 0, 100);
            double rx = (uint)Marshal.ReadInt32(assoc, 60) / 1000.0; // kbps → Mbps
            double tx = (uint)Marshal.ReadInt32(assoc, 64) / 1000.0;

            (int? rssi, string? banda) = LerPontoDeAcesso(cliente, guid, bssid);
            return new LeituraWiFi(rx, tx, sinal, rssi, banda);
        }
        finally
        {
            if (dados != nint.Zero)
            {
                WlanFreeMemory(dados);
            }
        }
    }

    /// <summary>RSSI e banda do ponto de acesso conectado (pela lista de redes que o Windows já tem; não faz varredura).</summary>
    private static (int? Rssi, string? Banda) LerPontoDeAcesso(nint cliente, Guid guid, byte[] bssid)
    {
        nint lista = nint.Zero;
        try
        {
            if (WlanGetNetworkBssList(cliente, ref guid, nint.Zero, BssQualquer, false, nint.Zero, out lista) != 0
                || lista == nint.Zero)
            {
                return (null, null);
            }

            int n = Marshal.ReadInt32(lista, 4);
            byte[] atual = new byte[6];
            for (int i = 0; i < n; i++)
            {
                nint e = lista + 8 + i * TamanhoEntradaBss;
                Marshal.Copy(e + 40, atual, 0, 6);
                if (!atual.AsSpan().SequenceEqual(bssid))
                {
                    continue;
                }

                int rssi = Marshal.ReadInt32(e, 56);
                uint khz = (uint)Marshal.ReadInt32(e, 92);
                return (rssi is < 0 and > -120 ? rssi : null, Banda(khz));
            }

            return (null, null);
        }
        finally
        {
            if (lista != nint.Zero)
            {
                WlanFreeMemory(lista);
            }
        }
    }

    /// <summary>Frequência central (kHz) → banda. 2,4 GHz: 2,4–2,5; 5 GHz: 5,15–5,9; 6 GHz: 5,925–7,125.</summary>
    public static string? Banda(uint khz) => khz switch
    {
        >= 2_400_000 and < 2_500_000 => "2,4 GHz",
        >= 5_150_000 and < 5_925_000 => "5 GHz",
        >= 5_925_000 and <= 7_125_000 => "6 GHz",
        _ => null,
    };
}
