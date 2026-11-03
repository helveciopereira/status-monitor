using System.Globalization;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// Unidade base escolhida pelo usuário nas configurações.
/// Decisão do dono (2026-10-31): padrão em <see cref="BytesPorSegundo"/> (MB/s),
/// com possibilidade de alterar para <see cref="BitsPorSegundo"/> (Mbps).
/// </summary>
public enum ModoUnidadeVelocidade
{
    BytesPorSegundo = 0,
    BitsPorSegundo = 1
}

/// <summary>
/// Formata velocidades em texto de largura previsível para a barra de tarefas, aplicando:
/// 1. Separador decimal do idioma da interface (v1.9.0: vírgula em português, ponto em inglês —
///    <see cref="Idioma.Cultura"/>, pt-BR por padrão; os métodos estáticos aceitam a cultura
///    explícita para o PDF/CSV, que ficam em pt-BR);
/// 2. Histerese de 2 leituras para descer de escala (evita piscar entre 999 KB/s e 1,00 MB/s);
/// 3. Sempre até 3 algarismos significativos (ex.: "1,24 MB/s", "38,7 MB/s", "387 KB/s").
/// Referência: 01-ANALISE.md §5 e 02-ARQUITETURA.md §7.
/// </summary>
public sealed class SpeedFormatter
{
    /// <summary>Cultura dos números da interface (v1.9.0: era pt-BR fixo).</summary>
    private static CultureInfo Cultura => Idioma.Cultura;

    private static readonly string[] UnidadesBytes = ["B/s", "KB/s", "MB/s", "GB/s"];
    private static readonly string[] UnidadesBits = ["bps", "Kbps", "Mbps", "Gbps"];

    // Começa em escala 1 (KB/s ou Kbps) para evitar exibir "0 B/s" na barra em repouso
    private int _indiceEscalaAtual = 1;
    private int _leiturasAbaixoDoLimiar;

    public ModoUnidadeVelocidade ModoUnidade { get; set; }

    public SpeedFormatter(ModoUnidadeVelocidade modoUnidade = ModoUnidadeVelocidade.BytesPorSegundo)
    {
        ModoUnidade = modoUnidade;
    }

    /// <summary>
    /// Formata um valor em bytes por segundo com a seta correspondente (ex.: "↑ 1,24 MB/s").
    /// </summary>
    public string FormatarComSeta(string glifoSeta, double bytesPorSegundo)
    {
        string valorUnidade = FormatarValorEUnidade(bytesPorSegundo);
        return $"{glifoSeta} {valorUnidade}";
    }

    /// <summary>
    /// Formata apenas o número compacto sem a unidade para o modo de colisão de 1 linha (ex.: "↑ 1,2  ↓ 38,7 MB/s").
    /// </summary>
    public static string FormatarLinhaCompacta(
        double uploadBytesPorSegundo,
        double downloadBytesPorSegundo,
        ModoUnidadeVelocidade modoUnidade = ModoUnidadeVelocidade.BytesPorSegundo)
    {
        double multiplicador = modoUnidade == ModoUnidadeVelocidade.BitsPorSegundo ? 8.0 : 1.0;
        double divisor = modoUnidade == ModoUnidadeVelocidade.BitsPorSegundo ? 1000.0 : 1024.0;
        string[] unidades = modoUnidade == ModoUnidadeVelocidade.BitsPorSegundo ? UnidadesBits : UnidadesBytes;

        double maiorValor = Math.Max(0.0, Math.Max(uploadBytesPorSegundo, downloadBytesPorSegundo)) * multiplicador;
        int escala = 2; // Padrão compacto em MB/s ou Mbps
        double fatorEscala = divisor * divisor;

        if (maiorValor >= fatorEscala * divisor)
        {
            escala = 3;
            fatorEscala *= divisor;
        }
        else if (maiorValor < divisor * 100.0)
        {
            escala = 1;
            fatorEscala = divisor;
        }

        double upConvertido = (Math.Max(0.0, uploadBytesPorSegundo) * multiplicador) / fatorEscala;
        double downConvertido = (Math.Max(0.0, downloadBytesPorSegundo) * multiplicador) / fatorEscala;

        return $"↑ {FormatarNumeroSignificativo(upConvertido)}  ↓ {FormatarNumeroSignificativo(downConvertido)} {unidades[escala]}";
    }

