using System.Windows;
using System.Windows.Media;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// Troca os tokens "Tema.*" (App.xaml) entre escuro e claro. Usado pelas JANELAS do app
/// (menu, painel rápido, estúdio), que seguem AppsUseLightTheme. O widget da barra segue a
/// barra (SystemUsesLightTheme) pelo motor de paletas — ver <see cref="Paletas"/>.
/// Cores do Modelo 3 (design/modelo-3-acrylic-pill, tokens "surface-*" e "primary").
/// </summary>
public static class ServicoTema
{
    public static bool ClaroAtivo { get; private set; }

    public static void Aplicar(bool claro)
    {
        ClaroAtivo = claro;
        ResourceDictionary r = Application.Current.Resources;
        if (claro)
        {
            r["Tema.Superficie"] = Pincel("#F9F9F9");
            r["Tema.SuperficieElevada"] = Pincel("#FFFFFF");
            r["Tema.SuperficieRecuada"] = Pincel("#F0F0F0");
            r["Tema.Borda"] = Pincel("#D6DCE1");
            r["Tema.Texto"] = Pincel("#1A1A1A");
            r["Tema.TextoSecundario"] = Pincel("#5C6670");
            r["Tema.Acento"] = Pincel("#005FB8");
            r["Tema.TextoSobreAcento"] = Pincel("#FFFFFF");
            r["Tema.Realce"] = Pincel("#0F000000");
            r["Tema.AoVivo"] = Pincel("#0F7B0F");
        }
        else
        {
            r["Tema.Superficie"] = Pincel("#202020");
            r["Tema.SuperficieElevada"] = Pincel("#2A2A2A");
            r["Tema.SuperficieRecuada"] = Pincel("#1B1C1C");
            r["Tema.Borda"] = Pincel("#3E484F");
            r["Tema.Texto"] = Pincel("#E5E2E1");
            r["Tema.TextoSecundario"] = Pincel("#BDC8D1");
            r["Tema.Acento"] = Pincel("#4CC2FF");
            r["Tema.TextoSobreAcento"] = Pincel("#00344B");
            r["Tema.Realce"] = Pincel("#14FFFFFF");
            r["Tema.AoVivo"] = Pincel("#6DCC60");
        }
    }

    private static SolidColorBrush Pincel(string hex)
    {
        var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }
}
