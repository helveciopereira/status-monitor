using System.Globalization;
using System.Text.Json.Serialization;

namespace VelocimetroInternet.Medicao;

/// <summary>
/// v1.5.0 (Onda 10): como o PC estava ligado ao roteador durante um teste de velocidade.
///
/// Por que existe: o teste mede o caminho INTEIRO (PC → roteador → operadora → servidor). Se o
/// trecho PC → roteador (o "link") é mais lento que o plano, nenhum teste passa do link, por melhor
/// que a operadora esteja — e a operadora usa isso para descartar a reclamação. Medido no PC do
/// dono em 2026-11-01: Wi-Fi 5 GHz com link de 432 Mbps num plano de 700 Mbps.
///
/// Classe pura (só dados e textos), gravada junto com cada teste no qualidade.json.
/// </summary>
/// <param name="Tipo">"Cabo", "Wi-Fi", o nome da placa ou "—".</param>
/// <param name="LinkDownMbps">Velocidade do link no sentido do download (Wi-Fi: taxa de recepção; cabo: velocidade da porta).</param>
/// <param name="LinkUpMbps">Velocidade do link no sentido do upload (Wi-Fi: taxa de transmissão).</param>
/// <param name="SinalPct">Qualidade do sinal do Wi-Fi, 0–100 (a mesma do ícone do Windows).</param>
/// <param name="RssiDbm">Potência do sinal do Wi-Fi em dBm (ex.: −67); mais perto de zero = melhor.</param>
/// <param name="Banda">"2,4 GHz", "5 GHz" ou "6 GHz".</param>
public sealed record ConexaoNoTeste(
    string Tipo,
    double? LinkDownMbps = null,
    double? LinkUpMbps = null,
    int? SinalPct = null,
    int? RssiDbm = null,
    string? Banda = null)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>O teste foi feito pelo Wi-Fi.</summary>
    [JsonIgnore] // calculado: não vai para o arquivo
    public bool EhWiFi => string.Equals(Tipo, "Wi-Fi", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// O link do PC com o roteador é mais lento que o plano: o download deste teste NÃO podia chegar
    /// ao contratado, qualquer que fosse a operadora. Sem plano ou sem link conhecido → falso
    /// (na dúvida, não desculpa a operadora).
    /// </summary>
    public bool LimitaODownload(double planoMbps) =>
        planoMbps > 0 && LinkDownMbps is double l && double.IsFinite(l) && l > 0 && l < planoMbps;

    /// <summary>"Wi-Fi 5 GHz · sinal 66 % · link 432 Mbps" / "Cabo · link 1 Gbps" / "Wi-Fi".</summary>
    public string Descricao()
    {
        var partes = new List<string> { Banda is null ? Tipo : $"{Tipo} {Banda}" };
        if (SinalPct is int s)
        {
            partes.Add($"sinal {s.ToString(PtBr)} %");
        }

        if (LinkDownMbps is double l && l > 0)
        {
            partes.Add($"link {Link(l)}");
        }

        return string.Join(" · ", partes);
    }

    /// <summary>"432 Mbps", "1 Gbps", "2,5 Gbps" — taxa de link é número redondo, sem casas inúteis.</summary>
    public static string Link(double mbps) =>
        mbps >= 1000
            ? $"{(mbps / 1000).ToString("0.#", PtBr)} Gbps"
            : $"{Math.Round(mbps).ToString("0", PtBr)} Mbps";

    /// <summary>
    /// Aviso da janela do teste quando o link limita o download; null quando não limita. Vem logo
    /// depois da <see cref="Descricao"/> (que já mostra o link): diz de quem é o limite e o que fazer.
    /// </summary>
    public string? AvisoDeLimite(double planoMbps)
    {
        if (!LimitaODownload(planoMbps))
        {
            return null;
        }

        string plano = $"{planoMbps.ToString("0.##", PtBr)} Mbps";
        return EhWiFi
            ? $"Esse link com o roteador é menor que o plano de {plano}: nenhum teste por este Wi-Fi passa dele. Para comparar com o contratado, teste por cabo."
            : $"Esse link com o roteador é menor que o plano de {plano}: confira o cabo (cabos antigos ficam em 100 Mbps) e a porta do roteador.";
    }
}
