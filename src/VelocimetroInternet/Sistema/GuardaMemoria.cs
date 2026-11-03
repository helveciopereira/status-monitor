namespace VelocimetroInternet.Sistema;

/// <summary>
/// v1.8.0: decide quando vale uma coleta completa de lixo (gen2 + finalizadores) fora dos momentos
/// em que o app já faz isso (fechar painel/estúdio).
///
/// Por quê: o heap gerenciado do widget é minúsculo (6 MB medidos com dotnet-gcdump na v1.7.1 após
/// 26 h), mas a memória privada chegava a 340 MB e os handles a 2 769 — e uma coleta completa
/// forçada derrubou ~1 000 handles de uma vez. Ou seja: boa parte do "crescimento" é lixo que
/// segura recurso nativo (nós da UI Automation, objetos COM, SafeHandles) esperando uma coleta de
/// gen2 que nunca vem, porque o coletor só olha o tamanho do heap gerenciado, que não cresce.
/// A regra: quando a memória privada subiu <see cref="LimiarCrescimento"/> desde a última coleta
/// completa (ou desde a primeira medição), coleta — e quem chama registra antes/depois no log,
/// o que também serve de diagnóstico no campo (se nada cair, o crescimento é de outra natureza).
///
/// Custo: uma gen2 de 6 MB leva poucos ms; a regra garante no máximo uma a cada
/// <see cref="LimiarCrescimento"/> de crescimento, não por relógio.
/// </summary>
public sealed class GuardaMemoria
{
    /// <summary>Crescimento da memória privada que dispara a coleta completa.</summary>
    public const long LimiarCrescimento = 16L * 1024 * 1024;

    private long? _base;

    /// <summary>Memória privada que serve de referência (após a última coleta ou a primeira medição), ou null.</summary>
    public long? Base => _base;

    /// <summary>
    /// Informa a memória privada atual e responde se deve coletar agora. A primeira chamada só
    /// fixa a referência (nunca coleta no primeiro tick: o app acabou de abrir).
    /// </summary>
    public bool DeveColetar(long privadaAgora)
    {
        if (_base is not { } b)
        {
            _base = privadaAgora;
            return false;
        }

        return privadaAgora - b >= LimiarCrescimento;
    }

    /// <summary>Depois da coleta, a memória que sobrou vira a nova referência.</summary>
    public void RegistrarAposColeta(long privadaDepois) => _base = privadaDepois;
}
