using System.Runtime.InteropServices;

namespace VelocimetroInternet.Sistema;

/// <summary>
/// Centraliza todas as assinaturas Win32 (P/Invoke) usadas pelo aplicativo.
/// Regra da arquitetura (02-ARQUITETURA.md §7): nenhuma outra classe declara DllImport/LibraryImport
/// diretamente, facilitando auditoria de memória não-gerenciada e manutenção em atualizações do Windows.
/// </summary>
public static class NativeMethods
{
    // =========================================================================
    // Constantes de Estilo de Janela (WinUser.h)
    // =========================================================================
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    public static readonly nint WS_CHILD = 0x40000000;
    public static readonly nint WS_POPUP = unchecked((nint)0x80000000);
    public static readonly nint WS_VISIBLE = 0x10000000;
    public static readonly nint WS_CLIPSIBLINGS = 0x04000000;

    /// <summary>
    /// WS_EX_TOOLWINDOW (0x80): oculta a janela do Alt+Tab e da barra de tarefas.
    /// WS_EX_NOACTIVATE (0x08000000): impede que clicar no widget roube o foco da janela ativa do usuário.
    /// WS_EX_LAYERED (0x00080000): necessário para composição com transparência por pixel.
    /// WS_EX_TRANSPARENT (0x00000020): usado apenas se o modo clique-através estiver ativo.
    /// </summary>
    public static readonly nint WS_EX_TOOLWINDOW = 0x00000080;
    public static readonly nint WS_EX_NOACTIVATE = 0x08000000;
    public static readonly nint WS_EX_LAYERED = 0x00080000;
    public static readonly nint WS_EX_TOPMOST = 0x00000008;

    public static readonly nint HWND_TOPMOST = new(-1);
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_ASYNCWINDOWPOS = 0x4000;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;

    // =========================================================================
    // Eventos de sistema (WinEvent) — usados para reafirmar o topo do widget
    // quando o usuário clica na barra/Iniciar (a barra também é "topmost" e,
    // ao ser clicada, passa por cima do widget no Modo B).
    // =========================================================================
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    /// <summary>Assinatura do callback de <see cref="SetWinEventHook"/>.</summary>
    public delegate void WinEventDelegate(
        nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    public static extern nint SetWinEventHook(
        uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventDelegate lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(nint hWinEventHook);

    // =========================================================================
    // Shell32 — detecção de tela cheia (jogo, vídeo, apresentação)
    // Valores de QUERY_USER_NOTIFICATION_STATE (shellapi.h):
    // 2 = QUNS_BUSY (app em tela cheia), 3 = QUNS_RUNNING_D3D_FULL_SCREEN,
    // 4 = QUNS_PRESENTATION_MODE. Nesses estados o widget deve sumir (01-ANALISE.md §3).
    // =========================================================================
    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int pquns);

    // =========================================================================
    // AppBar (v0.2.0) — registrar o widget como "appbar" SEM reservar espaço
    // (só ABM_NEW, nunca ABM_SETPOS) faz o shell nos avisar NA HORA, por mensagem,
    // quando um app entra/sai de tela cheia (ABN_FULLSCREENAPP). Antes dependíamos
    // do vigia de 1,5 s → o widget demorava a sumir (relato do dono, 2026-10-31).
    // =========================================================================
    public const uint ABM_NEW = 0x00000000;
    public const uint ABM_REMOVE = 0x00000001;
    public const int ABN_FULLSCREENAPP = 0x0000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public nint lParam;
    }

