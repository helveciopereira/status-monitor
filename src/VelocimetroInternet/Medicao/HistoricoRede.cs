using System.Globalization;
using System.IO;
using System.Text.Json;

namespace VelocimetroInternet.Medicao;

/// <summary>Um ponto da telemetria de 60 s do painel rápido.</summary>
public readonly record struct PontoHistorico(double UploadBps, double DownloadBps);

/// <summary>Instantâneo imutável lido pela interface (sem trava compartilhada com o loop).</summary>
public sealed record InstantaneoHistorico(
    IReadOnlyList<PontoHistorico> Pontos,
    double UploadAtual,
    double DownloadAtual,
    double PicoUpload,
    double PicoDownload,
    ulong BytesEnviadosHoje,
    ulong BytesRecebidosHoje,
    ulong VelocidadeLinkBps,
    string NomeInterface,
    // v0.8.0: quantos pontos cabem nos 60 s com o intervalo atual (o gráfico distribui a
    // largura por ESTE número, não mais por 60 fixo). Padrão 60 = intervalo de 1 s.
    int Capacidade = HistoricoRede.JanelaSegundos,
    // v1.0.0 (Onda 5): consumo do MÊS-calendário (soma dos dias, incluindo hoje)
    ulong BytesEnviadosMes = 0,
    ulong BytesRecebidosMes = 0);

/// <summary>
/// Memória curta do medidor para o "NetTraffic Quick Flyout" do Modelo 3:
/// - últimos 60 s (gráfico "Telemetria em Tempo Real (60s)" e o "Pico" de cada cartão);
/// - consumo acumulado do DIA ("Hoje: ↑ 1,2 GB · ↓ 14,8 GB"), persistido para sobreviver a
///   reinício do app/PC no mesmo dia, e zerado na virada do dia;
/// - v1.0.0: consumo de CADA dia (~13 meses) em consumo-diario.json → consumo do mês, alerta de
///   franquia e exportação CSV (ver <see cref="RegistroConsumo"/>). O consumo.json antigo (só o
///   dia em que foi gravado) é migrado na 1ª leitura.
///
/// Escrito pelo loop de medição (thread do pool) e lido pela UI → tudo sob <c>lock</c>;
/// a UI recebe um <see cref="InstantaneoHistorico"/> copiado.
/// </summary>
public sealed class HistoricoRede
{
    public const int JanelaSegundos = 60;

    /// <summary>Gravar o consumo no disco a cada 60 s de medição (no máximo 1 min perdido numa queda de energia).</summary>
    private const double SegundosEntreGravacoes = 60;

    private readonly object _trava = new();

    // v0.8.0: o anel conta AMOSTRAS, mas representa sempre 60 SEGUNDOS — a capacidade depende do
    // intervalo (0,5 s → 120 pontos; 1 s → 60; 2 s → 30; 5 s → 12). Antes era fixo em 60, o que
    // fazia o "intervalo de atualização" mudar o tamanho da janela do gráfico (item adiado na v0.3.0).
    private PontoHistorico[] _anel = new PontoHistorico[JanelaSegundos];
    private int _inicio;
    private int _quantidade;
    private double _intervaloSegundos = 1.0;
    private double _segundosDesdeGravacao;

    private DateOnly _dia;
    // v1.0.0: o consumo de hoje é a linha de hoje do registro diário (antes: dois campos soltos,
    // zerados na virada do dia — o dia anterior se perdia)
    private RegistroConsumo _registro = new();
    private ulong _linkBps;
    private string _nomeInterface = string.Empty;

    private readonly string _caminhoConsumo;
    private readonly string _caminhoDiario;
    private readonly Func<DateOnly> _hoje;

    /// <summary>Formato antigo (até v0.9.0): só o dia em que foi gravado.</summary>
    private sealed record ConsumoGravado(string Dia, ulong Enviados, ulong Recebidos);

