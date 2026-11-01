using System.Globalization;

namespace VelocimetroInternet.Medicao;

/// <summary>Onde um teste cai em relação às metas da Anatel para banda larga fixa.</summary>
public enum FaixaAnatel
{
    /// <summary>Plano não informado: não dá para comparar.</summary>
    SemPlano,

    /// <summary>≥ 80 % do contratado.</summary>
    Dentro,

    /// <summary>Entre 40 % e 80 %: passa no mínimo instantâneo, puxa a média para baixo.</summary>
    AbaixoDaMedia,

    /// <summary>&lt; 40 %: abaixo do mínimo instantâneo.</summary>
    AbaixoDoMinimo
}

/// <summary>Tudo o que o relatório de um mês mostra (cópia imutável, montada sob a trava do serviço).</summary>
public sealed record ResumoMes(
    int Ano,
    int Mes,
    double PlanoMbps,
    IReadOnlyList<TesteGuardado> Testes,
    double? MediaDownloadBps,
    double? MediaUploadBps,
    double? FracaoMedia,
    int TestesAbaixoDoMinimo,
    int TestesAbaixoDaMedia,
    int TestesPorWiFi,
    IReadOnlyList<Queda> QuedasInternet,
    IReadOnlyList<Queda> QuedasRede,
    TimeSpan TempoSemInternet,
    TimeSpan TempoSemRede,
    TimeSpan TempoObservado,
    int DiasObservados,
    double? Disponibilidade,
    IReadOnlyList<string> Conclusoes)
{
    /// <summary>"novembro de 2026".</summary>
    public string NomeMes => CalculoRelatorio.NomeMes(Ano, Mes);
}

