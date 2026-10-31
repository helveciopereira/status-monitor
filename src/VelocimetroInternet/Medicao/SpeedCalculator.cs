namespace VelocimetroInternet.Medicao;

/// <summary>
/// Resultado do cálculo de velocidade em bytes por segundo (cru e suavizado).
/// </summary>
public readonly record struct VelocidadeRede(
    double UploadBytesPorSegundo,
    double DownloadBytesPorSegundo,
    bool AmostraValida);

/// <summary>
/// Calcula a velocidade instantânea (delta de bytes ÷ delta de tempo do Stopwatch) e aplica
/// média móvel exponencial (EMA) para evitar que os números fiquem ilegíveis aos solavancos.
/// Regra de robustez (02-ARQUITETURA.md §3): se uma interface reconectar e o contador zerar
/// (delta negativo), descarta a amostra e realinha a base sem exibir valor absurdo.
/// </summary>
public sealed class SpeedCalculator
{
    private readonly double _fatorSuavizacao;
    private bool _possuiAmostraAnterior;
    private ulong _ultimoTotalRecebidos;
    private ulong _ultimoTotalEnviados;
    private double _emaDownloadBps;
    private double _emaUploadBps;

    /// <param name="fatorSuavizacao">
    /// Fator alpha da EMA entre 0.0 (sem suavização, valor 100% cru) e 0.9 (muito suavizado).
    /// Padrão da arquitetura (01-ANALISE.md §5): 0.5.
    /// </param>
    public SpeedCalculator(double fatorSuavizacao = 0.5)
    {
        _fatorSuavizacao = Math.Clamp(fatorSuavizacao, 0.0, 0.95);
    }

    public VelocidadeRede CalcularProximaAmostra(
        ulong totalBytesRecebidosAgora,
        ulong totalBytesEnviadosAgora,
        TimeSpan tempoDecorrido)
    {
        // Primeira leitura ou intervalo de tempo inválido: apenas registra a linha de base
        if (!_possuiAmostraAnterior || tempoDecorrido.TotalSeconds <= 0.001)
        {
            _possuiAmostraAnterior = true;
            _ultimoTotalRecebidos = totalBytesRecebidosAgora;
            _ultimoTotalEnviados = totalBytesEnviadosAgora;
            return new VelocidadeRede(_emaUploadBps, _emaDownloadBps, AmostraValida: false);
        }

        // Se o contador diminuiu (ex.: Wi-Fi reconectou ou adaptador reiniciou), descarta o delta negativo
        if (totalBytesRecebidosAgora < _ultimoTotalRecebidos ||
            totalBytesEnviadosAgora < _ultimoTotalEnviados)
        {
            _ultimoTotalRecebidos = totalBytesRecebidosAgora;
            _ultimoTotalEnviados = totalBytesEnviadosAgora;
            return new VelocidadeRede(_emaUploadBps, _emaDownloadBps, AmostraValida: false);
        }

        ulong deltaRecebidos = totalBytesRecebidosAgora - _ultimoTotalRecebidos;
        ulong deltaEnviados = totalBytesEnviadosAgora - _ultimoTotalEnviados;

        _ultimoTotalRecebidos = totalBytesRecebidosAgora;
        _ultimoTotalEnviados = totalBytesEnviadosAgora;

        double segundos = tempoDecorrido.TotalSeconds;
        double cruDownload = deltaRecebidos / segundos;
        double cruUpload = deltaEnviados / segundos;

        if (_fatorSuavizacao <= 0.001)
        {
            _emaDownloadBps = cruDownload;
            _emaUploadBps = cruUpload;
        }
        else
        {
            // α = peso da suavização anterior; (1 - α) = peso da leitura nova
            double pesoNovo = 1.0 - _fatorSuavizacao;
            _emaDownloadBps = (_emaDownloadBps * _fatorSuavizacao) + (cruDownload * pesoNovo);
            _emaUploadBps = (_emaUploadBps * _fatorSuavizacao) + (cruUpload * pesoNovo);
        }

        return new VelocidadeRede(
            UploadBytesPorSegundo: _emaUploadBps,
            DownloadBytesPorSegundo: _emaDownloadBps,
            AmostraValida: true);
    }
}