    /// <param name="caminhoConsumo">consumo.json antigo (lido só para migrar). Sobrescrito em testes.</param>
    /// <param name="hoje">Relógio injetável (testes da virada do dia).</param>
    public HistoricoRede(string? caminhoConsumo = null, Func<DateOnly>? hoje = null)
    {
        _caminhoConsumo = caminhoConsumo ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VelocimetroInternet", "consumo.json");
        // Ao lado do consumo.json e com o mesmo prefixo: em produção "consumo-diario.json"; nos
        // testes cada arquivo temporário ganha o seu (sem colisão entre testes em paralelo)
        _caminhoDiario = Path.Combine(
            Path.GetDirectoryName(_caminhoConsumo) ?? string.Empty,
            Path.GetFileNameWithoutExtension(_caminhoConsumo) + "-diario.json");
        _hoje = hoje ?? (() => DateOnly.FromDateTime(DateTime.Now));
        _dia = _hoje();
        CarregarConsumo();
    }

    /// <summary>Pontos que cabem em 60 s com o intervalo dado (mínimo 2, para haver uma linha).</summary>
    public static int CapacidadePara(double intervaloSegundos) =>
        intervaloSegundos > 0 && double.IsFinite(intervaloSegundos)
            ? Math.Max(2, (int)Math.Round(JanelaSegundos / intervaloSegundos))
            : JanelaSegundos;

    /// <summary>
    /// v0.8.0: avisa o intervalo de medição. Se a capacidade muda, o gráfico recomeça vazio —
    /// misturar pontos de 1 s com pontos de 5 s desenharia uma linha do tempo errada.
    /// O consumo do dia NÃO é afetado (ele soma bytes reais, independe do intervalo).
    /// </summary>
    public void DefinirIntervalo(double intervaloSegundos)
    {
        lock (_trava)
        {
            int nova = CapacidadePara(intervaloSegundos);
            _intervaloSegundos = intervaloSegundos > 0 && double.IsFinite(intervaloSegundos) ? intervaloSegundos : 1.0;
            if (nova == _anel.Length)
            {
                return;
            }

            _anel = new PontoHistorico[nova];
            _inicio = 0;
            _quantidade = 0;
        }
    }

    /// <summary>
    /// Registra 1 amostra: velocidades (suavizadas, para o gráfico) e os BYTES REAIS do intervalo
    /// (diferença dos contadores, para o consumo — integrar a velocidade suavizada erraria).
    /// </summary>
    public void Registrar(
        double uploadBps, double downloadBps,
        ulong bytesEnviadosNoIntervalo, ulong bytesRecebidosNoIntervalo,
        string nomeInterface, ulong linkBps)
    {
        lock (_trava)
        {
            DateOnly hoje = _hoje();
            if (hoje != _dia)
            {
                // Virou o dia: o consumo "Hoje" recomeça do zero — o dia anterior fica no registro
                _dia = hoje;
                _registro.Podar(hoje);
            }

            int capacidade = _anel.Length;
            int pos = (_inicio + _quantidade) % capacidade;
            _anel[pos] = new PontoHistorico(Math.Max(0, uploadBps), Math.Max(0, downloadBps));
            if (_quantidade < capacidade)
            {
                _quantidade++;
            }
            else
            {
                _inicio = (_inicio + 1) % capacidade;
            }

            _registro.Somar(_dia, bytesEnviadosNoIntervalo, bytesRecebidosNoIntervalo);
            _linkBps = linkBps;
            _nomeInterface = nomeInterface;

            // Grava no disco a cada minuto (no máximo 60 s de consumo perdidos numa queda de energia).
            // v0.8.0: conta SEGUNDOS, não registros — com intervalo de 0,5 s seriam 2 gravações/min.
            _segundosDesdeGravacao += _intervaloSegundos;
            if (_segundosDesdeGravacao >= SegundosEntreGravacoes)
            {
                _segundosDesdeGravacao = 0;
                GravarSemTrava();
            }
        }
    }

