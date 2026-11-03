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
    IReadOnlyList<string> Conclusoes,
    double? MediaCaboBps = null,
    double? MediaWiFiBps = null,
    int TestesLimitadosPeloLink = 0)
{
    // v1.5.0 (Onda 10): MediaCaboBps/MediaWiFiBps = média do download só dos testes por cabo / só dos
    // demais (Wi-Fi ou placa desconhecida); TestesLimitadosPeloLink = testes em que o link do PC com o
    // roteador era mais lento que o plano (o resultado não podia chegar ao contratado).

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
        List<TesteGuardado> porCabo = [.. testes.Where(EhCabo)];
        List<TesteGuardado> porWiFi = [.. testes.Where(t => !EhCabo(t))];
        int wifi = porWiFi.Count;

        // v1.5.0 (Onda 10): separa cabo de Wi-Fi e acha os testes que o link não deixava chegar ao plano
        double? mediaCabo = porCabo.Count > 0 ? porCabo.Average(t => t.DownloadBps) : null;
        double? mediaWiFi = porWiFi.Count > 0 ? porWiFi.Average(t => t.DownloadBps) : null;
        List<TesteGuardado> limitados = [.. testes.Where(t => t.ConexaoCompleta.LimitaODownload(planoMbps))];

        List<string> conclusoes = Concluir(testes.Count, planoMbps, mediaDown, fracao, abaixoMin, wifi, internet.Count, semInternet, observado);
        ConcluirConexao(conclusoes, testes, planoMbps, porCabo.Count, mediaCabo, wifi, mediaWiFi, limitados);

        return new ResumoMes(ano, mes, planoMbps, testes, mediaDown, mediaUp, fracao, abaixoMin, abaixoMedia, wifi,
            internet, rede, semInternet, semRede, observado, dias, disponibilidade, conclusoes,
            mediaCabo, mediaWiFi, limitados.Count);
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

        // (as frases sobre cabo × Wi-Fi saem do ConcluirConexao, v1.5.0)
        return c;
    }

    private static bool EhCabo(TesteGuardado t) => string.Equals(t.Conexao, "Cabo", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// v1.5.0 (Onda 10): frases sobre COMO os testes foram feitos — média por cabo × por Wi-Fi e, o mais
    /// importante, quantos testes não podiam chegar ao plano porque o link do PC com o roteador era mais
    /// lento que ele (ex.: Wi-Fi a 432 Mbps num plano de 700). Sem dados de link (testes da v1.4.0),
    /// mantém o aviso genérico de Wi-Fi.
    /// </summary>
    private static void ConcluirConexao(
        List<string> c, List<TesteGuardado> testes, double planoMbps, int nCabo, double? mediaCabo, int nWiFi, double? mediaWiFi,
        List<TesteGuardado> limitados)
    {
        int nTestes = testes.Count; // v1.8.1: a lista inteira entra para conferir o link de cada teste por Wi-Fi
        if (nCabo > 0 && nWiFi > 0)
        {
            c.Add(planoMbps > 0
                ? $"Por cabo: média {Mbps(mediaCabo!.Value)} ({Pct(mediaCabo.Value / (planoMbps * 1_000_000))} do plano) em {nCabo} teste(s). Por Wi-Fi: {Mbps(mediaWiFi!.Value)} ({Pct(mediaWiFi.Value / (planoMbps * 1_000_000))}) em {nWiFi}."
                : $"Por cabo: média {Mbps(mediaCabo!.Value)} em {nCabo} teste(s). Por Wi-Fi: {Mbps(mediaWiFi!.Value)} em {nWiFi}.");
        }

        if (limitados.Count > 0)
        {
            List<double> links = [.. limitados.Select(t => t.ConexaoCompleta.LinkDownMbps!.Value)];
            double min = links.Min(), max = links.Max();
            // v1.9.0: pt-BR explícito — o relatório vai para a operadora, em português, qualquer que seja o idioma da interface
            string faixa = ConexaoNoTeste.Link(min, PtBr) == ConexaoNoTeste.Link(max, PtBr)
                ? ConexaoNoTeste.Link(min, PtBr)
                : $"entre {ConexaoNoTeste.Link(min, PtBr)} e {ConexaoNoTeste.Link(max, PtBr)}";
            bool todosWiFi = limitados.All(t => t.ConexaoCompleta.EhWiFi);
            c.Add($"{limitados.Count} de {nTestes} teste(s) foram feitos com o computador ligado ao roteador a {faixa}{(todosWiFi ? " pelo Wi-Fi" : "")}, abaixo do plano de {planoMbps:0} Mbps: nesses, o resultado não podia passar do link, qualquer que fosse a operadora. Para a reclamação valer, repita os testes por cabo.");
        }
        else if (nWiFi > 0)
        {
            // v1.8.1 (Onda 14): se TODOS os testes por Wi-Fi do mês tinham o link conhecido e acima do
            // plano, o Wi-Fi comprovadamente não limitou — a frase diz isso em vez do aviso genérico
            // (achado no teste real de 2026-11-02: Wi-Fi 7 a 2,4 Gbps num plano de 700). Qualquer teste
            // sem link (v1.4.0) ou sem plano → aviso genérico de sempre.
            List<TesteGuardado> wifi = [.. testes.Where(t => t.ConexaoCompleta.EhWiFi)];
            bool todosAcimaDoPlano = planoMbps > 0 && wifi.Count == nWiFi && wifi.Count > 0
                && wifi.All(t => t.ConexaoCompleta.LinkDownMbps is double l && double.IsFinite(l) && l >= planoMbps);
            if (todosAcimaDoPlano)
            {
                List<double> links = [.. wifi.Select(t => t.ConexaoCompleta.LinkDownMbps!.Value)];
                double min = links.Min(), max = links.Max();
                string faixa = ConexaoNoTeste.Link(min, PtBr) == ConexaoNoTeste.Link(max, PtBr)
                    ? $"de {ConexaoNoTeste.Link(min, PtBr)}"
                    : $"entre {ConexaoNoTeste.Link(min, PtBr)} e {ConexaoNoTeste.Link(max, PtBr)}";
                c.Add(nWiFi == nTestes
                    ? $"Todos os {nTestes} teste(s) foram por Wi-Fi, com link {faixa}, acima do plano de {planoMbps:0} Mbps: o Wi-Fi não limitou estes testes."
                    : $"{nWiFi} de {nTestes} teste(s) foram por Wi-Fi, com link {faixa}, acima do plano de {planoMbps:0} Mbps: o Wi-Fi não limitou esses testes.");
            }
            else
            {
                c.Add($"{nWiFi} de {nTestes} teste(s) foram por Wi-Fi — a operadora pode atribuir a lentidão ao Wi-Fi. Testes por cabo têm mais peso numa reclamação.");
            }
        }
    }

    // ------------------------------------------------------------------ v1.8.1 (Onda 14): aviso de volta e CSV

    /// <summary>
    /// Texto da notificação quando uma queda termina: "A internet voltou após 12 min" (queda da
    /// operadora, roteador conectado) ou "A rede voltou após 3 min" (nenhuma placa conectada — Wi-Fi
    /// desligado, cabo solto, roteador reiniciando).
    /// </summary>
    public static string TextoVolta(Queda q) =>
        q.Tipo == EstadoInternet.SemRede
            ? $"A rede voltou após {Duracao(q.Duracao)} sem conexão (das {Hora(q.Inicio)} às {Hora(q.Fim)})."
            : $"A internet voltou após {Duracao(q.Duracao)} (das {Hora(q.Inicio)} às {Hora(q.Fim)}).";

    /// <summary>
    /// Testes e quedas do mês em CSV para anexar à reclamação — mesmo formato do CSV de consumo
    /// (v1.0.0): separador ";", vírgula decimal, datas ISO (o Excel em português abre direto). Um
    /// arquivo só, com dois blocos (testes, depois quedas) separados por linha em branco; cada bloco
    /// tem o próprio cabeçalho. Velocidades em Mbps, como a operadora vende.
    /// </summary>
    public static string ParaCsv(ResumoMes r)
    {
        var sb = new System.Text.StringBuilder();
        string plano = r.PlanoMbps > 0 ? $"plano de {r.PlanoMbps.ToString("0.##", PtBr)} Mbps" : "plano não informado";
        sb.Append("Status Monitor;").Append(r.NomeMes).Append(';').Append(plano).AppendLine();
        sb.AppendLine();

        sb.AppendLine("Testes de velocidade");
        sb.AppendLine("Data;Hora;Download (Mbps);Upload (Mbps);Latência (ms);Conexão;Link (Mbps);Sinal (%);Banda;Servidor;Faixa Anatel");
        foreach (TesteGuardado t in r.Testes)
        {
            ConexaoNoTeste cx = t.ConexaoCompleta;
            sb.Append(t.Quando.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(';')
              .Append(t.Quando.ToString("HH:mm", CultureInfo.InvariantCulture)).Append(';')
              .Append((t.DownloadBps / 1e6).ToString("0.0", PtBr)).Append(';')
              .Append((t.UploadBps / 1e6).ToString("0.0", PtBr)).Append(';')
              .Append(double.IsFinite(t.LatenciaMs) && t.LatenciaMs > 0 ? t.LatenciaMs.ToString("0", PtBr) : "").Append(';')
              .Append(Celula(cx.Tipo)).Append(';')
              .Append(cx.LinkDownMbps is double l && l > 0 ? Math.Round(l).ToString("0", PtBr) : "").Append(';')
              .Append(cx.SinalPct is int s ? s.ToString(PtBr) : "").Append(';')
              .Append(Celula(cx.Banda ?? "")).Append(';')
              .Append(Celula(t.Servidor ?? "")).Append(';')
              .Append(Faixa(Classificar(t.DownloadBps, r.PlanoMbps))).AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("Quedas");
        sb.AppendLine("Início;Fim;Duração (min);Tipo");
        foreach (Queda q in r.QuedasInternet.Concat(r.QuedasRede).OrderBy(q => q.Inicio))
        {
            sb.Append(q.Inicio.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(';')
              .Append(q.Fim.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(';')
              .Append(q.Duracao.TotalMinutes.ToString("0.0", PtBr)).Append(';')
              .Append(q.Tipo == EstadoInternet.SemInternet ? "Sem internet (roteador conectado)" : "Sem rede (placa desconectada)").AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Célula de texto livre: aspas quando tem ";", aspas ou quebra de linha (regra do CSV).</summary>
    private static string Celula(string s) =>
        s.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

    private static string Faixa(FaixaAnatel f) => f switch
    {
        FaixaAnatel.Dentro => "dentro (≥ 80 %)",
        FaixaAnatel.AbaixoDaMedia => "entre 40 % e 80 %",
        FaixaAnatel.AbaixoDoMinimo => "abaixo do mínimo (< 40 %)",
        _ => ""
    };

    private static string Hora(DateTime t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

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

    /// <summary>
    /// v1.6.0 (Onda 11): servidores usados pelos testes do mês, para o relatório dizer com quem se
    /// mediu. Os do fast.com viram um só ("fast.com (Netflix)", qualquer que seja a cidade); os outros
    /// aparecem como foram gravados. Ex.: "fast.com (Netflix) e speed.cloudflare.com". Vazio = sem testes.
    /// </summary>
    public static string ServidoresDosTestes(IEnumerable<TesteGuardado> testes)
    {
        List<string> nomes = [.. testes
            .Select(t => t.Servidor ?? "") // JSON editado à mão pode trazer null
            .Select(s => s.StartsWith("fast.com", StringComparison.Ordinal) ? "fast.com (Netflix)" : s)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
        return nomes.Count <= 1 ? nomes.FirstOrDefault() ?? "" : $"{string.Join(", ", nomes[..^1])} e {nomes[^1]}";
    }

    /// <summary>"novembro de 2026" (pt-BR, o do PDF/CSV). v1.9.0: com cultura, "November 2026" para a janela em inglês.</summary>
    public static string NomeMes(int ano, int mes, CultureInfo? cultura = null)
    {
        CultureInfo c = cultura ?? PtBr;
        return new DateTime(ano, mes, 1).ToString(c.TwoLetterISOLanguageName == "pt" ? "MMMM 'de' yyyy" : "MMMM yyyy", c);
    }

    /// <summary>"512 Mbps" / "1,2 Gbps" — sempre pt-BR (relatório para a operadora).</summary>
    public static string Mbps(double bps)
    {
        (string n, string u) = CalculoTeste.FormatarMbps(bps, PtBr);
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
