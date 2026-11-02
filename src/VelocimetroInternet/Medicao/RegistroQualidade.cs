using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VelocimetroInternet.Medicao;

/// <summary>Estado da conexão observado a cada poucos segundos.</summary>
public enum EstadoInternet
{
    /// <summary>Com internet (ou estado desconhecido — na dúvida, nunca acusa queda).</summary>
    Online,

    /// <summary>Placa conectada ao roteador, mas o Windows diz "sem internet" → problema da operadora/modem.</summary>
    SemInternet,

    /// <summary>Nenhuma placa conectada (Wi-Fi desligado, cabo solto, roteador desligado) → problema local.</summary>
    SemRede
}

/// <summary>Um teste de velocidade guardado (velocidades em BITS por segundo, como a operadora vende).</summary>
/// <param name="Conexao">"Cabo", "Wi-Fi" ou o nome da placa — a Anatel considera a medição por cabo.</param>
/// <param name="Rede">
/// v1.5.0 (Onda 10): link, sinal e banda no momento do teste. Null nos testes gravados pela v1.4.0
/// (o JSON antigo não tem o campo e continua sendo lido — o formato segue na versão 1).
/// </param>
public sealed record TesteGuardado(
    DateTime Quando, double DownloadBps, double UploadBps, double LatenciaMs, string Conexao, string Servidor,
    ConexaoNoTeste? Rede = null)
{
    /// <summary>Os dados de conexão do teste; nos testes antigos, só o tipo ("Cabo"/"Wi-Fi").</summary>
    [JsonIgnore] // calculado: não vai para o arquivo
    public ConexaoNoTeste ConexaoCompleta => Rede ?? new ConexaoNoTeste(Conexao);
}

/// <summary>Um período sem internet (ou sem rede) com começo e fim.</summary>
public sealed record Queda(DateTime Inicio, DateTime Fim, EstadoInternet Tipo)
{
    public TimeSpan Duracao => Fim - Inicio;
}

/// <summary>
/// v1.4.0 (Onda 9): matéria-prima do "relatório para reclamar com a operadora".
///
/// Decisão do dono (2026-11-01): NADA de teste automático em segundo plano (gastaria a banda e a
/// franquia). Então o relatório junta:
/// - os testes de velocidade que o PRÓPRIO usuário fez (cada um guardado aqui);
/// - as quedas de internet, observadas de graça pelo estado de conectividade do Windows (o mesmo
///   "Sem internet" do ícone de rede — o Windows já testa isso sozinho, o app só lê);
/// - o tempo em que o app estava de fato observando (PC desligado/suspenso/pausado não conta —
///   o relatório diz isso, para não acusar a operadora de tempo que ninguém mediu).
///
/// Classe pura (sem disco e sem relógio próprio): quem chama passa o "agora". Não é thread-safe —
/// o <see cref="ServicoQualidade"/> protege com trava.
/// </summary>
public sealed class RegistroQualidade
{
    /// <summary>Dias guardados (o mês atual + o mesmo mês do ano anterior, como o consumo).</summary>
    public const int DiasMantidos = 400;

    /// <summary>Quedas mais curtas que isto são descartadas (piscadas do Wi-Fi, o Windows reavaliando a rede).</summary>
    public static readonly TimeSpan DuracaoMinimaQueda = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Intervalo máximo entre duas observações para o tempo contar como "observado". Acima disto
    /// o app não estava rodando (PC suspenso, medição pausada): o tempo do buraco não conta e uma
    /// queda aberta é fechada no último instante em que foi vista.
    /// </summary>
    public static readonly TimeSpan LacunaMaxima = TimeSpan.FromSeconds(30);

    private readonly List<TesteGuardado> _testes = [];
    private readonly List<Queda> _quedas = [];
    private readonly SortedDictionary<DateOnly, double> _segundosObservados = [];

    // Observação em curso (só memória: se o app fechar com uma queda aberta, ela é fechada no
    // encerramento; se o processo morrer, perde-se só essa queda)
    private DateTime? _ultimaObservacao;
    private EstadoInternet _estadoAberto = EstadoInternet.Online;
    private DateTime _inicioAberto;

