using System.Globalization;

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
/// 1. Vírgula decimal brasileira (pt-BR);
/// 2. Histerese de 2 leituras para descer de escala (evita piscar entre 999 KB/s e 1,00 MB/s);
/// 3. Sempre até 3 algarismos significativos (ex.: "1,24 MB/s", "38,7 MB/s", "387 KB/s").
/// Referência: 01-ANALISE.md §5 e 02-ARQUITETURA.md §7.
/// </summary>
public sealed class SpeedFormatter
{
    private static readonly CultureInfo CulturaPtBr = new("pt-BR");

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
    public static string FormatarQuantidade(ulong bytes)
    {
        string[] unidades = ["B", "KB", "MB", "GB", "TB"];
        double valor = bytes;
        int i = 0;
        while (valor >= 1000 && i < unidades.Length - 1)
        {
            valor /= 1024.0;
            i++;
        }

        string numero = i == 0 || valor >= 100
            ? Math.Round(valor).ToString("0", CulturaPtBr)
            : valor.ToString("0.0", CulturaPtBr);
        return $"{numero} {unidades[i]}";
    }

    /// <summary>Velocidade de link da placa ("300 Mbps", "2,4 Gbps") — sempre em bits, como o Windows mostra.</summary>
    public static string FormatarLink(ulong bitsPorSegundo)
    {
        if (bitsPorSegundo >= 1_000_000_000)
        {
            double g = bitsPorSegundo / 1e9;
            return $"{(g % 1 == 0 ? g.ToString("0", CulturaPtBr) : g.ToString("0.0", CulturaPtBr))} Gbps";
        }

        return $"{Math.Round(bitsPorSegundo / 1e6).ToString("0", CulturaPtBr)} Mbps";
    }

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
            return "0,00";
        }

        if (valor < 9.995)
        {
            return valor.ToString("0.00", CulturaPtBr);
        }

        if (valor < 99.95)
        {
            return valor.ToString("0.0", CulturaPtBr);
        }

        return Math.Round(valor).ToString("0", CulturaPtBr);
    }
}

