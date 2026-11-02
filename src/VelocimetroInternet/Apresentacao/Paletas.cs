using System.Globalization;
using System.Windows.Media;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// Uma paleta do "Estúdio de Cores" do Modelo 3 (design/modelo-3-acrylic-pill).
/// As cores-base valem para a barra ESCURA; as da barra CLARA são fixas (quando o design as
/// define) ou DERIVADAS escurecendo até atingir o contraste mínimo.
/// </summary>
public sealed record PaletaCores(
    string Id,
    string Nome,
    string Descricao,
    Color UploadEscuro,
    Color DownloadEscuro,
    Color? UploadClaroFixo = null,
    Color? DownloadClaroFixo = null,
    double ContrasteMinimo = 4.5);

/// <summary>Cores já resolvidas para um tema da barra — é o que o widget pinta.</summary>
public readonly record struct CoresResolvidas(
    Color Upload,
    Color Download,
    Color FundoPilulaUpload,
    Color BordaPilulaUpload,
    Color FundoPilulaDownload,
    Color BordaPilulaDownload,
    Color Realce,
    Color FundoCompacto,
    Color BordaCompacto,
    Color Separador,
    double ContrasteUpload,
    double ContrasteDownload,
    // v1.7.0 (Onda 13): "borracha" do mini gráfico — a cor da barra, translúcida, pintada ACIMA da
    // curva para apagar parte do véu da pílula. O que sobra colorido (abaixo da curva) é o gráfico.
    Color MascaraGrafico = default,
    // v1.7.1: vermelho do número de CPU/RAM em alerta (≥ 80 %), já calibrado para o contraste
    // mínimo da paleta sobre a pílula neutra do sistema e sobre o fundo do modo uma linha.
    Color Alerta = default);

/// <summary>
/// Catálogo das paletas + o algoritmo de contraste WCAG 2.1 ("Motor WCAG" do design):
/// se a cor do texto cair abaixo do mínimo sobre o fundo real da pílula (barra + véu
/// translúcido da própria cor), a luminância é recalibrada em passos até passar.
/// </summary>
public static class Paletas
{
    /// <summary>Cor da barra do Windows 11 em cada tema (valores usados no design).</summary>
    public static readonly Color BarraEscura = Color.FromRgb(0x1C, 0x1C, 0x1C);
    public static readonly Color BarraClara = Color.FromRgb(0xF3, 0xF3, 0xF3);

    // Opacidade das pílulas (Modelo 3): fundo 18%/14%, borda 28%/25%
    private const byte AlfaFundoEscuro = 0x2E;
    private const byte AlfaBordaEscuro = 0x48;
    private const byte AlfaFundoClaro = 0x24;
    private const byte AlfaBordaClaro = 0x40;

    public const string IdPersonalizada = "personalizada";

    /// <summary>As 5 paletas predefinidas da Onda 3, exatamente como no design.</summary>
    public static readonly IReadOnlyList<PaletaCores> Predefinidas =
    [
        new("classico", "Clássico Acrylic", "Padrão Fluent do Windows 11 com luminosidade equilibrada.",
            Hex("#4CC2FF"), Hex("#FF6B6B"), Hex("#005FB8"), Hex("#C42B1C")),
        new("neon", "Neon Cyberpunk", "Ciano elétrico com magenta de alto impacto visual para OLED.",
            Hex("#00F0FF"), Hex("#FF007F")),
        new("mono", "Monocromático Minimal", "Tons neutros para quem prefere a barra sem distração de cor.",
            Hex("#E0E0E0"), Hex("#9E9E9E")),
        new("alto-contraste", "Alto Contraste WCAG", "Legibilidade extrema para baixa visão (mínimo 7:1).",
            Hex("#FFFF00"), Hex("#00FFFF"), ContrasteMinimo: 7.0),
        new("bio", "Bio Verde & Âmbar", "Esmeralda e âmbar inspirados em telemetria industrial.",
            Hex("#10B981"), Hex("#F59E0B"))
    ];

    /// <summary>Paleta pelo id; "personalizada" usa as cores escolhidas pelo usuário.</summary>
    public static PaletaCores Obter(string id, string corUploadPersonalizada, string corDownloadPersonalizada)
    {
        if (string.Equals(id, IdPersonalizada, StringComparison.OrdinalIgnoreCase)
            && TentarHex(corUploadPersonalizada, out Color up)
            && TentarHex(corDownloadPersonalizada, out Color down))
        {
            return new PaletaCores(IdPersonalizada, "Personalizada", "Cores escolhidas por você.", up, down);
        }

        return Predefinidas.FirstOrDefault(p => p.Id == id) ?? Predefinidas[0];
    }

