using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// Modo de acoplamento do widget à barra de tarefas do Windows 11.
/// Referência: 01-ANALISE.md §3 e 02-ARQUITETURA.md §4.
/// </summary>
public enum ModoEncaixeBarra
{
    /// <summary>
    /// Técnica A: Janela filha de Shell_TrayWnd via SetParent.
    /// Move-se com a barra e acompanha tela cheia automaticamente.
    /// </summary>
    ModoA_FilhaDaBarra = 0,

    /// <summary>
    /// Técnica B: Janela Topmost independente posicionada exatamente sobre a "vaga do Clima",
    /// com WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE (fora do Alt+Tab e sem roubar foco).
    /// </summary>
    ModoB_TopmostSobreBarra = 1
}

/// <summary>
/// Gerencia o ciclo de vida do encaixe do <see cref="TaskbarWidget"/> na barra de tarefas do Windows 11:
/// 1. Localiza a Shell_TrayWnd (ou usa fallback geométrico da base da tela se o Explorer estiver reiniciando);
/// 2. Mede a "vaga do Clima" (entre o último botão de app e o '^' da bandeja) via UI Automation;
/// 3. Aplica o Modo A (SetParent) ou Modo B (Topmost);
/// 4. Escuta a mensagem global "TaskbarCreated" para reencaixar automaticamente quando o Explorer reinicia;
/// 5. Vigia a lista de botões de aplicativos a cada 1,5 s para reposicionar ou compactar sem travar a UI.
/// </summary>
public sealed class TaskbarHost : IDisposable
{
    private readonly TaskbarWidget _widget;
    private readonly Action<string>? _logger;
    private readonly DispatcherTimer _timerVigiaLayout;

    private nint _hwndWidget;
    private nint _hwndTaskbar;
    private uint _msgTaskbarCreated;
    private ModoEncaixeBarra _modoPreferido;
    private ModoEncaixeBarra _modoAtivo;
    private bool _atualizandoGeometria;

    public bool ForcarModoCompactoTeste { get; set; }
    public ModoEncaixeBarra ModoAtivo => _modoAtivo;
    public PosicaoWidgetBarra UltimaPosicao { get; private set; }

    public TaskbarHost(
        TaskbarWidget widget,
        ModoEncaixeBarra modoPreferido = ModoEncaixeBarra.ModoA_FilhaDaBarra,
        Action<string>? logger = null)
    {
        _widget = widget;
        _modoPreferido = modoPreferido;
        _modoAtivo = modoPreferido;
        _logger = logger;

        _timerVigiaLayout = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        _timerVigiaLayout.Tick += async (_, _) => await AtualizarPosicaoAsync(forcarReencaixe: false);
    }

    public async Task InicializarAsync()
    {
        var helper = new WindowInteropHelper(_widget);
        _hwndWidget = helper.EnsureHandle();

        // Registra a mensagem que o Windows Explorer envia em broadcast sempre que a barra é recriada
        _msgTaskbarCreated = NativeMethods.RegisterWindowMessageW("TaskbarCreated");

        var source = HwndSource.FromHwnd(_hwndWidget);
        source?.AddHook(WndProc);

        AplicarEstilosExJanela();
        await AtualizarPosicaoAsync(forcarReencaixe: true);
        _timerVigiaLayout.Start();
    }

    /// <summary>
    /// Permite alternar em tempo de execução entre Modo A e Modo B (usado na PoC 1 da Onda 0).
    /// </summary>
    public async Task TrocarModoEncaixeAsync(ModoEncaixeBarra novoModo)
    {
        _modoPreferido = novoModo;
        _modoAtivo = novoModo;
        _logger?.Invoke($"Alternando modo de encaixe para: {_modoAtivo}");
        await AtualizarPosicaoAsync(forcarReencaixe: true);
    }

