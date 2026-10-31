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
    int Capacidade = HistoricoRede.JanelaSegundos);

/// <summary>
/// Memória curta do medidor para o "NetTraffic Quick Flyout" do Modelo 3:
/// - últimos 60 s (gráfico "Telemetria em Tempo Real (60s)" e o "Pico" de cada cartão);
/// - consumo acumulado do DIA ("Hoje: ↑ 1,2 GB · ↓ 14,8 GB"), persistido em consumo.json
///   para sobreviver a reinício do app/PC no mesmo dia, e zerado na virada do dia.
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
    private ulong _enviadosHoje;
    private ulong _recebidosHoje;
    private ulong _linkBps;
    private string _nomeInterface = string.Empty;

    private readonly string _caminhoConsumo;
    private readonly Func<DateOnly> _hoje;

    private sealed record ConsumoGravado(string Dia, ulong Enviados, ulong Recebidos);

    /// <param name="caminhoConsumo">Sobrescrito em testes.</param>
    /// <param name="hoje">Relógio injetável (testes da virada do dia).</param>
    public HistoricoRede(string? caminhoConsumo = null, Func<DateOnly>? hoje = null)
    {
        _caminhoConsumo = caminhoConsumo ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VelocimetroInternet", "consumo.json");
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
                // Virou o dia: o consumo "Hoje" recomeça do zero
                _dia = hoje;
                _enviadosHoje = 0;
                _recebidosHoje = 0;
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

            _enviadosHoje += bytesEnviadosNoIntervalo;
            _recebidosHoje += bytesRecebidosNoIntervalo;
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
            bool diaAtual = _hoje() == _dia;
            return new InstantaneoHistorico(
                pontos, ultimo.UploadBps, ultimo.DownloadBps, picoUp, picoDown,
                diaAtual ? _enviadosHoje : 0, diaAtual ? _recebidosHoje : 0,
                _linkBps, _nomeInterface, _anel.Length);
        }
    }

    /// <summary>Grava o consumo do dia (chamado no encerramento do app).</summary>
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
            Directory.CreateDirectory(Path.GetDirectoryName(_caminhoConsumo)!);
            string tmp = _caminhoConsumo + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(
                new ConsumoGravado(_dia.ToString("yyyy-MM-dd"), _enviadosHoje, _recebidosHoje)));
            File.Move(tmp, _caminhoConsumo, overwrite: true);
        }
        catch
        {
            // Silencioso: perder o consumo do dia não pode derrubar o medidor
        }
    }

    private void CarregarConsumo()
    {
        try
        {
            if (!File.Exists(_caminhoConsumo))
            {
                return;
            }

            ConsumoGravado? c = JsonSerializer.Deserialize<ConsumoGravado>(File.ReadAllText(_caminhoConsumo));
            // Só aproveita o que foi gravado HOJE; de outro dia é descartado
            if (c is not null && c.Dia == _dia.ToString("yyyy-MM-dd"))
            {
                _enviadosHoje = c.Enviados;
                _recebidosHoje = c.Recebidos;
            }
        }
        catch
        {
            // Arquivo corrompido: recomeça do zero
        }
    }
}
