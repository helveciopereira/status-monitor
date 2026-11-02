namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// v1.7.0 (Onda 13): os últimos <see cref="JanelaMs"/> de velocidade de UMA direção (↑ ou ↓),
/// para o mini gráfico atrás do número na barra.
///
/// Guarda o INSTANTE de cada amostra, não só o valor: o intervalo de atualização é configurável
/// (0,5 a 5 s) e pode haver buracos (sem conexão, PC suspenso). Posicionar pelo tempo mantém a
/// largura do gráfico sempre igual a 60 s, qualquer que seja o ritmo das amostras.
/// Classe sem WPF de propósito: é testada sem janela.
/// </summary>
public sealed class SerieCurta
{
    /// <summary>Janela mostrada: 60 s (a mesma do gráfico "1 min" do painel rápido).</summary>
    public const long JanelaMs = 60_000;

    /// <summary>
    /// Piso da escala vertical, em bytes/s (64 KB/s ≈ 0,5 Mbps). Sem ele, o tráfego de fundo de
    /// um PC parado (poucos KB/s) encheria a pílula toda e pareceria uso pesado.
    /// </summary>
    public const double PisoEscalaBytesPorSegundo = 64 * 1024;

    /// <summary>Folga no topo: o pico chega a 90 % da altura, sem encostar na borda da pílula.</summary>
    private const double Folga = 1.1;

    // Em ordem de tempo: entra no fim, sai do começo. ~60 itens no intervalo padrão de 1 s, então
    // o RemoveAt(0) de uma List custa nada e a leitura por índice fica simples.
    private readonly List<(long Ms, double Valor)> _amostras = [];

    /// <summary>Quantidade de amostras guardadas (para teste e para "há o que desenhar?").</summary>
    public int Quantidade => _amostras.Count;

    /// <summary>
    /// Acrescenta uma amostra e descarta as que saíram da janela. Valor negativo/NaN vira 0
    /// (contador do Windows que "volta" ao trocar de placa não pode desenhar abaixo do chão).
    /// Instante MENOR que o último (relógio não volta, mas por garantia) zera a série.
    /// </summary>
    public void Adicionar(long agoraMs, double bytesPorSegundo)
    {
        if (_amostras.Count > 0 && agoraMs < _amostras[^1].Ms)
        {
            _amostras.Clear();
        }

        double valor = double.IsFinite(bytesPorSegundo) && bytesPorSegundo > 0 ? bytesPorSegundo : 0;
        _amostras.Add((agoraMs, valor));
        Descartar(agoraMs);
    }

    /// <summary>Esquece tudo (medição pausada: o buraco não deve virar uma reta enganosa).</summary>
    public void Limpar() => _amostras.Clear();

    /// <summary>
    /// Pontos prontos para desenhar, em frações de 0 a 1: X = 0 é "60 s atrás" e X = 1 é "agora";
    /// Y = 0 é parado e Y = 1 é o topo da pílula. A escala é o maior valor da janela (com folga),
    /// nunca menor que o piso. Menos de 2 pontos = nada a desenhar (lista vazia).
    /// </summary>
    public IReadOnlyList<(double X, double Y)> Pontos(long agoraMs)
    {
        Descartar(agoraMs);
        if (_amostras.Count < 2)
        {
            return [];
        }

        double maximo = PisoEscalaBytesPorSegundo;
        foreach ((_, double v) in _amostras)
        {
            maximo = Math.Max(maximo, v);
        }

        double escala = maximo * Folga;
        var pontos = new List<(double X, double Y)>(_amostras.Count);
        foreach ((long ms, double v) in _amostras)
        {
            double x = 1.0 - (double)(agoraMs - ms) / JanelaMs;
            pontos.Add((Math.Clamp(x, 0, 1), Math.Clamp(v / escala, 0, 1)));
        }

        return pontos;
    }

    /// <summary>
    /// Tira as amostras velhas, mas MANTÉM a última de antes da janela: ela ancora a linha na borda
    /// esquerda (sem ela o desenho começaria um pouco depois do X = 0, "flutuando").
    /// </summary>
    private void Descartar(long agoraMs)
    {
        long limite = agoraMs - JanelaMs;
        // Só descarta a 1ª se a 2ª também já está na borda ou fora da janela
        while (_amostras.Count >= 2 && _amostras[1].Ms <= limite)
        {
            _amostras.RemoveAt(0);
        }
    }
}