    public async Task AtualizarPosicaoAsync(bool forcarReencaixe)
    {
        if (_atualizandoGeometria || _hwndWidget == nint.Zero)
        {
            return;
        }

        _atualizandoGeometria = true;
        try
        {
            DpiScale dpiScale = VisualTreeHelper.GetDpi(_widget);
            double escalaDpi = dpiScale.DpiScaleX > 0.1 ? dpiScale.DpiScaleX : 1.5;

            nint taskbar = NativeMethods.FindWindowW("Shell_TrayWnd", null);
            NativeMethods.RECT rectBarra;
            int bordaAppsPx;
            int bordaBandejaPx;
            bool taskbarMudou = taskbar != _hwndTaskbar;
            _hwndTaskbar = taskbar;

            if (taskbar != nint.Zero && NativeMethods.GetWindowRect(taskbar, out rectBarra) && rectBarra.Width > 0)
            {
                uint dpiJanela = NativeMethods.GetDpiForWindow(taskbar);
                if (dpiJanela > 0)
                {
                    escalaDpi = dpiJanela / 96.0;
                }

                // Restaura o modo preferido se a barra voltou a ficar disponível
                _modoAtivo = _modoPreferido;

                // Lê a geometria real da barra via UI Automation numa thread de fundo (MTA)
                (bordaAppsPx, bordaBandejaPx) = await Task.Run(() => MedirLimitesBarraPorUia(taskbar, rectBarra));
            }
            else
            {
                // Fallback automático para Modo B (Topmost) quando a Shell_TrayWnd não está acessível
                // (ex.: Explorer reiniciando ou sessão sem shell ativo), preservando a geometria medida de 2560x1600.
                _modoAtivo = ModoEncaixeBarra.ModoB_TopmostSobreBarra;
                int larguraTelaPx = (int)Math.Round(SystemParameters.PrimaryScreenWidth * escalaDpi);
                int alturaTelaPx = (int)Math.Round(SystemParameters.PrimaryScreenHeight * escalaDpi);
                int alturaBarraPx = (int)Math.Round(48 * escalaDpi);

                rectBarra = new NativeMethods.RECT
                {
                    Left = 0,
                    Top = alturaTelaPx - alturaBarraPx,
                    Right = larguraTelaPx,
                    Bottom = alturaTelaPx
                };

                bordaAppsPx = (int)Math.Round(larguraTelaPx * 0.4566);     // ~1169 px em 2560 px
                bordaBandejaPx = (int)Math.Round(larguraTelaPx * 0.7019);  // ~1797 px em 2560 px
            }

            PosicaoWidgetBarra pos = TaskbarLayout.CalcularVagaDoClima(
                rectBarra,
                bordaAppsPx,
                bordaBandejaPx,
                escalaDpi);

            if (ForcarModoCompactoTeste)
            {
                pos = pos with
                {
                    Estado = EstadoColisaoBarra.CompactoUmaLinha,
                    LarguraFisicaPx = (int)Math.Round(TaskbarLayout.LarguraLogicaCompactaPx * escalaDpi)
                };
            }

            UltimaPosicao = pos;

            if (pos.Estado == EstadoColisaoBarra.OcultoPorColisao)
            {
                _widget.Visibility = Visibility.Hidden;
                _logger?.Invoke($"Colisão severa detectada (espaço livre={pos.EspacoLivreFisicoPx}px). Widget oculto temporariamente.");
                return;
            }

            _widget.AplicarEstadoColisao(pos.Estado);
            if (_widget.Visibility != Visibility.Visible)
            {
                _widget.Visibility = Visibility.Visible;
            }

            // Ajusta largura/altura lógica do WPF para casar exatamente com os pixels físicos calculados
            _widget.Width = pos.LarguraFisicaPx / escalaDpi;
            _widget.Height = pos.AlturaFisicaPx / escalaDpi;

            if (forcarReencaixe || taskbarMudou)
            {
                ExecutarEncaixeNativo(taskbar, pos);
            }
            else
            {
                ReposicionarJanela(pos);
            }
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"Erro em AtualizarPosicaoAsync: {ex.Message}");
        }
        finally
        {
            _atualizandoGeometria = false;
        }
    }

    private void ExecutarEncaixeNativo(nint taskbar, PosicaoWidgetBarra pos)
    {
        AplicarEstilosExJanela();

        if (_modoAtivo == ModoEncaixeBarra.ModoA_FilhaDaBarra && taskbar != nint.Zero)
        {
            nint resParent = NativeMethods.SetParent(_hwndWidget, taskbar);
            if (resParent == nint.Zero)
            {
                _logger?.Invoke("SetParent devolveu 0 — caindo automaticamente para Modo B (Topmost).");
                _modoAtivo = ModoEncaixeBarra.ModoB_TopmostSobreBarra;
                NativeMethods.SetParent(_hwndWidget, nint.Zero);
            }
        }
        else
        {
            NativeMethods.SetParent(_hwndWidget, nint.Zero);
        }

        ReposicionarJanela(pos);

        _logger?.Invoke(
            $"Encaixe aplicado [{_modoAtivo} | Taskbar=0x{taskbar:X}]: Tela=({pos.XTelaPx},{pos.YTelaPx}) " +
            $"RelBarra=({pos.XRelativoBarraPx},{pos.YRelativoBarraPx}) " +
            $"Tam={pos.LarguraFisicaPx}x{pos.AlturaFisicaPx}px " +
            $"EspacoLivre={pos.EspacoLivreFisicoPx}px Estado={pos.Estado}");
    }

    private void ReposicionarJanela(PosicaoWidgetBarra pos)
    {
        nint estiloAtual = NativeMethods.GetWindowLongPtr(_hwndWidget, NativeMethods.GWL_STYLE);
        bool ehWsChild = (estiloAtual & NativeMethods.WS_CHILD) != 0;

        int x = ehWsChild ? pos.XRelativoBarraPx : pos.XTelaPx;
        int y = ehWsChild ? pos.YRelativoBarraPx : pos.YTelaPx;

        NativeMethods.SetWindowPos(
            _hwndWidget,
            NativeMethods.HWND_TOPMOST,
            x,
            y,
            pos.LarguraFisicaPx,
            pos.AlturaFisicaPx,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private void AplicarEstilosExJanela()
    {
        nint exStyle = NativeMethods.GetWindowLongPtr(_hwndWidget, NativeMethods.GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOPMOST;
        NativeMethods.SetWindowLongPtr(_hwndWidget, NativeMethods.GWL_EXSTYLE, exStyle);
    }

    private static (int BordaDireitaAppsPx, int BordaEsquerdaBandejaPx) MedirLimitesBarraPorUia(
        nint taskbarHwnd,
        NativeMethods.RECT rectBarra)
    {
        int fallbackBandejaX = rectBarra.Right - (int)(rectBarra.Width * 0.28);
        nint trayNotify = NativeMethods.FindWindowExW(taskbarHwnd, nint.Zero, "TrayNotifyWnd", null);
        if (trayNotify != nint.Zero && NativeMethods.GetWindowRect(trayNotify, out NativeMethods.RECT rectTray) && rectTray.Left > rectBarra.Left)
        {
            fallbackBandejaX = rectTray.Left;
        }

        int bordaApps = rectBarra.Left + 400;
        int bordaBandeja = fallbackBandejaX;

        try
        {
            AutomationElement rootBarra = AutomationElement.FromHandle(taskbarHwnd);
            if (rootBarra == null)
            {
                return (bordaApps, bordaBandeja);
            }

            var condicaoApps = new PropertyCondition(
                AutomationElement.ClassNameProperty,
                "Taskbar.TaskListButtonAutomationPeer");

            AutomationElementCollection botoesApps = rootBarra.FindAll(TreeScope.Descendants, condicaoApps);
            int maiorDireitaApp = 0;
            foreach (AutomationElement botao in botoesApps)
            {
                Rect r = botao.Current.BoundingRectangle;
                if (!r.IsEmpty && r.Width > 0 && r.Right > maiorDireitaApp)
                {
                    maiorDireitaApp = (int)Math.Round(r.Right);
                }
            }

            if (maiorDireitaApp > rectBarra.Left)
            {
                bordaApps = maiorDireitaApp;
            }

            var condicaoBandeja = new PropertyCondition(
                AutomationElement.AutomationIdProperty,
                "SystemTrayIcon");

            AutomationElementCollection iconesBandeja = rootBarra.FindAll(TreeScope.Descendants, condicaoBandeja);
            int menorEsquerdaBandeja = int.MaxValue;
            foreach (AutomationElement icone in iconesBandeja)
            {
                Rect r = icone.Current.BoundingRectangle;
                if (!r.IsEmpty && r.Width > 0 && r.Left > bordaApps && r.Left < menorEsquerdaBandeja)
                {
                    menorEsquerdaBandeja = (int)Math.Round(r.Left);
                }
            }

            if (menorEsquerdaBandeja < int.MaxValue)
            {
                bordaBandeja = menorEsquerdaBandeja;
            }
        }
        catch
        {
            // Se o Explorer estiver no meio de uma reinicialização, usa o fallback do TrayNotifyWnd
        }

        return (bordaApps, bordaBandeja);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new nint(NativeMethods.MA_NOACTIVATE);
        }

        if (_msgTaskbarCreated != 0 && (uint)msg == _msgTaskbarCreated)
        {
            _logger?.Invoke("Evento TaskbarCreated recebido (Explorer reiniciou)! Reencaixando widget...");
            _ = _widget.Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(800);
                await AtualizarPosicaoAsync(forcarReencaixe: true);
            });
        }
        else if (msg is NativeMethods.WM_DISPLAYCHANGE or NativeMethods.WM_DPICHANGED or NativeMethods.WM_SETTINGCHANGE)
        {
            _ = _widget.Dispatcher.InvokeAsync(async () => await AtualizarPosicaoAsync(forcarReencaixe: false));
        }

        return nint.Zero;
    }

    public void Dispose()
    {
        _timerVigiaLayout.Stop();
    }
}