    public IReadOnlyList<TesteGuardado> Testes => _testes;

    public IReadOnlyList<Queda> Quedas => _quedas;

    public IReadOnlyDictionary<DateOnly, double> SegundosObservados => _segundosObservados;

    /// <summary>Queda em andamento agora (para o relatório do mês atual mostrar), ou null.</summary>
    public Queda? QuedaAberta(DateTime agora) =>
        _estadoAberto != EstadoInternet.Online && _ultimaObservacao is not null
            ? new Queda(_inicioAberto, agora, _estadoAberto)
            : null;

    // ------------------------------------------------------------------ observação

    /// <summary>
    /// Registra o estado visto em <paramref name="agora"/>. Devolve true se uma queda acabou de ser
    /// FECHADA e guardada (o serviço aproveita para gravar no disco).
    /// </summary>
    public bool Observar(DateTime agora, EstadoInternet estado)
    {
        bool guardou = false;

        if (_ultimaObservacao is DateTime anterior)
        {
            TimeSpan passo = agora - anterior;
            if (passo > TimeSpan.Zero && passo <= LacunaMaxima)
            {
                // Tempo observado vai para o dia do instante ATUAL (passos de segundos: a virada
                // da meia-noite erra no máximo alguns segundos de um dia para o outro)
                DateOnly dia = DateOnly.FromDateTime(agora);
                _segundosObservados.TryGetValue(dia, out double s);
                _segundosObservados[dia] = s + passo.TotalSeconds;
            }
            else
            {
                // Buraco (suspensão, pausa) ou relógio voltou: o que estava aberto termina no
                // último instante visto — ninguém sabe o que houve no buraco
                guardou |= FecharAberta(anterior);
            }
        }

        if (estado != _estadoAberto)
        {
            // Mudou: fecha a queda anterior (se havia) e, se o novo estado é ruim, abre outra
            guardou |= FecharAberta(agora);
            if (estado != EstadoInternet.Online)
            {
                _estadoAberto = estado;
                _inicioAberto = agora;
            }
        }

        _ultimaObservacao = agora;
        return guardou;
    }

    /// <summary>App fechando / medição pausada: fecha a queda aberta no último instante observado.</summary>
    public bool Encerrar()
    {
        bool guardou = _ultimaObservacao is DateTime ultimo && FecharAberta(ultimo);
        _ultimaObservacao = null;
        return guardou;
    }

    private bool FecharAberta(DateTime fim)
    {
        if (_estadoAberto == EstadoInternet.Online)
        {
            return false;
        }

        var q = new Queda(_inicioAberto, fim, _estadoAberto);
        _estadoAberto = EstadoInternet.Online;
        if (q.Duracao < DuracaoMinimaQueda)
        {
            return false;
        }

        _quedas.Add(q);
        return true;
    }

    // ------------------------------------------------------------------ testes

    public void AdicionarTeste(TesteGuardado t) => _testes.Add(t);

    /// <summary>Descarta o que tem mais de <see cref="DiasMantidos"/> dias.</summary>
    public void Podar(DateTime agora)
    {
        DateTime limite = agora.Date.AddDays(-DiasMantidos);
        _testes.RemoveAll(t => t.Quando < limite);
        _quedas.RemoveAll(q => q.Fim < limite);
        foreach (DateOnly d in _segundosObservados.Keys.Where(d => d < DateOnly.FromDateTime(limite)).ToList())
        {
            _segundosObservados.Remove(d);
        }
    }

    // ------------------------------------------------------------------ JSON

    private sealed record ArquivoGravado(
        int Versao, List<TesteGuardado> Testes, List<Queda> Quedas, Dictionary<string, double> SegundosObservados);

