using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// v1.3.0 (Onda 8): ícone pequeno de cada .exe da lista de consumo por app — o mesmo do
/// Explorer (SHGetFileInfo). Guardado em cache pelo caminho: a lista se atualiza a cada 30 s e
/// extrair ícone toda vez seria desperdício. O cache morre com a janela (não fica na memória
/// do widget o dia todo).
/// </summary>
public sealed class IconesApps
{
    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ícone do arquivo, ou null (sem caminho, arquivo sumiu, app da Loja) → a tela usa um glifo.</summary>
    public ImageSource? Obter(string? caminho)
    {
        if (string.IsNullOrEmpty(caminho))
        {
            return null;
        }

        if (_cache.TryGetValue(caminho, out ImageSource? conhecido))
        {
            return conhecido;
        }

        ImageSource? icone = null;
        if (File.Exists(caminho))
        {
            var info = new NativeMethods.SHFILEINFO();
            NativeMethods.SHGetFileInfo(caminho, 0, ref info, (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
                NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON);
            if (info.hIcon != 0)
            {
                try
                {
                    BitmapSource b = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    b.Freeze(); // pode ser usado por qualquer thread e não segura o HICON
                    icone = b;
                }
                finally
                {
                    NativeMethods.DestroyIcon(info.hIcon); // o HICON é nosso: sem isto, vaza a cada ícone
                }
            }
        }

        return _cache[caminho] = icone;
    }
}
