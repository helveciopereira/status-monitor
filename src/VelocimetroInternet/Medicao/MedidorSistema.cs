using System.Runtime.InteropServices;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Medicao;

/// <summary>Uso do computador num instante: CPU e RAM em porcentagem (0–100). null = leitura indisponível.</summary>
public readonly record struct UsoSistema(double? CpuPercentual, double? RamPercentual);

/// <summary>Tempos acumulados do processador desde o boot (100 ns, todos os núcleos somados).</summary>
public readonly record struct TemposCpu(long Ocioso, long Kernel, long Usuario);

/// <summary>
/// Mede o uso de CPU e de RAM do Windows inteiro (v0.5.0, pedido do dono: "consumo de CPU e RAM,
/// tudo em porcentagem").
///
/// Por que GetSystemTimes e não PerformanceCounter: o contador de desempenho carrega a infraestrutura
/// de PDH (dezenas de MB de RAM e ~1 s na 1ª leitura) — inaceitável num widget que mira ~50 MB.
/// GetSystemTimes é uma chamada só, sem alocação. A CPU é a fração do intervalo em que os núcleos
/// NÃO estiveram ociosos, entre duas leituras consecutivas (o loop lê 1×/s).
///
/// Diferença esperada do Gerenciador de Tarefas: no Windows 11 ele mostra "% Utilidade do
/// processador", que considera turbo/frequência e pode passar o tempo ocupado puro. Este número é o
/// "% Tempo do processador" clássico — pode ficar alguns pontos abaixo do Gerenciador em carga.
///
/// RAM = (total − disponível) ÷ total — a mesma conta do "Em uso" do Gerenciador.
/// </summary>
public sealed class MedidorSistema
{
    private TemposCpu? _anterior;

    /// <summary>Lê CPU (desde a leitura anterior) e RAM (agora). A 1ª chamada devolve CPU null: falta a base.</summary>
    public UsoSistema Ler()
    {
        double? cpu = null;
        if (NativeMethods.GetSystemTimes(out long ocioso, out long kernel, out long usuario))
        {
            var atual = new TemposCpu(ocioso, kernel, usuario);
            if (_anterior is { } ant)
            {
                cpu = CalcularPercentualCpu(ant, atual);
            }

            _anterior = atual;
        }

        return new UsoSistema(cpu, LerRamPercentual());
    }

    /// <summary>
    /// % de CPU entre duas leituras. O tempo de kernel do Windows JÁ INCLUI o ocioso, então
    /// total = Δkernel + Δuser e ocupado = total − Δidle. Intervalo vazio ou contador que voltou
    /// (não deveria, mas o custo de checar é zero) → null, para não exibir lixo.
    /// </summary>
    public static double? CalcularPercentualCpu(TemposCpu anterior, TemposCpu atual)
    {
        long dOcioso = atual.Ocioso - anterior.Ocioso;
        long dTotal = (atual.Kernel - anterior.Kernel) + (atual.Usuario - anterior.Usuario);
        if (dTotal <= 0 || dOcioso < 0)
        {
            return null;
        }

        double ocupado = (double)(dTotal - dOcioso) / dTotal * 100.0;
        return Math.Clamp(ocupado, 0.0, 100.0);
    }

    /// <summary>% de RAM física em uso. Total zero (struct mal preenchida) → null.</summary>
    public static double? CalcularPercentualRam(ulong totalBytes, ulong disponivelBytes)
    {
        if (totalBytes == 0 || disponivelBytes > totalBytes)
        {
            return null;
        }

        return (double)(totalBytes - disponivelBytes) / totalBytes * 100.0;
    }

    private static double? LerRamPercentual()
    {
        var status = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        return NativeMethods.GlobalMemoryStatusEx(ref status)
            ? CalcularPercentualRam(status.ullTotalPhys, status.ullAvailPhys)
            : null;
    }

    /// <summary>Texto curto para o widget: "7%", "100%"; sem leitura → "—". Sem casas decimais: cabe e não "pisca".</summary>
    public static string Formatar(double? percentual) =>
        percentual is { } p ? $"{Math.Round(p, MidpointRounding.AwayFromZero):0}%" : "—";

    /// <summary>v1.7.1: a partir de quanto o número de CPU/RAM fica vermelho na barra (pedido do dono).</summary>
    public const int LimiteAlertaPercentual = 80;

    /// <summary>
    /// v1.7.1: o valor está em alerta (≥ 80 %)? Compara o valor JÁ ARREDONDADO, o mesmo que aparece
    /// na tela: 79,6 % é mostrado como "80%" e por isso também fica vermelho — nunca um "80%" branco.
    /// Sem leitura (null) não é alerta.
    /// </summary>
    public static bool EmAlerta(double? percentual) =>
        percentual is { } p && Math.Round(p, MidpointRounding.AwayFromZero) >= LimiteAlertaPercentual;
}