    public string FormatarValorEUnidade(double bytesPorSegundo)
    {
        double valorBase = Math.Max(0.0, bytesPorSegundo);
        double divisor = ModoUnidade == ModoUnidadeVelocidade.BitsPorSegundo ? 1000.0 : 1024.0;
        string[] nomesUnidades = ModoUnidade == ModoUnidadeVelocidade.BitsPorSegundo ? UnidadesBits : UnidadesBytes;

        if (ModoUnidade == ModoUnidadeVelocidade.BitsPorSegundo)
        {
            valorBase *= 8.0;
        }

        // Calcula o valor na escala atual (_indiceEscalaAtual: 1 = KB/Kbps, 2 = MB/Mbps, 3 = GB/Gbps)
        double valorNaEscala = valorBase / Math.Pow(divisor, _indiceEscalaAtual);

        // Subida de unidade é imediata ao passar de 999,5 (evita exibir 1000 KB/s)
        while (valorNaEscala >= 999.5 && _indiceEscalaAtual < nomesUnidades.Length - 1)
        {
            _indiceEscalaAtual++;
            _leiturasAbaixoDoLimiar = 0;
            valorNaEscala = valorBase / Math.Pow(divisor, _indiceEscalaAtual);
        }

        // Descida de unidade exige histerese: ficar abaixo de 0,90 por pelo menos 2 leituras seguidas
        // (e nunca desce abaixo de KB/s / Kbps para manter estética limpa em repouso)
        if (valorNaEscala < 0.90 && _indiceEscalaAtual > 1)
        {
            _leiturasAbaixoDoLimiar++;
            if (_leiturasAbaixoDoLimiar >= 2)
            {
                while (valorNaEscala < 0.90 && _indiceEscalaAtual > 1)
                {
                    _indiceEscalaAtual--;
                    valorNaEscala = valorBase / Math.Pow(divisor, _indiceEscalaAtual);
                }

                _leiturasAbaixoDoLimiar = 0;
            }
        }
        else
        {
            _leiturasAbaixoDoLimiar = 0;
        }

        string numeroFormatado = FormatarNumeroSignificativo(valorNaEscala);
        return $"{numeroFormatado} {nomesUnidades[_indiceEscalaAtual]}";
    }

    /// <summary>
    /// Quantidade transferida para o "Consumo Acumulado" do painel (base 1024, 1 casa):
    /// "0 B", "512 KB", "14,8 GB". Sem histerese — o valor só cresce ao longo do dia.
    /// </summary>
    /// <param name="cultura">v1.9.0: null = a da interface (<see cref="Idioma.Cultura"/>); o PDF/CSV passa pt-BR.</param>
    public static string FormatarQuantidade(ulong bytes, CultureInfo? cultura = null)
    {
        CultureInfo c = cultura ?? Cultura;
        string[] unidades = ["B", "KB", "MB", "GB", "TB"];
        double valor = bytes;
        int i = 0;
        while (valor >= 1000 && i < unidades.Length - 1)
        {
            valor /= 1024.0;
            i++;
        }

        string numero = i == 0 || valor >= 100
            ? Math.Round(valor).ToString("0", c)
            : valor.ToString("0.0", c);
        return $"{numero} {unidades[i]}";
    }

    /// <summary>Velocidade de link da placa ("300 Mbps", "2,4 Gbps") — sempre em bits, como o Windows mostra.</summary>
    /// <param name="cultura">v1.9.0: null = a da interface.</param>
    public static string FormatarLink(ulong bitsPorSegundo, CultureInfo? cultura = null)
    {
        CultureInfo c = cultura ?? Cultura;
        if (bitsPorSegundo >= 1_000_000_000)
        {
            double g = bitsPorSegundo / 1e9;
            return $"{(g % 1 == 0 ? g.ToString("0", c) : g.ToString("0.0", c))} Gbps";
        }

        return $"{Math.Round(bitsPorSegundo / 1e6).ToString("0", c)} Mbps";
    }