    /// <summary>Resolve as cores finais para a barra clara ou escura, garantindo o contraste mínimo.</summary>
    public static CoresResolvidas Resolver(PaletaCores paleta, bool barraClara)
    {
        Color barra = barraClara ? BarraClara : BarraEscura;
        byte alfaFundo = barraClara ? AlfaFundoClaro : AlfaFundoEscuro;
        byte alfaBorda = barraClara ? AlfaBordaClaro : AlfaBordaEscuro;

        Color up = barraClara ? paleta.UploadClaroFixo ?? paleta.UploadEscuro : paleta.UploadEscuro;
        Color down = barraClara ? paleta.DownloadClaroFixo ?? paleta.DownloadEscuro : paleta.DownloadEscuro;

        up = GarantirContraste(up, barra, alfaFundo, paleta.ContrasteMinimo, escurecer: barraClara);
        down = GarantirContraste(down, barra, alfaFundo, paleta.ContrasteMinimo, escurecer: barraClara);

        // v1.7.1: fundos REAIS por trás do número de CPU/RAM. A pílula do sistema é um véu neutro
        // (branco na barra escura, preto na clara, alfa 0x1A — o mesmo de TaskbarWidget.AplicarCores);
        // o modo uma linha usa o FundoCompacto. O vermelho tem de passar nos dois.
        Color fundoCompacto = barraClara ? Color.FromArgb(0x14, 0, 0, 0) : Color.FromArgb(0x26, 0x1E, 0x20, 0x26);
        Color neutro = barraClara ? Colors.Black : Colors.White;
        Color alerta = GarantirContrasteSobre(
            barraClara ? AlertaClaro : AlertaEscuro,
            [Misturar(barra, neutro, AlfaFundoSistema), Misturar(barra, fundoCompacto, fundoCompacto.A)],
            paleta.ContrasteMinimo,
            escurecer: barraClara);

        return new CoresResolvidas(
            Upload: up,
            Download: down,
            FundoPilulaUpload: ComAlfa(up, alfaFundo),
            BordaPilulaUpload: ComAlfa(up, alfaBorda),
            FundoPilulaDownload: ComAlfa(down, alfaFundo),
            BordaPilulaDownload: ComAlfa(down, alfaBorda),
            Realce: barraClara ? Color.FromArgb(0x0D, 0, 0, 0) : Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF),
            FundoCompacto: fundoCompacto,
            BordaCompacto: barraClara ? Color.FromArgb(0x26, 0, 0, 0) : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF),
            Separador: barraClara ? Color.FromArgb(0x55, 0, 0, 0) : Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF),
            ContrasteUpload: RazaoContraste(up, Misturar(barra, up, alfaFundo)),
            ContrasteDownload: RazaoContraste(down, Misturar(barra, down, alfaFundo)),
            MascaraGrafico: ComAlfa(barra, AlfaMascaraGrafico),
            Alerta: alerta);
    }

    /// <summary>
    /// v1.7.1: vermelho de partida do alerta de CPU/RAM. São os vermelhos "crítico" do Fluent
    /// (Windows 11): mais claro na barra escura, mais fechado na clara. O motor de contraste ainda
    /// clareia/escurece se a paleta exigir mais (Alto Contraste pede 7:1).
    /// </summary>
    public static readonly Color AlertaEscuro = Color.FromRgb(0xFF, 0x5F, 0x5F);
    public static readonly Color AlertaClaro = Color.FromRgb(0xC4, 0x2B, 0x1C);

    /// <summary>Opacidade do véu neutro da pílula de CPU/RAM (o mesmo 0x1A de TaskbarWidget.AplicarCores).</summary>
    public const byte AlfaFundoSistema = 0x1A;

    /// <summary>
    /// v1.7.1: como <see cref="GarantirContraste"/>, mas para texto sobre fundos FIXOS (que não
    /// mudam com a cor do texto): passos de 4% de luminosidade até passar em TODOS os fundos.
    /// </summary>
    public static Color GarantirContrasteSobre(Color texto, IReadOnlyList<Color> fundos, double minimo, bool escurecer)
    {
        (double h, double s, double l) = ParaHsl(texto);
        Color atual = texto;
        for (int passo = 0; passo < 30; passo++)
        {
            if (fundos.All(f => RazaoContraste(atual, f) >= minimo))
            {
                return atual;
            }

            l = Math.Clamp(l + (escurecer ? -0.04 : 0.04), 0.0, 1.0);
            atual = DeHsl(h, s, l);
        }

        // Limite extremo: preto ou branco puros sempre passam
        return escurecer ? Colors.Black : Colors.White;
    }

    /// <summary>
    /// v1.7.0 (Onda 13): quanto do véu da pílula some acima da curva do mini gráfico (75%).
    ///
    /// Por que apagar em vez de pintar: as cores do texto já estão calibradas no limite do contraste
    /// sobre o véu da pílula. Um gráfico na cor do número, pintado POR CIMA do véu, derrubava o
    /// contraste (medido: sobra 1–3% de opacidade para ele; com 18% nem o texto branco passava na
    /// barra escura — 3,74:1). Apagar o véu leva o fundo para a cor da barra, que só AUMENTA o
    /// contraste: abaixo da curva o fundo é o de sempre, acima é mais neutro. Nenhuma cor muda.
    /// </summary>
    public const byte AlfaMascaraGrafico = 0xC0;

    /// <summary>
    /// Clareia (barra escura) ou escurece (barra clara) a cor em passos de 4% de luminosidade HSL
    /// até o texto atingir <paramref name="minimo"/> sobre o fundo da pílula. O fundo da pílula é
    /// recalculado a cada passo, porque ele é um véu translúcido da própria cor.
    /// </summary>
    public static Color GarantirContraste(Color texto, Color barra, byte alfaFundo, double minimo, bool escurecer)
    {
        (double h, double s, double l) = ParaHsl(texto);
        Color atual = texto;
        for (int passo = 0; passo < 30; passo++)
        {
            if (RazaoContraste(atual, Misturar(barra, atual, alfaFundo)) >= minimo)
            {
                return atual;
            }

            l = Math.Clamp(l + (escurecer ? -0.04 : 0.04), 0.0, 1.0);
            atual = DeHsl(h, s, l);
        }

        // Limite extremo: preto ou branco puros sempre passam
        return escurecer ? Colors.Black : Colors.White;
    }

    // ---------------------------------------------------------------- WCAG 2.1

    /// <summary>Luminância relativa (WCAG 2.1, sRGB linearizado).</summary>
    public static double Luminancia(Color c)
    {
        static double Canal(byte v)
        {
            double x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Canal(c.R) + 0.7152 * Canal(c.G) + 0.0722 * Canal(c.B);
    }

    /// <summary>Razão de contraste WCAG entre duas cores opacas (1:1 a 21:1).</summary>
    public static double RazaoContraste(Color a, Color b)
    {
        double la = Luminancia(a);
        double lb = Luminancia(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Rótulo do design: "AAA" (≥7), "AA" (≥4,5), "AA texto grande" (≥3) ou "Insuficiente".</summary>
    public static string NivelWcag(double razao) => razao switch
    {
        >= 7.0 => "AAA",
        >= 4.5 => "AA",
        >= 3.0 => "AA texto grande",
        _ => "Insuficiente"
    };

    /// <summary>Resultado de pintar <paramref name="topo"/> com opacidade <paramref name="alfa"/> sobre <paramref name="fundo"/>.</summary>
    public static Color Misturar(Color fundo, Color topo, byte alfa)
    {
        double a = alfa / 255.0;
        return Color.FromRgb(
            (byte)Math.Round(fundo.R + (topo.R - fundo.R) * a),
            (byte)Math.Round(fundo.G + (topo.G - fundo.G) * a),
            (byte)Math.Round(fundo.B + (topo.B - fundo.B) * a));
    }

    // ---------------------------------------------------------------- utilidades

    public static Color ComAlfa(Color c, byte alfa) => Color.FromArgb(alfa, c.R, c.G, c.B);

    public static string ParaHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color Hex(string hex) => TentarHex(hex, out Color c) ? c : Colors.Magenta;

    /// <summary>Aceita "#RRGGBB" ou "RRGGBB" (sem alfa — a opacidade é do motor de pílulas).</summary>
    public static bool TentarHex(string? texto, out Color cor)
    {
        cor = default;
        if (string.IsNullOrWhiteSpace(texto))
        {
            return false;
        }

        string s = texto.Trim().TrimStart('#');
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
        {
            return false;
        }

        cor = Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    private static (double H, double S, double L) ParaHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, h = 0, s = 0;
        double d = max - min;
        if (d > 1e-9)
        {
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r)
            {
                h = (g - b) / d + (g < b ? 6 : 0);
            }
            else if (max == g)
            {
                h = (b - r) / d + 2;
            }
            else
            {
                h = (r - g) / d + 4;
            }

            h /= 6;
        }

        return (h, s, l);
    }

    private static Color DeHsl(double h, double s, double l)
    {
        static double Matiz(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        double r, g, b;
        if (s < 1e-9)
        {
            r = g = b = l;
        }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = Matiz(p, q, h + 1.0 / 3);
            g = Matiz(p, q, h);
            b = Matiz(p, q, h - 1.0 / 3);
        }

        return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }
}
