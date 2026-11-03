namespace VelocimetroInternet.Sistema;

/// <summary>
/// v1.7.2: decide o que fazer quando uma exceção NÃO TRATADA chega à thread da interface.
///
/// Antes desta versão não havia tratador nenhum: um clique num botão de janela secundária que
/// lançasse exceção (ex.: um <c>async void</c> do teste de velocidade) fechava o app inteiro sem
/// deixar uma linha no log — para o usuário, "o widget sumiu". Agora a regra é:
/// - erro isolado → fica no log e o app SEGUE (o widget continua medindo; a janela que falhou
///   é o único prejuízo);
/// - tempestade de erros (mais de <see cref="MaximoPorJanela"/> em <see cref="Janela"/>) → algo
///   está quebrado de verdade e o app encerra, senão ficaria preso num ciclo de falhas gravando
///   o log sem parar. O limite é alto o bastante para um erro repetido a cada clique do usuário
///   não derrubar o app, e baixo o bastante para um laço de erro (vários por segundo) parar em
///   poucos segundos.
///
/// Regra pura (sem relógio próprio): quem chama passa o instante, por isso é testável.
/// </summary>
public sealed class PoliticaFalhas
{
    /// <summary>Até esta quantidade de erros dentro da janela, o app continua.</summary>
    public const int MaximoPorJanela = 5;

    /// <summary>Janela deslizante em que os erros são contados.</summary>
    public static readonly TimeSpan Janela = TimeSpan.FromMinutes(1);

    private readonly Queue<DateTime> _instantes = new();
    private readonly object _trava = new();

    /// <summary>
    /// Registra uma falha ocorrida em <paramref name="agora"/> e responde: o app deve continuar?
    /// true = continuar (erro isolado); false = encerrar (tempestade).
    /// </summary>
    public bool RegistrarEContinuar(DateTime agora)
    {
        lock (_trava)
        {
            // Esquece os erros que já saíram da janela (ela desliza com o tempo)
            while (_instantes.Count > 0 && agora - _instantes.Peek() > Janela)
            {
                _instantes.Dequeue();
            }

            _instantes.Enqueue(agora);
            return _instantes.Count <= MaximoPorJanela;
        }
    }

    /// <summary>Quantos erros estão dentro da janela agora (para o log).</summary>
    public int ErrosNaJanela
    {
        get
        {
            lock (_trava)
            {
                return _instantes.Count;
            }
        }
    }
}