    /// <summary>
    /// Escala "redonda" para o gráfico de 60 s do painel: o menor valor da série 1-2-5
    /// (1, 2, 5, 10, 20, 50, 100, 200, 500) que cabe o pico, na unidade em uso.
    /// Ex.: pico de 690 KB/s → topo "1 MB/s" e meio "512 KB/s"; pico de 38,7 Mbps → "50 Mbps"/"25 Mbps".
    /// Números redondos dão noção de grandeza de relance — "o pico chegou a ~70% de 1 MB/s" —
    /// coisa que um topo igual ao próprio pico (ex.: "690 KB/s") não dá.
    /// </summary>
    /// <returns>Topo em BYTES por segundo (para desenhar) e os rótulos do topo e do meio.</returns>
    public static (double TopoBytesPorSegundo, string RotuloTopo, string RotuloMeio) EscalaRedonda(
        double picoBytesPorSegundo,
        ModoUnidadeVelocidade modo)
    {
        bool bits = modo == ModoUnidadeVelocidade.BitsPorSegundo;
        double multiplicador = bits ? 8.0 : 1.0;
        double divisor = bits ? 1000.0 : 1024.0;
        string[] unidades = bits ? UnidadesBits : UnidadesBytes;

        // Piso de 1 KB/s (1 Kbps): em repouso o gráfico não "amplia" ruído de poucos bytes
        int escala = 1;
        double valor = Math.Max(picoBytesPorSegundo * multiplicador, divisor) / divisor;
        while (valor > 500 && escala < unidades.Length - 1)
        {
            // Acima de 500 na escala atual, o próximo degrau (1000) é a unidade seguinte
            escala++;
            valor /= divisor;
        }

        double[] serie = [1, 2, 5, 10, 20, 50, 100, 200, 500, 1000];
        double redondo = serie.First(s => s >= valor - 1e-9);

        double topoNaUnidade = redondo * Math.Pow(divisor, escala);
        double topoBytes = topoNaUnidade / multiplicador;

        // Meio: na mesma unidade quando dá ≥ 1 ("25 Mbps", "2,5 MB/s"); senão uma unidade abaixo
        // (metade de 1 MB/s = "512 KB/s"; de 1 Mbps = "500 Kbps"; de 1 KB/s = "512 B/s").
        // Não usa o formatador da barra: ele nunca desce de KB e daria "0,50 KB/s".
        string rotuloTopo = $"{FormatarRedondo(redondo)} {unidades[escala]}";
        string rotuloMeio = redondo / 2 >= 1
            ? $"{FormatarRedondo(redondo / 2)} {unidades[escala]}"
            : $"{FormatarRedondo(redondo / 2 * divisor)} {unidades[escala - 1]}";
        return (topoBytes, rotuloTopo, rotuloMeio);
    }

    private static string FormatarRedondo(double v) =>
        v % 1 == 0 ? v.ToString("0", Cultura) : v.ToString("0.#", Cultura);

    /// <summary>
    /// Separa "38,7 MB/s" em ("38,7", "MB/s") — o painel mostra o número grande e a unidade menor.
    /// </summary>
    public static (string Numero, string Unidade) SepararNumeroEUnidade(string texto)
    {
        int espaco = texto.LastIndexOf(' ');
        return espaco < 0 ? (texto, string.Empty) : (texto[..espaco], texto[(espaco + 1)..]);
    }

    private static string FormatarNumeroSignificativo(double valor)
    {
        if (valor < 0.005)
        {
            return 0d.ToString("0.00", Cultura); // v1.9.0: "0,00" em português, "0.00" em inglês
        }

        if (valor < 9.995)
        {
            return valor.ToString("0.00", Cultura);
        }

        if (valor < 99.95)
        {
            return valor.ToString("0.0", Cultura);
        }

        return Math.Round(valor).ToString("0", Cultura);
    }
}

