using System.Globalization;
using System.Text;
using System.Text.Json;

namespace VelocimetroInternet.Medicao;

/// <summary>Bytes de um dia: enviados (↑) e recebidos (↓).</summary>
public readonly record struct ConsumoDia(ulong Enviados, ulong Recebidos)
{
    public ulong Total => Enviados + Recebidos;
}

/// <summary>
/// v1.0.0 (Onda 5): consumo POR DIA, guardado por ~13 meses. É a base de três ideias do
/// 04-IDEIAS.md de uma vez: "consumo do mês", "alerta de franquia" e "histórico com exportação
/// CSV" (no lugar do SQLite previsto — um JSON de ~400 linhas não justifica uma dependência).
///
/// Classe pura (sem disco, sem relógio): o <see cref="HistoricoRede"/> decide quando gravar e
/// qual é o "hoje". Não é thread-safe — quem chama protege com a trava dele.
/// </summary>
public sealed class RegistroConsumo
{
    /// <summary>Dias guardados: 400 cobre o mês atual + o mesmo mês do ano passado.</summary>
    public const int DiasMantidos = 400;

    private readonly SortedDictionary<DateOnly, ConsumoDia> _dias = [];

    /// <summary>
    /// Franquia: maior marco (80 ou 100 %) já avisado em cada mês ("2026-10" → 80). Fica no mesmo
    /// arquivo para o aviso não se repetir a cada reinício do app no mesmo mês.
    /// </summary>
    private readonly Dictionary<string, int> _avisosFranquia = [];

    public IReadOnlyDictionary<DateOnly, ConsumoDia> Dias => _dias;

    /// <summary>Soma bytes ao dia (cria a linha do dia se ainda não existir).</summary>
    public void Somar(DateOnly dia, ulong enviados, ulong recebidos)
    {
        _dias.TryGetValue(dia, out ConsumoDia atual);
        _dias[dia] = new ConsumoDia(atual.Enviados + enviados, atual.Recebidos + recebidos);
    }

    /// <summary>Substitui o valor de um dia (usado na migração do consumo.json antigo).</summary>
    public void Definir(DateOnly dia, ConsumoDia valor) => _dias[dia] = valor;

    public ConsumoDia Do(DateOnly dia) => _dias.TryGetValue(dia, out ConsumoDia c) ? c : default;

    /// <summary>Soma do mês-calendário que contém <paramref name="diaDoMes"/>.</summary>
    public ConsumoDia DoMes(DateOnly diaDoMes)
    {
        ulong env = 0, rec = 0;
        foreach ((DateOnly d, ConsumoDia c) in _dias)
        {
            if (d.Year == diaDoMes.Year && d.Month == diaDoMes.Month)
            {
                env += c.Enviados;
                rec += c.Recebidos;
            }
        }

        return new ConsumoDia(env, rec);
    }

    /// <summary>Descarta dias com mais de <see cref="DiasMantidos"/> (e avisos de meses que já saíram).</summary>
    public void Podar(DateOnly hoje)
    {
        DateOnly limite = hoje.AddDays(-DiasMantidos);
        foreach (DateOnly d in _dias.Keys.Where(d => d < limite).ToList())
        {
            _dias.Remove(d);
        }

        string mesLimite = ChaveMes(limite);
        foreach (string m in _avisosFranquia.Keys.Where(m => string.CompareOrdinal(m, mesLimite) < 0).ToList())
        {
            _avisosFranquia.Remove(m);
        }
    }

    // ------------------------------------------------------------------ franquia

    /// <summary>
    /// Marco de franquia que deve ser avisado AGORA (80 ou 100), ou null. Marca como avisado —
    /// cada marco aparece uma única vez por mês. Franquia ≤ 0 = desligada.
    /// Se o mês já começou acima de 100 % (ex.: franquia definida tarde), avisa só o 100.
    /// </summary>
    public int? MarcoFranquiaParaAvisar(DateOnly hoje, double franquiaGb)
    {
        if (!(franquiaGb > 0) || !double.IsFinite(franquiaGb))
        {
            return null;
        }

        double usado = DoMes(hoje).Total / (franquiaGb * 1_000_000_000d); // GB decimal, como as operadoras
        int marco = usado >= 1.0 ? 100 : usado >= 0.8 ? 80 : 0;
        string mes = ChaveMes(hoje);
        _avisosFranquia.TryGetValue(mes, out int jaAvisado);
        if (marco == 0 || marco <= jaAvisado)
        {
            return null;
        }

        _avisosFranquia[mes] = marco;
        return marco;
    }

    /// <summary>Fração da franquia usada no mês (0 = sem franquia definida).</summary>
    public double FracaoFranquia(DateOnly hoje, double franquiaGb) =>
        franquiaGb > 0 && double.IsFinite(franquiaGb) ? DoMes(hoje).Total / (franquiaGb * 1_000_000_000d) : 0;

    private static string ChaveMes(DateOnly d) => d.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ CSV e JSON

    /// <summary>
    /// CSV para planilha em pt-BR: separador ";", decimal com vírgula, datas ISO. Uma linha por dia,
    /// em MB (decimal) — fácil de somar e de comparar com a fatura.
    /// </summary>
    public string ParaCsv()
    {
        var pt = new CultureInfo("pt-BR");
        var sb = new StringBuilder();
        sb.AppendLine("Data;Enviado (MB);Recebido (MB);Total (MB)");
        foreach ((DateOnly d, ConsumoDia c) in _dias)
        {
            sb.Append(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(';')
              .Append((c.Enviados / 1e6).ToString("0.00", pt)).Append(';')
              .Append((c.Recebidos / 1e6).ToString("0.00", pt)).Append(';')
              .Append((c.Total / 1e6).ToString("0.00", pt)).AppendLine();
        }

        return sb.ToString();
    }

    private sealed record DiaGravado(string Dia, ulong Enviados, ulong Recebidos);

    private sealed record ArquivoGravado(int Versao, List<DiaGravado> Dias, Dictionary<string, int>? AvisosFranquia);

    public string ParaJson() => JsonSerializer.Serialize(new ArquivoGravado(
        1,
        _dias.Select(p => new DiaGravado(p.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), p.Value.Enviados, p.Value.Recebidos)).ToList(),
        new Dictionary<string, int>(_avisosFranquia)));

    /// <summary>Lê o JSON gravado; linhas com data inválida são ignoradas (arquivo editado à mão).</summary>
    public static RegistroConsumo DeJson(string json)
    {
        var r = new RegistroConsumo();
        ArquivoGravado? a = JsonSerializer.Deserialize<ArquivoGravado>(json);
        foreach (DiaGravado d in a?.Dias ?? [])
        {
            if (DateOnly.TryParseExact(d.Dia, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly dia))
            {
                r._dias[dia] = new ConsumoDia(d.Enviados, d.Recebidos);
            }
        }

        foreach ((string mes, int marco) in a?.AvisosFranquia ?? [])
        {
            r._avisosFranquia[mes] = marco;
        }

        return r;
    }
}
