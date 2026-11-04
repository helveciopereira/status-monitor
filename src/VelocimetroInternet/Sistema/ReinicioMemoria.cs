using System.Diagnostics;

namespace VelocimetroInternet.Sistema;

/// <summary>
/// v1.9.1: reinício automático por memória — a parte PURA (testável) e os argumentos de linha de
/// comando que a cópia nova recebe.
///
/// Por quê: o que cresce na memória do app não é lixo gerenciado (o GC já recolhe; coleta forçada
/// devolvia ~5 MB de 160) e sim heaps nativos do WPF e do driver de vídeo, que o app não consegue
/// liberar. Reabrir o processo é a única "liberação" que alcança tudo. A regra garante que isso só
/// acontece (1) com a opção ligada, (2) depois de <see cref="TempoMinimoAberto"/> — um app que acabou
/// de abrir ainda está aquecendo (75 → 125 MB nos primeiros 10 min é normal) — e (3) acima do teto
/// escolhido no estúdio. O MOMENTO fica a cargo da <see cref="RegraMomentoTranquilo"/> (App.xaml.cs).
///
/// Sequência do reinício: a cópia atual abre a nova com <see cref="ArgumentoAguardarPid"/> + seu PID
/// e encerra pelo caminho normal (OnExit salva consumo/queda, tira a bandeja e solta o mutex). A nova
/// espera esse PID sair ANTES de disputar o mutex de instância única — senão ela se veria como a
/// "2ª cópia", avisaria a antiga e sairia.
/// </summary>
public static class ReinicioMemoria
{
    /// <summary>Argumento seguido do PID da cópia antiga, que a nova espera encerrar.</summary>
    public const string ArgumentoAguardarPid = "--aguardar-pid";

    /// <summary>Marca a cópia como reaberta pelo reinício de memória (só para o log).</summary>
    public const string ArgumentoReiniciado = "--reiniciado-memoria";

    /// <summary>Tempo mínimo aberto antes de considerar o teto (o aquecimento inicial não é crescimento).</summary>
    public static readonly TimeSpan TempoMinimoAberto = TimeSpan.FromHours(1);

    /// <summary>
    /// Decide se é hora de PEDIR o reinício (o momento tranquilo é verificado depois, na UI).
    /// </summary>
    /// <param name="ligado">Opção do estúdio.</param>
    /// <param name="tetoMb">Teto de memória privada em MB (já normalizado pela configuração).</param>
    /// <param name="privadaBytes">Memória privada atual do processo.</param>
    /// <param name="aberto">Há quanto tempo o processo está aberto.</param>
    public static bool DevePedir(bool ligado, int tetoMb, long privadaBytes, TimeSpan aberto) =>
        ligado && aberto >= TempoMinimoAberto && privadaBytes > tetoMb * 1024L * 1024L;

    /// <summary>PID informado após <see cref="ArgumentoAguardarPid"/>, ou null se ausente/ilegível.</summary>
    public static int? PidAguardar(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(ArgumentoAguardarPid, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], out int pid) && pid > 0)
            {
                return pid;
            }
        }

        return null;
    }

    /// <summary>true quando esta cópia foi aberta pelo próprio app para substituir a anterior.</summary>
    public static bool FoiReiniciado(string[] args) =>
        args.Any(a => a.Equals(ArgumentoReiniciado, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Espera o processo <paramref name="pid"/> encerrar, até <paramref name="limite"/>. Processo que já
    /// não existe conta como encerrado (true). false = ainda vivo ao fim do prazo (a nova cópia segue
    /// assim mesmo; se a antiga ainda segurar o mutex, a nova se comporta como 2ª instância).
    /// </summary>
    public static bool AguardarSaida(int pid, TimeSpan limite)
    {
        try
        {
            using var antigo = Process.GetProcessById(pid);
            return antigo.WaitForExit((int)limite.TotalMilliseconds);
        }
        catch (ArgumentException)
        {
            return true; // já saiu
        }
        catch (InvalidOperationException)
        {
            return true; // saiu entre o GetProcessById e o WaitForExit
        }
    }
}
