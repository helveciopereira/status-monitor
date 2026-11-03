using System.Globalization;
using VelocimetroInternet.Medicao;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// v1.0.0 (Onda 5): textos "puros" das ideias de baixo esforço do 04-IDEIAS.md — a dica ao passar
/// o mouse no widget e a opacidade da "cor por intensidade". Sem WPF: testável em xUnit comum.
/// </summary>
public static class DicaWidget
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    /// <summary>
    /// Dica do widget: interface + IP, consumo de hoje e do mês (com a franquia, se houver),
    /// picos dos últimos 60 s e os atalhos de mouse. Linhas sem dado somem (ex.: IP desconhecido).
    /// </summary>
    /// <param name="fracaoFranquia">0 = sem franquia definida.</param>
    /// <param name="semInternet">v1.8.1: placa conectada, mas o Windows diz "sem internet" (ignorado se <paramref name="semConexao"/>).</param>
    public static string Montar(InstantaneoHistorico h, string? ip, ModoUnidadeVelocidade unidade,
        double fracaoFranquia, bool semConexao, LatenciaAtual? latencia = null, bool semInternet = false)
    {
        var linhas = new List<string>(7);

        if (semConexao)
        {
            linhas.Add("Sem conexão de rede");
        }
        else
        {
            string rede = string.IsNullOrEmpty(h.NomeInterface) ? "Rede" : h.NomeInterface;
            linhas.Add(string.IsNullOrEmpty(ip) ? rede : $"{rede} · IP {ip}");
            if (semInternet)
            {
                // v1.8.1: explica o "⚠" do widget — o roteador responde, a internet não
                linhas.Add("⚠ Sem internet: o PC está ligado ao roteador, mas a internet não responde");
            }
        }

        linhas.Add($"Hoje: ↑ {SpeedFormatter.FormatarQuantidade(h.BytesEnviadosHoje)} · ↓ {SpeedFormatter.FormatarQuantidade(h.BytesRecebidosHoje)}");

        string mes = $"Mês: ↑ {SpeedFormatter.FormatarQuantidade(h.BytesEnviadosMes)} · ↓ {SpeedFormatter.FormatarQuantidade(h.BytesRecebidosMes)}";
        if (fracaoFranquia > 0)
        {
            mes += $" ({TextoPercentual(fracaoFranquia)} da franquia)";
        }

        linhas.Add(mes);

        // Formatadores novos: o pico é valor estático, não precisa da histerese do widget
        var f = new SpeedFormatter(unidade);
        // v1.1.0: latência só com a medição ligada (e já com ao menos uma rodada concluída)
        if (latencia is { Medido: true })
        {
            linhas.Add($"Latência: {latencia.Texto()}");
        }

        linhas.Add($"Pico em 60 s: ↑ {f.FormatarValorEUnidade(h.PicoUpload)} · ↓ {new SpeedFormatter(unidade).FormatarValorEUnidade(h.PicoDownload)}");
        // "Clique abre o painel" é óbvio; a dica ensina só os atalhos escondidos (cabe numa linha)
        linhas.Add("Duplo clique: Gerenciador de Tarefas · Ctrl + arrastar: mover");
        return string.Join(Environment.NewLine, linhas);
    }

    /// <summary>"82 %" (arredondado para baixo: 99,6 % não pode aparecer como "100 %" antes da hora).</summary>
    public static string TextoPercentual(double fracao) =>
        $"{Math.Floor(Math.Max(0, fracao) * 100).ToString("0", PtBr)} %";

    /// <summary>Opacidade mínima do número parado: ainda legível na barra (não some).</summary>
    public const double OpacidadeMinima = 0.5;

    /// <summary>
    /// Cor por intensidade: opacidade do número entre <see cref="OpacidadeMinima"/> (parado) e 1
    /// (na velocidade de referência ou acima). Raiz quadrada: 10 % da banda já aparece bem mais
    /// forte que 0 — o uso comum fica longe do teto, e a escala linear deixaria tudo apagado.
    /// Referência ≤ 0 (desligado ou desconhecida) = opacidade 1, o visual de sempre.
    /// </summary>
    public static double OpacidadePorIntensidade(double bytesPorSegundo, double referenciaBytesPorSegundo)
    {
        if (!(referenciaBytesPorSegundo > 0) || !double.IsFinite(bytesPorSegundo))
        {
            return 1.0;
        }

        double fracao = Math.Clamp(bytesPorSegundo / referenciaBytesPorSegundo, 0, 1);
        return OpacidadeMinima + ((1 - OpacidadeMinima) * Math.Sqrt(fracao));
    }
}

/// <summary>
/// v1.0.0: "foi duplo clique?" pela regra do próprio Windows — 2º aperto dentro do tempo de duplo
/// clique (Painel de Controle › Mouse) e dentro do retângulo SM_CXDOUBLECLK × SM_CYDOUBLECLK,
/// centrado no 1º aperto.
/// </summary>
public static class RegraDuploClique
{
    public static bool EhDuplo(long msDesdeUltimo, int dxPx, int dyPx, uint tempoDuploMs, int larguraPx, int alturaPx) =>
        msDesdeUltimo >= 0
        && msDesdeUltimo <= tempoDuploMs
        && Math.Abs(dxPx) <= Math.Max(1, larguraPx) / 2
        && Math.Abs(dyPx) <= Math.Max(1, alturaPx) / 2;
}