    public InstantaneoHistorico Capturar()
    {
        lock (_trava)
        {
            var pontos = new PontoHistorico[_quantidade];
            double picoUp = 0, picoDown = 0;
            for (int i = 0; i < _quantidade; i++)
            {
                PontoHistorico p = _anel[(_inicio + i) % _anel.Length];
                pontos[i] = p;
                picoUp = Math.Max(picoUp, p.UploadBps);
                picoDown = Math.Max(picoDown, p.DownloadBps);
            }

            PontoHistorico ultimo = _quantidade > 0 ? pontos[^1] : default;
            // Virada do dia SEM medição (pausa, sem rede): "hoje" já é o dia novo, ainda vazio
            DateOnly hoje = _hoje();
            ConsumoDia dia = _registro.Do(hoje);
            ConsumoDia mes = _registro.DoMes(hoje);
            return new InstantaneoHistorico(
                pontos, ultimo.UploadBps, ultimo.DownloadBps, picoUp, picoDown,
                dia.Enviados, dia.Recebidos,
                _linkBps, _nomeInterface, _anel.Length,
                mes.Enviados, mes.Recebidos);
        }
    }

    // ------------------------------------------------------------------ v1.0.0: franquia e CSV

    /// <summary>
    /// Marco da franquia mensal (80 ou 100 %) a avisar agora, ou null. Cada marco sai uma vez por
    /// mês; o "já avisado" é gravado junto do consumo (reiniciar o app não repete o aviso).
    /// </summary>
    public int? MarcoFranquiaParaAvisar(double franquiaGb)
    {
        lock (_trava)
        {
            int? marco = _registro.MarcoFranquiaParaAvisar(_hoje(), franquiaGb);
            if (marco is not null)
            {
                GravarSemTrava();
            }

            return marco;
        }
    }

    /// <summary>Fração da franquia mensal já usada (0 = sem franquia).</summary>
    public double FracaoFranquia(double franquiaGb)
    {
        lock (_trava)
        {
            return _registro.FracaoFranquia(_hoje(), franquiaGb);
        }
    }

    /// <summary>Consumo de cada dia guardado, em CSV pt-BR (";" e vírgula decimal).</summary>
    public string ExportarCsv()
    {
        lock (_trava)
        {
            return _registro.ParaCsv();
        }
    }

    /// <summary>Grava o consumo (chamado no encerramento do app e antes de atualizar).</summary>
    public void SalvarConsumo()
    {
        lock (_trava)
        {
            GravarSemTrava();
        }
    }

    private void GravarSemTrava()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_caminhoDiario)!);
            // Grava num .tmp e troca: queda de energia no meio não deixa um arquivo pela metade
            string tmp = _caminhoDiario + ".tmp";
            File.WriteAllText(tmp, _registro.ParaJson());
            File.Move(tmp, _caminhoDiario, overwrite: true);
        }
        catch
        {
            // Silencioso: perder o consumo do dia não pode derrubar o medidor
        }
    }

    private void CarregarConsumo()
    {
        // v1.0.0: o registro diário é a fonte; sem ele (1ª abertura depois de atualizar), migra o
        // consumo.json antigo, que guardava só o dia em que foi gravado
        try
        {
            if (File.Exists(_caminhoDiario))
            {
                _registro = RegistroConsumo.DeJson(File.ReadAllText(_caminhoDiario));
                _registro.Podar(_dia);
            }
        }
        catch
        {
            // Corrompido: tenta o formato antigo abaixo e, sem ele, recomeça do zero
            _registro = new RegistroConsumo();
        }

        // O consumo.json antigo é SEMPRE conferido: uma versão anterior pode ter rodado depois
        // (ex.: a 0.9.0 aberta de novo antes de a atualização chegar) e contado mais naquele dia.
        // Fica o MAIOR valor de cada campo — nunca soma (seria contar duas vezes o mesmo tráfego).
        try
        {
            if (!File.Exists(_caminhoConsumo))
            {
                return;
            }

            ConsumoGravado? c = JsonSerializer.Deserialize<ConsumoGravado>(File.ReadAllText(_caminhoConsumo));
            if (c is not null && DateOnly.TryParseExact(c.Dia, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateOnly diaGravado))
            {
                // Antes, um dia diferente de hoje era descartado; agora vira histórico do mês
                ConsumoDia atual = _registro.Do(diaGravado);
                _registro.Definir(diaGravado, new ConsumoDia(
                    Math.Max(atual.Enviados, c.Enviados), Math.Max(atual.Recebidos, c.Recebidos)));
            }
        }
        catch
        {
            // Arquivo corrompido: recomeça do zero
        }
    }
}