/// <summary>
/// v1.4.0 (Onda 9): contas do relatório para a operadora — puras, testáveis.
///
/// Referência usada (a mesma do teste de velocidade desde a v1.2.0): metas da Anatel para banda
/// larga fixa — em cada medição pelo menos 40 % do contratado e, na MÉDIA do mês, 80 %.
/// O relatório é um registro do usuário, não a medição oficial; ele diz isso no rodapé.
/// </summary>
public static class CalculoRelatorio
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public const double MinimoInstantaneo = 0.4;
    public const double MinimoMedia = 0.8;

    public static FaixaAnatel Classificar(double downloadBps, double planoMbps)
    {
        if (!(planoMbps > 0) || !double.IsFinite(downloadBps))
        {
            return FaixaAnatel.SemPlano;
        }

        double f = downloadBps / (planoMbps * 1_000_000);
        return f >= MinimoMedia ? FaixaAnatel.Dentro
            : f >= MinimoInstantaneo ? FaixaAnatel.AbaixoDaMedia
            : FaixaAnatel.AbaixoDoMinimo;
    }

    /// <summary>Monta o resumo do mês. <paramref name="quedaAberta"/> = queda ainda em andamento (entra até "agora").</summary>
    public static ResumoMes Resumir(RegistroQualidade r, int ano, int mes, double planoMbps, Queda? quedaAberta = null)
    {
        var inicioMes = new DateTime(ano, mes, 1);
        DateTime fimMes = inicioMes.AddMonths(1);

        List<TesteGuardado> testes = [.. r.Testes.Where(t => t.Quando >= inicioMes && t.Quando < fimMes).OrderBy(t => t.Quando)];

        // Quedas que TOCAM o mês, recortadas nas bordas (uma queda de 31/10 23:50 a 01/11 00:30
        // conta 30 min em novembro)
        IEnumerable<Queda> todas = quedaAberta is null ? r.Quedas : r.Quedas.Append(quedaAberta);
        List<Queda> doMes = [.. todas
            .Where(q => q.Fim > inicioMes && q.Inicio < fimMes)
            .Select(q => new Queda(Max(q.Inicio, inicioMes), Min(q.Fim, fimMes), q.Tipo))
            .Where(q => q.Duracao > TimeSpan.Zero)
            .OrderBy(q => q.Inicio)];
        List<Queda> internet = [.. doMes.Where(q => q.Tipo == EstadoInternet.SemInternet)];
        List<Queda> rede = [.. doMes.Where(q => q.Tipo == EstadoInternet.SemRede)];
        TimeSpan semInternet = Soma(internet);
        TimeSpan semRede = Soma(rede);

        double segundos = 0;
        int dias = 0;
        foreach ((DateOnly d, double s) in r.SegundosObservados)
        {
            if (d.Year == ano && d.Month == mes)
            {
                segundos += s;
                dias++;
            }
        }

        TimeSpan observado = TimeSpan.FromSeconds(segundos);
        // Disponibilidade = parte do tempo OBSERVADO com o roteador conectado em que havia internet
        // (tempo sem rede local é culpa da casa, sai da conta nos dois lados)
        double baseTempo = segundos - semRede.TotalSeconds;
        double? disponibilidade = baseTempo > 0 ? Math.Clamp(1 - semInternet.TotalSeconds / baseTempo, 0, 1) : null;

        double? mediaDown = testes.Count > 0 ? testes.Average(t => t.DownloadBps) : null;
        double? mediaUp = testes.Count > 0 ? testes.Average(t => t.UploadBps) : null;
        double? fracao = mediaDown is double md && planoMbps > 0 ? md / (planoMbps * 1_000_000) : null;
        int abaixoMin = testes.Count(t => Classificar(t.DownloadBps, planoMbps) == FaixaAnatel.AbaixoDoMinimo);
        int abaixoMedia = testes.Count(t => Classificar(t.DownloadBps, planoMbps) == FaixaAnatel.AbaixoDaMedia);
        int wifi = testes.Count(t => !string.Equals(t.Conexao, "Cabo", StringComparison.OrdinalIgnoreCase));

        List<string> conclusoes = Concluir(testes.Count, planoMbps, mediaDown, fracao, abaixoMin, wifi, internet.Count, semInternet, observado);

        return new ResumoMes(ano, mes, planoMbps, testes, mediaDown, mediaUp, fracao, abaixoMin, abaixoMedia, wifi,
            internet, rede, semInternet, semRede, observado, dias, disponibilidade, conclusoes);
    }

    /// <summary>Frases do quadro "Conclusão", na ordem em que importam para a reclamação.</summary>
    private static List<string> Concluir(
        int nTestes, double planoMbps, double? mediaDown, double? fracao, int abaixoMin, int wifi,
        int nQuedas, TimeSpan semInternet, TimeSpan observado)
    {
        var c = new List<string>();
        if (nTestes == 0)
        {
            c.Add("Nenhum teste de velocidade neste mês. Para o relatório ter valor, faça testes em dias e horários diferentes (manhã, tarde e noite), de preferência por cabo.");
        }
        else if (fracao is null)
        {
            c.Add($"Média de {nTestes} teste(s): {Mbps(mediaDown!.Value)} de download. Informe a velocidade do plano no estúdio (Medição) para comparar com o contratado.");
        }
        else
        {
            string pct = Pct(fracao.Value);
            c.Add(fracao.Value >= MinimoMedia
                ? $"A média de {nTestes} teste(s) foi {Mbps(mediaDown!.Value)} — {pct} do plano de {planoMbps:0} Mbps, dentro dos 80 % exigidos na média do mês."
                : $"A média de {nTestes} teste(s) foi {Mbps(mediaDown!.Value)} — {pct} do plano de {planoMbps:0} Mbps, ABAIXO dos 80 % exigidos na média do mês.");
            if (abaixoMin > 0)
            {
                c.Add($"{abaixoMin} de {nTestes} teste(s) ficaram abaixo do mínimo de 40 % em qualquer medição ({Mbps(planoMbps * 1_000_000 * MinimoInstantaneo)}).");
            }
        }

        if (observado > TimeSpan.Zero)
        {
            c.Add(nQuedas == 0
                ? $"Nenhuma queda de internet de 1 minuto ou mais nas {Duracao(observado)} observadas."
                : $"{nQuedas} queda(s) de internet com o roteador conectado, somando {Duracao(semInternet)} nas {Duracao(observado)} observadas.");
        }

        if (wifi > 0)
        {
            c.Add($"{wifi} de {nTestes} teste(s) foram por Wi-Fi — a operadora pode atribuir a lentidão ao Wi-Fi. Testes por cabo têm mais peso numa reclamação.");
        }

        return c;
    }

    /// <summary>Meses com testes, quedas ou tempo observado (o mais recente primeiro); o mês atual sempre entra.</summary>
    public static List<(int Ano, int Mes)> MesesComDados(RegistroQualidade r, DateTime agora)
    {
        var meses = new SortedSet<(int, int)> { (agora.Year, agora.Month) };
        foreach (TesteGuardado t in r.Testes)
        {
            meses.Add((t.Quando.Year, t.Quando.Month));
        }

        foreach (Queda q in r.Quedas)
        {
            meses.Add((q.Inicio.Year, q.Inicio.Month));
        }

        foreach (DateOnly d in r.SegundosObservados.Keys)
        {
            meses.Add((d.Year, d.Month));
        }

        return [.. meses.Reverse()];
    }

    // ------------------------------------------------------------------ formatação (pt-BR)

    /// <summary>"novembro de 2026".</summary>
    public static string NomeMes(int ano, int mes) =>
        new DateTime(ano, mes, 1).ToString("MMMM 'de' yyyy", PtBr);

    /// <summary>"512 Mbps" / "1,2 Gbps".</summary>
    public static string Mbps(double bps)
    {
        (string n, string u) = CalculoTeste.FormatarMbps(bps);
        return $"{n} {u}";
    }

    /// <summary>Percentual arredondado PARA BAIXO ("79 %" nunca vira "80 %" e passa na meta por arredondamento).</summary>
    public static string Pct(double fracao) => $"{Math.Floor(fracao * 100).ToString("0", PtBr)} %";

    /// <summary>
    /// "45 s", "12 min", "2 h 05 min", "179 h 59 min". Sem "dias": "7 d 11 h observadas" se lê
    /// mal, e horas somadas são o que a pessoa compara ("3 h sem internet em 180 h").
    /// </summary>
    public static string Duracao(TimeSpan d)
    {
        if (d < TimeSpan.FromMinutes(1))
        {
            return $"{Math.Max(0, (int)d.TotalSeconds)} s";
        }

        if (d < TimeSpan.FromHours(1))
        {
            return $"{(int)d.TotalMinutes} min";
        }

        return $"{(int)d.TotalHours} h {d.Minutes:00} min";
    }

    private static TimeSpan Soma(IEnumerable<Queda> qs) => qs.Aggregate(TimeSpan.Zero, (s, q) => s + q.Duracao);

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
