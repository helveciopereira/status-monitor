using System.Windows.Media;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// v0.9.0 (Onda 3, seletor visual de cor): conversão RGB ⇄ HSV, pura e testável.
/// H em graus [0, 360), S e V em [0, 1]. É o modelo do quadrado do seletor:
/// eixo X = saturação, eixo Y = brilho (V), barra de baixo = matiz (H).
/// </summary>
public readonly record struct CorHsv(double H, double S, double V)
{
    /// <summary>Converte uma cor RGB (o alfa é ignorado) para HSV.</summary>
    public static CorHsv De(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0;
        if (delta > 0)
        {
            // Qual canal é o maior decide o "setor" de 60° do círculo de matizes
            if (max == r)
            {
                h = 60 * (((g - b) / delta) % 6);
            }
            else if (max == g)
            {
                h = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                h = 60 * (((r - g) / delta) + 4);
            }
        }

        if (h < 0)
        {
            h += 360;
        }

        double s = max <= 0 ? 0 : delta / max;
        return new CorHsv(h, s, max);
    }

    /// <summary>Converte de volta para RGB opaco (valores fora da faixa são limitados).</summary>
    public Color ParaCor()
    {
        double h = double.IsFinite(H) ? ((H % 360) + 360) % 360 : 0;
        double s = Math.Clamp(double.IsFinite(S) ? S : 0, 0, 1);
        double v = Math.Clamp(double.IsFinite(V) ? V : 0, 0, 1);

        double croma = v * s;
        double x = croma * (1 - Math.Abs((h / 60 % 2) - 1));
        double m = v - croma;

        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (croma, x, 0.0),
            1 => (x, croma, 0.0),
            2 => (0.0, croma, x),
            3 => (0.0, x, croma),
            4 => (x, 0.0, croma),
            _ => (croma, 0.0, x)
        };

        return Color.FromRgb(Byte(r + m), Byte(g + m), Byte(b + m));
    }

    /// <summary>Matiz puro (S = V = 1) — o fundo do quadrado do seletor.</summary>
    public static Color MatizPuro(double h) => new CorHsv(h, 1, 1).ParaCor();

    private static byte Byte(double canal) => (byte)Math.Clamp(Math.Round(canal * 255), 0, 255);
}
