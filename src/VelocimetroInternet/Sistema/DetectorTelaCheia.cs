using System.Text;

namespace VelocimetroInternet.Sistema;

/// <summary>
/// Decide se há um aplicativo em TELA CHEIA no monitor da barra (jogo, vídeo, apresentação),
/// caso em que o widget deve sumir para não ficar por cima do conteúdo.
///
/// Por que não só <c>SHQueryUserNotificationState</c> (usado na v0.1.1)? Ele é atualizado pelo
/// shell com atraso e só era consultado no vigia de 1,5 s → o widget "demorava a sumir"
/// (relato do dono em 2026-10-31). Agora a regra principal é GEOMÉTRICA e instantânea:
/// a janela em primeiro plano cobre o monitor inteiro? O SHQuery fica só como reforço para
/// D3D exclusivo e modo apresentação (que a geometria pode não pegar).
/// </summary>
public static class DetectorTelaCheia
{
    /// <summary>
    /// Classes de janela que cobrem o monitor mas NÃO são tela cheia "de verdade":
    /// a área de trabalho (Progman/WorkerW), a própria barra e o painel do Iniciar/Pesquisa.
    /// Sem isso, clicar na área de trabalho (Win+D) esconderia o widget.
    /// </summary>
    private static readonly string[] ClassesIgnoradas =
    [
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow"
    ];

    /// <summary>
    /// Regra pura (testável sem tela): a janela cobre todo o retângulo do monitor e não é
    /// uma das classes do próprio shell.
    /// </summary>
    public static bool CobreMonitorInteiro(
        NativeMethods.RECT janela,
        NativeMethods.RECT monitor,
        string classeJanela)
    {
        foreach (string classe in ClassesIgnoradas)
        {
            if (string.Equals(classe, classeJanela, StringComparison.Ordinal))
            {
                return false;
            }
        }

        // ">=" e "<=": jogos e o modo F11 dos navegadores às vezes passam 1 px além da borda
        return janela.Left <= monitor.Left
            && janela.Top <= monitor.Top
            && janela.Right >= monitor.Right
            && janela.Bottom >= monitor.Bottom;
    }

    /// <summary>
    /// Consulta o estado real agora. <paramref name="pidProprio"/> evita que uma janela nossa
    /// (ex.: o painel rápido) seja tomada como tela cheia.
    /// </summary>
    public static bool HaTelaCheiaAgora(uint pidProprio)
    {
        nint fg = NativeMethods.GetForegroundWindow();
        if (fg != nint.Zero)
        {
            NativeMethods.GetWindowThreadProcessId(fg, out uint pid);
            if (pid != pidProprio && NativeMethods.GetWindowRect(fg, out NativeMethods.RECT rJanela))
            {
                nint monitor = NativeMethods.MonitorFromWindow(fg, NativeMethods.MONITOR_DEFAULTTONEAREST);
                var info = new NativeMethods.MONITORINFO
                {
                    cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>()
                };

                if (monitor != nint.Zero && NativeMethods.GetMonitorInfoW(monitor, ref info))
                {
                    var classe = new StringBuilder(128);
                    NativeMethods.GetClassNameW(fg, classe, classe.Capacity);
                    if (CobreMonitorInteiro(rJanela, info.rcMonitor, classe.ToString()))
                    {
                        return true;
                    }
                }
            }
        }

        // Reforço: 3 = QUNS_RUNNING_D3D_FULL_SCREEN, 4 = QUNS_PRESENTATION_MODE.
        // (2 = QUNS_BUSY foi retirado: ele também liga com janelas "ocupadas" sem tela cheia.)
        return NativeMethods.SHQueryUserNotificationState(out int estado) == 0 && estado is 3 or 4;
    }
}
