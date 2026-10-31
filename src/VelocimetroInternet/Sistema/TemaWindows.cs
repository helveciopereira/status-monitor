using Microsoft.Win32;

namespace VelocimetroInternet.Sistema;

/// <summary>
/// Lê o tema do Windows. Atenção à diferença entre os dois valores do registro:
/// - SystemUsesLightTheme → cor da BARRA DE TAREFAS e do Iniciar (é o que o widget acompanha);
/// - AppsUseLightTheme    → cor das janelas de aplicativos (painel rápido e estúdio acompanham).
/// O usuário pode ter barra escura com apps claros (modo "personalizado"), por isso são separados.
/// A mudança chega como WM_SETTINGCHANGE com lParam = "ImmersiveColorSet".
/// </summary>
public static class TemaWindows
{
    private const string Chave = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Texto que acompanha WM_SETTINGCHANGE quando o tema claro/escuro muda.</summary>
    public const string SinalMudancaTema = "ImmersiveColorSet";

    public static bool BarraClara() => LerDword("SystemUsesLightTheme", padrao: 0) == 1;

    public static bool AppsClaros() => LerDword("AppsUseLightTheme", padrao: 1) == 1;

    private static int LerDword(string nome, int padrao)
    {
        try
        {
            using RegistryKey? k = Registry.CurrentUser.OpenSubKey(Chave);
            return k?.GetValue(nome) is int v ? v : padrao;
        }
        catch
        {
            return padrao;
        }
    }
}
