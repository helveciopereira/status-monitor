using System.Runtime.InteropServices;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Medicao;

/// <summary>
/// Resultado consolidado de uma leitura instantânea das interfaces de rede ativas.
/// </summary>
public readonly record struct LeituraAcumuladaRede(
    ulong TotalBytesRecebidos,
    ulong TotalBytesEnviados,
    int InterfacesFisicasAtivas,
    string ResumoInterfacesAtivas,
    // v0.2.0 (painel rápido): maior velocidade de link entre as interfaces contadas (bits/s;
    // 0 = desconhecida) e o apelido curto da interface principal ("Wi-Fi", "Ethernet")
    ulong VelocidadeLinkBitsPorSegundo = 0,
    string NomeInterfacePrincipal = "");

/// <summary>
/// Lê os contadores acumulados de 64 bits (InOctets / OutOctets) diretamente do kernel/NDIS
/// via <see cref="NativeMethods.GetIfTable2"/>.
/// Regra crítica (02-ARQUITETURA.md §3): libera sempre a tabela nativa via <see cref="NativeMethods.FreeMibTable"/>
/// em bloco finally para garantir 0 vazamento de memória não-gerenciada em execução 24/7.
/// </summary>
public sealed class NetworkSampler
{
    private readonly IReadOnlyCollection<string>? _sempreIncluir;
    private readonly IReadOnlyCollection<string>? _sempreExcluir;

    public NetworkSampler(
        IReadOnlyCollection<string>? sempreIncluir = null,
        IReadOnlyCollection<string>? sempreExcluir = null)
    {
        _sempreIncluir = sempreIncluir;
        _sempreExcluir = sempreExcluir;
    }

    /// <summary>
    /// Lê todas as interfaces da tabela MIB-II, filtra apenas as placas físicas reais (deduplicadas por LUID/MAC)
    /// e retorna a soma de bytes recebidos e enviados.
    /// </summary>
    public LeituraAcumuladaRede LerContadoresAtuais()
    {
        nint tablePtr = nint.Zero;
        try
        {
            uint status = NativeMethods.GetIfTable2(out tablePtr);
            if (status != 0 || tablePtr == nint.Zero)
            {
                return new LeituraAcumuladaRede(0, 0, 0, "Erro GetIfTable2: " + status);
            }

            // Em MIB_IF_TABLE2 (Windows x64):
            // Offset 0: ULONG NumEntries (4 bytes) + 4 bytes de padding de alinhamento para ULONG64 (Total = 8 bytes).
            // Offset 8: Início do array MIB_IF_ROW2 Table[ANY_SIZE].
            int numEntries = Marshal.ReadInt32(tablePtr);
            nint rowPtr = tablePtr + 8;
            int rowSize = Marshal.SizeOf<NativeMethods.MIB_IF_ROW2>();

            ulong somaRecebidos = 0;
            ulong somaEnviados = 0;
            int contadas = 0;
            List<string>? nomesAtivos = null;
            ulong maiorLink = 0;
            string principal = string.Empty;
            HashSet<ulong> luidsVistos = [];

            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<NativeMethods.MIB_IF_ROW2>(rowPtr);
                rowPtr += rowSize;

                // Evita somar duas vezes caso a mesma LUID apareça duplicada na tabela
                if (!luidsVistos.Add(row.InterfaceLuid))
                {
                    continue;
                }

                var amostra = new AmostraInterfaceRede(
                    InterfaceLuid: row.InterfaceLuid,
                    Alias: row.Alias ?? string.Empty,
                    Descricao: row.Description ?? string.Empty,
                    Tipo: row.Type,
                    EhHardware: row.IsHardwareInterface,
                    EstaOperacional: row.IsOperStatusUp,
                    MidiaConectada: row.IsMediaConnected,
                    BytesRecebidos: row.InOctets,
                    BytesEnviados: row.OutOctets);

                if (InterfaceFilter.DeveContabilizar(in amostra, _sempreIncluir, _sempreExcluir))
                {
                    somaRecebidos += amostra.BytesRecebidos;
                    somaEnviados += amostra.BytesEnviados;
                    contadas++;
                    nomesAtivos ??= new List<string>(2);
                    nomesAtivos.Add($"{amostra.Alias} ({amostra.Descricao})");

                    // A interface de maior link é tratada como a "principal" no painel. O valor
                    // 0xFFFFFFFFFFFFFFFF significa "desconhecido" na API e é descartado.
                    ulong link = Math.Max(row.ReceiveLinkSpeed, row.TransmitLinkSpeed);
                    if (link != ulong.MaxValue && link > maiorLink)
                    {
                        maiorLink = link;
                        principal = amostra.Alias;
                    }
                    else if (principal.Length == 0)
                    {
                        principal = amostra.Alias;
                    }
                }
            }

            string resumo = nomesAtivos is { Count: > 0 }
                ? string.Join(" + ", nomesAtivos)
                : "Nenhuma interface física conectada";

            return new LeituraAcumuladaRede(somaRecebidos, somaEnviados, contadas, resumo, maiorLink, principal);
        }
        finally
        {
            if (tablePtr != nint.Zero)
            {
                NativeMethods.FreeMibTable(tablePtr);
            }
        }
    }
}

