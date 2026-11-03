using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// v1.8.0: avisa quando a lista de botões da barra de tarefas PODE ter mudado — por evento, sem
/// relógio. Substitui a busca UIA a cada 1,5 s (57 600 buscas/dia, ~35 ms cada = ~2 % de um núcleo
/// em repouso, medido no harness em 2026-11-03) por uma busca só quando acontece algo.
///
/// O que faz um botão aparecer ou sumir na barra é uma janela DE TOPO ser mostrada, oculta ou
/// destruída. Então: um gancho de sistema (<c>SetWinEventHook</c>) para EVENT_OBJECT_SHOW/HIDE/
/// DESTROY, filtrado a janelas que de fato ganham botão (de topo, sem dono ou com WS_EX_APPWINDOW,
/// sem WS_EX_TOOLWINDOW). Para HIDE e DESTROY não dá para ler o estilo (a janela já foi), por isso
/// o vigia LEMBRA as janelas que viu mostradas (semeadas com as já abertas) e só avisa quando uma
/// delas some.
///
/// Becos sem saída medidos no harness antes desta escolha (2026-11-03):
/// - WinEvents de OBJETO só do processo do Explorer: 0 eventos dentro da Shell_TrayWnd ao abrir e
///   fechar o Bloco de Notas — a barra XAML do Windows 11 não emite WinEvent por botão.
/// - CREATE/DESTROY de topo sem filtro: janelas COM ocultas ("OleMainThreadWndClass") nascem e
///   morrem a cada ~2,5 s em repouso; sem o conjunto de janelas MOSTRADAS, o vigia dispararia o
///   tempo todo.
///
/// Os callbacks chegam na thread que criou o gancho (WINEVENT_OUTOFCONTEXT + fila de mensagens
/// da UI), então não há concorrência: o conjunto e o evento são usados só nessa thread.
/// </summary>
public sealed class VigiaJanelasBarra : IDisposable
{
    /// <summary>Uma janela com botão na barra foi mostrada, oculta ou destruída (hwnd, motivo).</summary>
    public event Action<nint, string>? JanelaMudou;

    /// <summary>Janelas de topo vistas mostradas (ou já abertas no início) — para reconhecer HIDE/DESTROY.</summary>
    private readonly HashSet<nint> _mostradas = [];

    // Delegates em campos: o Windows guarda só o ponteiro; se o GC coletar o delegate, o callback
    // seguinte cai num endereço inválido.
    private NativeMethods.WinEventDelegate? _callback;
    private nint _hook;

    /// <summary>Instala o gancho. Deve ser chamado na thread da UI (os eventos chegam nela).</summary>
    public void Iniciar()
    {
        if (_hook != nint.Zero)
        {
            return;
        }

        // Semente: as janelas com botão que já estavam abertas, para o fechamento delas contar
        NativeMethods.EnumWindowsProc semear = (hwnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd) && GanhaBotaoNaBarra(hwnd))
            {
                _mostradas.Add(hwnd);
            }

            return true;
        };
        NativeMethods.EnumWindows(semear, nint.Zero);

        _callback = AoEvento;
        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_HIDE,
            nint.Zero, _callback, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
    }

    /// <summary>Quantas janelas com botão o vigia está acompanhando (diagnóstico/harness).</summary>
    public int JanelasAcompanhadas => _mostradas.Count;

    private void AoEvento(nint hook, uint evento, nint hwnd, int idObjeto, int idFilho, uint thread, uint instante)
    {
        if (hwnd == nint.Zero || idObjeto != NativeMethods.OBJID_WINDOW || idFilho != 0)
        {
            return; // partes internas de uma janela (menus, cursor, caret, controles filhos)
        }

        switch (evento)
        {
            case NativeMethods.EVENT_OBJECT_SHOW:
                // Só janela de topo que ganha botão; a semente garante que uma janela já contada
                // mostrada de novo (restaurar da bandeja) também avisa — o botão volta.
                if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) == hwnd && GanhaBotaoNaBarra(hwnd))
                {
                    _mostradas.Add(hwnd);
                    Avisar(hwnd, "mostrada");
                }

                break;

            case NativeMethods.EVENT_OBJECT_HIDE:
                if (_mostradas.Remove(hwnd))
                {
                    Avisar(hwnd, "oculta");
                }

                break;

            case NativeMethods.EVENT_OBJECT_DESTROY:
                if (_mostradas.Remove(hwnd))
                {
                    Avisar(hwnd, "destruída");
                }

                break;
        }

        // Proteção: janelas destruídas sem evento (processo morto) sairiam só por aqui. O conjunto
        // não cresce sem limite — numa sessão normal são dezenas de janelas, nunca milhares.
        if (_mostradas.Count > 4096)
        {
            _mostradas.RemoveWhere(h => !NativeMethods.IsWindow(h));
        }
    }

    private void Avisar(nint hwnd, string motivo) => JanelaMudou?.Invoke(hwnd, motivo);

    /// <summary>
    /// Regra do Windows para uma janela ter botão na barra: de topo, visível, sem WS_EX_TOOLWINDOW, e
    /// sem dono (ou com WS_EX_APPWINDOW, que força o botão mesmo com dono). Dicas, menus, popups e
    /// os próprios widgets desta app são WS_EX_TOOLWINDOW e ficam de fora — era o que faria o
    /// vigia disparar a cada dica de mouse.
    /// </summary>
    public static bool GanhaBotaoNaBarra(nint hwnd)
    {
        nint exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }

        if ((exStyle & NativeMethods.WS_EX_APPWINDOW) != 0)
        {
            return true;
        }

        return NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) == nint.Zero;
    }

    public void Dispose()
    {
        if (_hook != nint.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = nint.Zero;
        }

        _callback = null;
        _mostradas.Clear();
    }
}
