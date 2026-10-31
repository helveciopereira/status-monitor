using System.Runtime.InteropServices;

namespace VelocimetroInternet.Sistema;

/// <summary>
/// Ícone na área de notificação via Shell_NotifyIcon, usando a janela do widget para receber
/// os cliques (sem WinForms). O ícone vem do próprio .exe (recurso gravado por ApplicationIcon),
/// no tamanho certo para o DPI da barra (24 px a 150%).
///
/// O Explorer esquece todos os ícones quando reinicia → <see cref="Readicionar"/> deve ser chamado
/// no TaskbarCreated.
/// </summary>
public sealed class IconeBandeja : IDisposable
{
    private const uint IdIcone = 1;

    private readonly nint _hwnd;
    private readonly uint _msgCallback;
    private nint _hIcone;
    private bool _visivel;
    private string _dica = "Status Monitor";

    /// <summary>Clique esquerdo (abre o painel rápido).</summary>
    public event Action? CliqueEsquerdo;

    /// <summary>Clique direito (abre o menu).</summary>
    public event Action? CliqueDireito;

    public IconeBandeja(nint hwndJanela)
    {
        _hwnd = hwndJanela;
        _msgCallback = NativeMethods.RegisterWindowMessageW("VelocimetroInternet_Bandeja");
        _hIcone = CarregarIcone();
    }

    public bool Visivel
    {
        get => _visivel;
        set
        {
            if (value == _visivel)
            {
                return;
            }

            if (value)
            {
                Adicionar();
            }
            else
            {
                Remover();
            }
        }
    }

    /// <summary>Texto ao passar o mouse (até 127 caracteres), atualizado a cada leitura.</summary>
    public void DefinirDica(string texto)
    {
        _dica = texto.Length > 127 ? texto[..127] : texto;
        if (!_visivel)
        {
            return;
        }

        var dados = CriarDados();
        dados.uFlags = NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP;
        NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_MODIFY, ref dados);
    }

    /// <summary>Depois que o Explorer reinicia, o ícone precisa ser recriado.</summary>
    public void Readicionar()
    {
        if (_visivel)
        {
            _visivel = false;
            Adicionar();
        }
    }

    /// <summary>Gancho de mensagens da janela do widget (registrado pelo App).</summary>
    public nint ProcessarMensagem(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)msg != _msgCallback || _msgCallback == 0)
        {
            return nint.Zero;
        }

        // Sem NOTIFYICON_VERSION_4: lParam traz a própria mensagem do mouse
        int evento = (int)(lParam & 0xFFFF);
        if (evento == NativeMethods.WM_LBUTTONUP)
        {
            CliqueEsquerdo?.Invoke();
        }
        else if (evento == NativeMethods.WM_RBUTTONUP)
        {
            CliqueDireito?.Invoke();
        }

        handled = true;
        return nint.Zero;
    }

    private void Adicionar()
    {
        var dados = CriarDados();
        dados.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP;
        _visivel = NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_ADD, ref dados);
    }

    private void Remover()
    {
        var dados = CriarDados();
        NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_DELETE, ref dados);
        _visivel = false;
    }

    private NativeMethods.NOTIFYICONDATAW CriarDados() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = IdIcone,
        uCallbackMessage = _msgCallback,
        hIcon = _hIcone,
        szTip = _dica,
        szInfo = string.Empty,
        szInfoTitle = string.Empty
    };

    /// <summary>
    /// Ícone pequeno no tamanho do DPI da barra. 1ª tentativa: recurso 32512 do .exe (gerado pelo
    /// ApplicationIcon); 2ª: o primeiro ícone do arquivo .exe (ExtractIconEx).
    /// </summary>
    private nint CarregarIcone()
    {
        uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
        int lado = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXSMICON, dpi == 0 ? 96 : dpi);
        nint h = NativeMethods.LoadImageW(
            NativeMethods.GetModuleHandleW(null), NativeMethods.IDI_APLICACAO,
            NativeMethods.IMAGE_ICON, lado, lado, NativeMethods.LR_DEFAULTCOLOR);

        if (h == nint.Zero && Environment.ProcessPath is { } exe)
        {
            NativeMethods.ExtractIconExW(exe, 0, out _, out h, 1);
        }

        return h;
    }

    public void Dispose()
    {
        if (_visivel)
        {
            Remover();
        }

        if (_hIcone != nint.Zero)
        {
            NativeMethods.DestroyIcon(_hIcone);
            _hIcone = nint.Zero;
        }
    }
}