    [DllImport("shell32.dll")]
    public static extern nuint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    // =========================================================================
    // Geometria da janela em primeiro plano (checagem de tela cheia)
    // =========================================================================
    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFO lpmi);

    /// <summary>Aviso de energia; com PBT_APMRESUMEAUTOMATIC = o PC voltou de suspensão/hibernação.</summary>
    public const int WM_POWERBROADCAST = 0x0218;
    public static readonly nint PBT_APMRESUMEAUTOMATIC = 0x0012;

    /// <summary>Repassa o direito de trazer janela para frente a outro processo (2ª → 1ª instância).</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(uint dwProcessId);

    /// <summary>Barra de título escura (Win 10 20H1+/Win 11) para as janelas do app no tema escuro.</summary>
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    /// <summary>Ponto de tela (pixels físicos) — usado para ancorar o painel ao clique na bandeja.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

    /// <summary>Janela sob um ponto da tela — "o mouse está em cima do widget?" (v0.4.0, atualização).</summary>
    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(POINT pt);

    /// <summary>Sobe até a janela de topo (GA_ROOT = 2) a partir de uma janela filha.</summary>
    public const uint GA_ROOT = 2;

    [DllImport("user32.dll")]
    public static extern nint GetAncestor(nint hwnd, uint gaFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(nint hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(nint hWnd, uint Msg, nint wParam, nint lParam);


    // =========================================================================
    // Ícone da bandeja (Shell_NotifyIcon) — feito à mão para NÃO carregar o
    // WinForms inteiro (~10 MB a mais de RAM só por causa de um ícone).
    // =========================================================================
    public const uint NIM_ADD = 0x00000000;
    public const uint NIM_MODIFY = 0x00000001;
    public const uint NIM_DELETE = 0x00000002;
    public const uint NIM_SETVERSION = 0x00000004;
    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON = 0x00000002;
    public const uint NIF_TIP = 0x00000004;
    public const uint NIF_SHOWTIP = 0x00000080;
    // v1.0.0: balão/notificação do ícone (aviso de franquia). NIIF_INFO = ícone "i";
    // NIIF_RESPECT_QUIET_TIME = não aparece no "não perturbe" nem na 1ª hora após instalar o Windows.
    public const uint NIF_INFO = 0x00000010;
    public const uint NIIF_INFO = 0x00000001;
    public const uint NIIF_RESPECT_QUIET_TIME = 0x00000080;
    public const uint NOTIFYICON_VERSION_4 = 4;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_CONTEXTMENU = 0x007B;
    public const int NIN_SELECT = 0x0400;
    public const int NIN_KEYSELECT = 0x0401;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    // v1.0.0: duplo clique no widget (abre o Gerenciador de Tarefas). Contado à mão porque o
    // 1º clique abre o painel e tira o foco do widget — o ClickCount do WPF não é confiável aí.
    [DllImport("user32.dll")]
    public static extern uint GetDoubleClickTime();

    /// <summary>Largura/altura (px) do retângulo em que o 2º clique ainda conta como duplo.</summary>
    public const int SM_CXDOUBLECLK = 36;
    public const int SM_CYDOUBLECLK = 37;

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    public const uint IMAGE_ICON = 1;
    public const uint LR_DEFAULTCOLOR = 0x00000000;

    /// <summary>ID do recurso de ícone que o apphost do .NET grava a partir de &lt;ApplicationIcon&gt;.</summary>
    public const int IDI_APLICACAO = 32512;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint LoadImageW(nint hInst, nint name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(nint hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern uint ExtractIconExW(string lpszFile, int nIconIndex, out nint phiconLarge, out nint phiconSmall, uint nIcons);

    /// <summary>SM_CXSMICON: lado do ícone pequeno (bandeja) — 16 px a 100%, 24 px a 150%.</summary>
    public const int SM_CXSMICON = 49;

    [DllImport("user32.dll")]
    public static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int MA_NOACTIVATE = 3;
    public const int WM_DISPLAYCHANGE = 0x007E;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_SETTINGCHANGE = 0x001A;

    // =========================================================================
    // User32.dll — Gerenciamento da Barra de Tarefas e Janelas
    // =========================================================================

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint FindWindowExW(nint hWndParent, nint hWndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetParent(nint hWndChild, nint hWndNewParent);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(nint hWnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    // =========================================================================
    // Psapi / Kernel32 — Redução de Working Set após inicialização (02-ARQUITETURA.md §8)
    // =========================================================================

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EmptyWorkingSet(nint hProcess);

    // =========================================================================
    // CPU e RAM do sistema (v0.5.0) — kernel32
    // =========================================================================

    /// <summary>
    /// Tempos ACUMULADOS desde o boot, somando todos os núcleos, em unidades de 100 ns.
    /// Atenção: o tempo de kernel INCLUI o tempo ocioso (por isso ocupado = kernel + user − idle).
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

    /// <summary>Estado da memória física/virtual. <c>dwLength</c> PRECISA vir preenchido com o tamanho da struct.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;           // % de uso já arredondado pelo Windows (0–100)
        public ulong ullTotalPhys;          // RAM física total (bytes)
        public ulong ullAvailPhys;          // RAM física disponível (bytes) — base do "Em uso" do Gerenciador
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    // =========================================================================
    // IpHlpApi.dll — Contadores de Rede de 64 bits (GetIfTable2 / FreeMibTable)
    // =========================================================================

    public const int IF_MAX_STRING_SIZE = 256;
    public const int IF_MAX_PHYS_ADDRESS_LENGTH = 32;

    /// <summary>
    /// Lê a tabela MIB-II completa de interfaces de rede (incluindo contadores de 64 bits InOctets/OutOctets).
    /// ATENÇÃO (Regra de memória): O ponteiro retornado em <paramref name="Table"/> é alocado no heap nativo
    /// pelo Windows e DEVE obrigatoriamente ser liberado com <see cref="FreeMibTable"/> em bloco try/finally.
    /// </summary>
    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetIfTable2(out nint Table);

    [DllImport("iphlpapi.dll")]
    public static extern void FreeMibTable(nint Memory);

    /// <summary>
    /// Representa uma linha da tabela MIB_IF_ROW2 (netioapi.h no Windows x64, 1352 bytes).
    /// Contém metadados da placa (física vs virtual, tipo, status operacional) e contadores de 64 bits sem overflow.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MIB_IF_ROW2
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public Guid InterfaceGuid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = IF_MAX_STRING_SIZE + 1)]
        public string Alias;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = IF_MAX_STRING_SIZE + 1)]
        public string Description;

        public uint PhysicalAddressLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = IF_MAX_PHYS_ADDRESS_LENGTH)]
        public byte[] PhysicalAddress;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = IF_MAX_PHYS_ADDRESS_LENGTH)]
        public byte[] PermanentPhysicalAddress;

        public uint Mtu;
        public uint Type;                   // 6 = Ethernet (IEEE 802.3), 71 = Wi-Fi (IEEE 802.11)
        public uint TunnelType;
        public uint MediaType;
        public uint PhysicalMediumType;
        public uint AccessType;
        public uint DirectionType;

        /// <summary>
        /// Byte de flags bit-a-bit:
        /// Bit 0 (0x01) = HardwareInterface (true para placas de rede físicas reais).
        /// Bit 1 (0x02) = FilterInterface.
        /// </summary>
        public byte InterfaceAndOperStatusFlags;

        public uint OperStatus;             // 1 = IfOperStatusUp
        public uint AdminStatus;
        public uint MediaConnectState;      // 1 = MediaConnectStateConnected
        public Guid NetworkGuid;
        public uint ConnectionType;

        public ulong TransmitLinkSpeed;
        public ulong ReceiveLinkSpeed;
        public ulong InOctets;              // Total de bytes recebidos (64-bit, nunca estoura)
        public ulong InUcastPkts;
        public ulong InNUcastPkts;
        public ulong InDiscards;
        public ulong InErrors;
        public ulong InUnknownProtos;
        public ulong InUcastOctets;
        public ulong InMulticastOctets;
        public ulong InBroadcastOctets;
        public ulong OutOctets;             // Total de bytes enviados (64-bit, nunca estoura)
        public ulong OutUcastPkts;
        public ulong OutNUcastPkts;
        public ulong OutDiscards;
        public ulong OutErrors;
        public ulong OutUcastOctets;
        public ulong OutMulticastOctets;
        public ulong OutBroadcastOctets;
        public ulong OutQLen;

        public readonly bool IsHardwareInterface => (InterfaceAndOperStatusFlags & 0x01) != 0;
        public readonly bool IsOperStatusUp => OperStatus == 1;
        public readonly bool IsMediaConnected => MediaConnectState == 1;
    }

    // =========================================================================
    // v1.3.0 (Onda 8): consumo por aplicativo — nome e ícone do .exe
    // =========================================================================

    /// <summary>
    /// QueryDosDevice("C:") → "\Device\HarddiskVolume4". O Windows identifica cada app pelo
    /// caminho de DISPOSITIVO; com o mapa unidade→dispositivo vira um caminho normal.
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint QueryDosDevice(string lpDeviceName, char[] lpTargetPath, uint ucchMax);

    public const uint SHGFI_ICON = 0x100;
    public const uint SHGFI_SMALLICON = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    /// <summary>Ícone pequeno do arquivo (o mesmo do Explorer). O HICON devolvido DEVE ir para DestroyIcon.</summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern nint SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);
    // (DestroyIcon já é declarado acima, na seção do ícone da bandeja)
}