    public string ParaJson() => JsonSerializer.Serialize(new ArquivoGravado(
        1,
        _testes,
        _quedas,
        _segundosObservados.ToDictionary(p => p.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), p => p.Value)));

    /// <summary>Lê o JSON gravado; dia com data inválida (arquivo editado à mão) é ignorado.</summary>
    public static RegistroQualidade DeJson(string json)
    {
        var r = new RegistroQualidade();
        ArquivoGravado? a = JsonSerializer.Deserialize<ArquivoGravado>(json);
        r._testes.AddRange((a?.Testes ?? []).Where(t => t is not null));
        r._quedas.AddRange((a?.Quedas ?? []).Where(q => q is not null && q.Fim >= q.Inicio));
        foreach ((string dia, double s) in a?.SegundosObservados ?? [])
        {
            if (DateOnly.TryParseExact(dia, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly d)
                && double.IsFinite(s) && s > 0)
            {
                r._segundosObservados[d] = s;
            }
        }

        return r;
    }
}

/// <summary>
/// v1.4.0: dono do <see cref="RegistroQualidade"/> em produção — trava (o loop de medição observa
/// numa thread do pool; o teste e o relatório usam a thread da interface) e disco
/// (%LocalAppData%\VelocimetroInternet\qualidade.json, gravação atômica .tmp → troca).
/// </summary>
public sealed class ServicoQualidade
{
    /// <summary>Gravar o tempo observado a cada 5 min (no máximo 5 min de "tempo observado" perdidos numa queda de energia).</summary>
    private static readonly TimeSpan IntervaloGravacao = TimeSpan.FromMinutes(5);

    private readonly object _trava = new();
    private readonly string _caminho;
    private readonly Func<DateTime> _agora;
    private RegistroQualidade _registro = new();
    private DateTime _ultimaGravacao;

    /// <param name="caminho">Arquivo (sobrescrito nos testes).</param>
    /// <param name="agora">Relógio injetável (testes).</param>
    public ServicoQualidade(string? caminho = null, Func<DateTime>? agora = null)
    {
        _caminho = caminho ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VelocimetroInternet", "qualidade.json");
        _agora = agora ?? (() => DateTime.Now);
        _ultimaGravacao = _agora();
        try
        {
            if (File.Exists(_caminho))
            {
                _registro = RegistroQualidade.DeJson(File.ReadAllText(_caminho));
                _registro.Podar(_agora());
            }
        }
        catch
        {
            // Corrompido: recomeça do zero (o relatório é um extra; nunca pode impedir o medidor)
            _registro = new RegistroQualidade();
        }
    }

    /// <summary>Chamado pelo loop de medição a cada poucos segundos.</summary>
    public void Observar(EstadoInternet estado)
    {
        lock (_trava)
        {
            DateTime agora = _agora();
            bool fechouQueda = _registro.Observar(agora, estado);
            if (fechouQueda || agora - _ultimaGravacao >= IntervaloGravacao)
            {
                GravarSemTrava(agora);
            }
        }
    }

    /// <summary>Medição pausada ou app fechando: fecha a queda aberta e grava.</summary>
    public void Encerrar()
    {
        lock (_trava)
        {
            _registro.Encerrar();
            GravarSemTrava(_agora());
        }
    }

    /// <summary>Guarda um teste de velocidade concluído (gravado na hora).</summary>
    public void AdicionarTeste(TesteGuardado t)
    {
        lock (_trava)
        {
            _registro.AdicionarTeste(t);
            GravarSemTrava(_agora());
        }
    }

    /// <summary>Resumo de um mês (cópia — a interface lê sem trava).</summary>
    public ResumoMes Resumir(int ano, int mes, double planoMbps)
    {
        lock (_trava)
        {
            DateTime agora = _agora();
            return CalculoRelatorio.Resumir(_registro, ano, mes, planoMbps, _registro.QuedaAberta(agora));
        }
    }

    /// <summary>Meses com algum dado (o mais recente primeiro), sempre incluindo o mês atual.</summary>
    public List<(int Ano, int Mes)> MesesComDados()
    {
        lock (_trava)
        {
            return CalculoRelatorio.MesesComDados(_registro, _agora());
        }
    }

    private void GravarSemTrava(DateTime agora)
    {
        _ultimaGravacao = agora;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
            string tmp = _caminho + ".tmp";
            File.WriteAllText(tmp, _registro.ParaJson());
            File.Move(tmp, _caminho, overwrite: true);
        }
        catch
        {
            // Silencioso: disco cheio/sem permissão não pode derrubar o medidor
        }
    }
}
