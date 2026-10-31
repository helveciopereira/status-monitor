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
    string NomeInterface);

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

    private readonly object _trava = new();
    private readonly PontoHistorico[] _anel = new PontoHistorico[JanelaSegundos];
    private int _inicio;
    private int _quantidade;

    private DateOnly _dia;
    private ulong _enviadosHoje;
    private ulong _recebidosHoje;
    private ulong _linkBps;
    private string _nomeInterface = string.Empty;
    private int _registrosDesdeGravacao;

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

            int pos = (_inicio + _quantidade) % JanelaSegundos;
            _anel[pos] = new PontoHistorico(Math.Max(0, uploadBps), Math.Max(0, downloadBps));
            if (_quantidade < JanelaSegundos)
            {
                _quantidade++;
            }
            else
            {
                _inicio = (_inicio + 1) % JanelaSegundos;
            }

            _enviadosHoje += bytesEnviadosNoIntervalo;
            _recebidosHoje += bytesRecebidosNoIntervalo;
            _linkBps = linkBps;
            _nomeInterface = nomeInterface;

            // Grava no disco a cada minuto (no máximo 60 s de consumo perdidos numa queda de energia)
            if (++_registrosDesdeGravacao >= 60)
            {
                _registrosDesdeGravacao = 0;
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
                PontoHistorico p = _anel[(_inicio + i) % JanelaSegundos];
                pontos[i] = p;
                picoUp = Math.Max(picoUp, p.UploadBps);
                picoDown = Math.Max(picoDown, p.DownloadBps);
            }

            PontoHistorico ultimo = _quantidade > 0 ? pontos[^1] : default;
            bool diaAtual = _hoje() == _dia;
            return new InstantaneoHistorico(
                pontos, ultimo.UploadBps, ultimo.DownloadBps, picoUp, picoDown,
                diaAtual ? _enviadosHoje : 0, diaAtual ? _recebidosHoje : 0,
                _linkBps, _nomeInterface);
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
