using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// v0.8.0 (Onda 2, item adiado "fonte/tamanho, ordem das linhas"): aparência do texto do widget.
///
/// A largura do widget na barra é calculada pelo <see cref="TaskbarLayout"/> a partir de constantes
/// medidas com a fonte padrão (Segoe UI Variable Text 10,5). Com outra fonte/tamanho, as pílulas
/// precisam crescer ou encolher na mesma proporção — senão o texto seria cortado. Em vez de chutar
/// um número por combinação, o <see cref="FatorLargura"/> MEDE o texto de pior caso nas duas
/// configurações (FormattedText, o mesmo motor que desenha a tela) e devolve a razão.
/// </summary>
public readonly record struct AparenciaWidget(string Fonte, string Tamanho, bool DownloadEmCima)
{
    /// <summary>Aparência de sempre (antes da v0.8.0): fator de largura exatamente 1.</summary>
    public static readonly AparenciaWidget Padrao = new("segoe", "normal", false);

    /// <summary>
    /// Texto mais largo que a pílula de rede precisa caber (mesmo pior caso que definiu os 78 px
    /// lógicos da v0.6.0). '8' é o algarismo mais largo nas fontes proporcionais.
    /// </summary>
    private const string TextoPiorCaso = "↓ 88,8 Mbps";

    /// <summary>Família de fonte WPF para o id gravado no settings.json.</summary>
    public static FontFamily FamiliaDe(string fonte) => fonte switch
    {
        "segoe-classica" => new FontFamily("Segoe UI"),
        "cascadia" => new FontFamily("Cascadia Mono, Consolas"),
        _ => new FontFamily("Segoe UI Variable Text, Segoe UI")
    };

    /// <summary>Tamanho dos VALORES (↑/↓, CPU/RAM) em px lógicos. O padrão 10,5 é o de sempre.</summary>
    public static double TamanhoDe(string tamanho) => tamanho switch
    {
        "pequeno" => 9.5,
        "grande" => 11.5,
        _ => 10.5
    };

    public FontFamily Familia => FamiliaDe(Fonte);

    /// <summary>Tamanho dos valores (ex.: "1,24 MB/s").</summary>
    public double TamanhoValor => TamanhoDe(Tamanho);

    /// <summary>Rótulos "CPU"/"RAM": 1,5 menor que o valor (9 no padrão, como antes).</summary>
    public double TamanhoRotulo => TamanhoValor - 1.5;

    /// <summary>Linha única do modo compacto: 0,5 menor que o valor (10 no padrão, como antes).</summary>
    public double TamanhoCompacto => TamanhoValor - 0.5;

    /// <summary>Separadores " | " do modo compacto (9,5 no padrão, como antes).</summary>
    public double TamanhoSeparador => TamanhoValor - 1.0;

    /// <summary>
    /// Quanto as pílulas devem crescer (&gt; 1) ou encolher (&lt; 1) em relação à aparência padrão.
    /// Exatamente 1 no padrão — a geometria da v0.7.0 fica idêntica, bit a bit.
    /// Limitado a [0,8; 1,6] para nunca gerar um widget absurdo.
    /// </summary>
    public double FatorLargura()
    {
        if (Fonte == Padrao.Fonte && Tamanho == Padrao.Tamanho)
        {
            return 1.0;
        }

        double referencia = MedirLargura(FamiliaDe(Padrao.Fonte), TamanhoDe(Padrao.Tamanho));
        double atual = MedirLargura(Familia, TamanhoValor);
        if (referencia <= 0 || !double.IsFinite(atual))
        {
            return 1.0;
        }

        return Math.Clamp(atual / referencia, 0.8, 1.6);
    }

    /// <summary>Largura do texto de pior caso, em px lógicos, no peso SemiBold usado no widget.</summary>
    private static double MedirLargura(FontFamily familia, double tamanho)
    {
        var tipo = new Typeface(familia, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var texto = new FormattedText(
            TextoPiorCaso, CultureInfo.GetCultureInfo("pt-BR"), FlowDirection.LeftToRight,
            tipo, tamanho, Brushes.Black, pixelsPerDip: 1.0);
        return texto.WidthIncludingTrailingWhitespace;
    }
}
