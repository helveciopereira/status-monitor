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
}
