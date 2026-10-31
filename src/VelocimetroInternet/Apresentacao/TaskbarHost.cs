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

    // Gancho de "janela em primeiro plano mudou" (reafirma o topo na hora, sem esperar o timer).
    // O delegate fica num campo para o GC não coletá-lo enquanto o Windows ainda o chama.
    private NativeMethods.WinEventDelegate? _callbackForeground;
    private nint _hookForeground;

    // Três motivos independentes para o widget ficar oculto (v0.2.0). Ele só aparece quando
    // NENHUM está ativo — antes cada caminho mexia em Visibility por conta própria e um
    // desfazia o outro (ex.: sair da tela cheia reexibia um widget que o usuário escondeu).
    private bool _ocultoPorTelaCheia;
    private bool _ocultoPorColisao;
    private bool _ocultoPeloUsuario;

    // Registro como "appbar" para receber ABN_FULLSCREENAPP instantaneamente
    private uint _msgCallbackAppBar;
    private bool _appBarRegistrada;
    private readonly uint _pidProprio = (uint)Environment.ProcessId;

    /// <summary>Disparado quando o estado de tela cheia muda (o painel rápido também se fecha).</summary>
    public event Action<bool>? TelaCheiaMudou;

    /// <summary>
    /// "Fixar na Barra" do painel rápido (Modelo 3). false = widget escondido pelo usuário;
    /// o app continua medindo e o ícone da bandeja continua disponível para reabrir.
    /// </summary>
    public bool FixadoNaBarra
    {
        get => !_ocultoPeloUsuario;
        set
        {
            _ocultoPeloUsuario = !value;
            AplicarVisibilidade();
        }
    }

    /// <summary>Há app em tela cheia agora (widget oculto por isso). Lido pela atualização automática (v0.4.0).</summary>
    public bool EmTelaCheia => _ocultoPorTelaCheia;

    private bool DeveMostrar => !_ocultoPorTelaCheia && !_ocultoPorColisao && !_ocultoPeloUsuario;

    private void AplicarVisibilidade()
    {
        Visibility alvo = DeveMostrar ? Visibility.Visible : Visibility.Hidden;
        if (_widget.Visibility != alvo)
        {
            _widget.Visibility = alvo;
        }
    }

    /// <summary>
    /// Registra o widget como appbar SEM reservar espaço (apenas ABM_NEW). O shell passa a
    /// enviar ABN_FULLSCREENAPP para esta janela no instante em que um app entra/sai de tela
    /// cheia. Precisa ser refeito quando o Explorer reinicia (TaskbarCreated).
    /// </summary>
    private void RegistrarAppBar()
    {
        if (_appBarRegistrada)
        {
            RemoverAppBar();
        }

        var dados = new NativeMethods.APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.APPBARDATA>(),
            hWnd = _hwndWidget,
            uCallbackMessage = _msgCallbackAppBar
        };
        _appBarRegistrada = NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref dados) != 0;
        _logger?.Invoke($"Registro como appbar (aviso instantâneo de tela cheia): {(_appBarRegistrada ? "ok" : "falhou — fica o vigia de 1,5 s")}");
    }

    private void RemoverAppBar()
    {
        var dados = new NativeMethods.APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.APPBARDATA>(),
            hWnd = _hwndWidget
        };
        NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref dados);
        _appBarRegistrada = false;
    }

    /// <summary>
    /// Padrão = Modo B. Motivo (v0.1.1, verificado na sessão real do usuário em 2026-10-31):
    /// no Modo A a janela WPF transparente (layered) vira filha da barra XAML do Win 11 e
    /// NÃO É DESENHADA; e, quando o Explorer reinicia, a barra antiga é destruída levando a
    /// nossa janela junto (o processo segue vivo, sem nada na tela). Modo A só por --modo-a.
    /// </summary>
    public TaskbarHost(
        TaskbarWidget widget,
        ModoEncaixeBarra modoPreferido = ModoEncaixeBarra.ModoB_TopmostSobreBarra,
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

        _msgCallbackAppBar = NativeMethods.RegisterWindowMessageW("VelocimetroInternet_AppBarCallback");
        RegistrarAppBar();

        AplicarEstilosExJanela();
        await AtualizarPosicaoAsync(forcarReencaixe: true);
        _timerVigiaLayout.Start();

        // Reafirma o topo sempre que outra janela vem para a frente (ex.: clique na barra ou no
        // Iniciar). WINEVENT_OUTOFCONTEXT entrega o evento na fila de mensagens desta thread (UI).
        _callbackForeground = (_, _, _, _, _, _, _) => ReafirmarTopoOuOcultar();
        _hookForeground = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            nint.Zero, _callbackForeground, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
    }

    /// <summary>
    /// Só no Modo B: (1) se houver app em tela cheia, esconde o widget para não cobrir jogo/vídeo;
    /// (2) caso contrário, coloca o widget de volta acima da barra (que também é topmost).
    /// Chamado pelo aviso do shell (ABN_FULLSCREENAPP, instantâneo), pela troca de janela em
    /// primeiro plano e pelo vigia de 1,5 s (rede de segurança).
    /// </summary>
    private void ReafirmarTopoOuOcultar()
    {
        if (_modoAtivo != ModoEncaixeBarra.ModoB_TopmostSobreBarra || _hwndWidget == nint.Zero)
        {
            return;
        }

        bool telaCheia = DetectorTelaCheia.HaTelaCheiaAgora(_pidProprio);
        if (telaCheia != _ocultoPorTelaCheia)
        {
            _ocultoPorTelaCheia = telaCheia;
            _logger?.Invoke(telaCheia ? "Tela cheia detectada — widget oculto." : "Saiu da tela cheia — widget de volta.");
            AplicarVisibilidade();
            TelaCheiaMudou?.Invoke(telaCheia);
        }

        if (!DeveMostrar)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            _hwndWidget, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
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

            bool colisaoSevera = pos.Estado == EstadoColisaoBarra.OcultoPorColisao;
            if (colisaoSevera != _ocultoPorColisao)
            {
                _ocultoPorColisao = colisaoSevera;
                _logger?.Invoke(colisaoSevera
                    ? $"Colisão severa detectada (espaço livre={pos.EspacoLivreFisicoPx}px). Widget oculto temporariamente."
                    : "Espaço liberado — widget volta a aparecer.");
            }

            if (colisaoSevera)
            {
                AplicarVisibilidade();
                return;
            }

            _widget.AplicarEstadoColisao(pos.Estado);

            // Ajusta largura/altura lógica do WPF para casar exatamente com os pixels físicos calculados
            _widget.Width = pos.LarguraFisicaPx / escalaDpi;
            _widget.Height = pos.AlturaFisicaPx / escalaDpi;

            // Posição final ANTES de tornar visível: antes a janela aparecia no canto da tela
            // por um instante e só depois ia para a barra (o "piscar no topo" relatado na v0.1.0).
            bool ehFilha = (NativeMethods.GetWindowLongPtr(_hwndWidget, NativeMethods.GWL_STYLE) & NativeMethods.WS_CHILD) != 0;
            if (!ehFilha)
            {
                _widget.Left = pos.XTelaPx / escalaDpi;
                _widget.Top = pos.YTelaPx / escalaDpi;
            }

            AplicarVisibilidade();

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

        // Em tela cheia não força SWP_SHOWWINDOW (senão o vigia de 1,5 s reexibiria o widget por cima do jogo)
        uint flags = NativeMethods.SWP_NOACTIVATE | (DeveMostrar ? NativeMethods.SWP_SHOWWINDOW : 0u);
        NativeMethods.SetWindowPos(
            _hwndWidget,
            NativeMethods.HWND_TOPMOST,
            x,
            y,
            pos.LarguraFisicaPx,
            pos.AlturaFisicaPx,
            flags);

        // O vigia periódico também cobre tela cheia e topo (rede de segurança se o gancho perder evento)
        ReafirmarTopoOuOcultar();
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

        // Aviso instantâneo do shell: lParam != 0 → um app ENTROU em tela cheia; 0 → saiu.
        // Não confiamos cegamente no lParam (ele também dispara ao clicar na área de trabalho):
        // reconfirmamos com a regra geométrica do DetectorTelaCheia.
        if (_msgCallbackAppBar != 0 && (uint)msg == _msgCallbackAppBar && wParam == NativeMethods.ABN_FULLSCREENAPP)
        {
            ReafirmarTopoOuOcultar();
            handled = true;
            return nint.Zero;
        }

        if (_msgTaskbarCreated != 0 && (uint)msg == _msgTaskbarCreated)
        {
            _logger?.Invoke("Evento TaskbarCreated recebido (Explorer reiniciou)! Reencaixando widget...");
            TaskbarRecriada?.Invoke();
            _ = _widget.Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(800);
                RegistrarAppBar(); // o registro de appbar morre com o Explorer antigo
                await AtualizarPosicaoAsync(forcarReencaixe: true);
            });
        }
        else if (msg is NativeMethods.WM_DISPLAYCHANGE or NativeMethods.WM_DPICHANGED or NativeMethods.WM_SETTINGCHANGE)
        {
            _ = _widget.Dispatcher.InvokeAsync(async () => await AtualizarPosicaoAsync(forcarReencaixe: false));
        }

        return nint.Zero;
    }

    /// <summary>Disparado quando o Explorer recria a barra (o ícone da bandeja precisa ser readicionado).</summary>
    public event Action? TaskbarRecriada;

    /// <summary>Janela nativa do widget (usada pelo ícone da bandeja e pelo painel rápido).</summary>
    public nint HwndWidget => _hwndWidget;

    /// <summary>Permite que outros componentes (bandeja, instância única) escutem mensagens do widget.</summary>
    public void AdicionarGanchoMensagens(HwndSourceHook gancho) => HwndSource.FromHwnd(_hwndWidget)?.AddHook(gancho);

    public void Dispose()
    {
        _timerVigiaLayout.Stop();
        if (_appBarRegistrada)
        {
            RemoverAppBar();
        }
        if (_hookForeground != nint.Zero)
        {
            NativeMethods.UnhookWinEvent(_hookForeground);
            _hookForeground = nint.Zero;
        }
    }
}
