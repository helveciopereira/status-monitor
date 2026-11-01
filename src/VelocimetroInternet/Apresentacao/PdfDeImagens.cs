using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace VelocimetroInternet.Apresentacao;

/// <summary>Uma página já desenhada: pixels RGB (3 bytes por pixel, linha a linha, de cima para baixo).</summary>
public sealed record PaginaRgb(int LarguraPx, int AlturaPx, byte[] Rgb);

/// <summary>
/// v1.4.0 (Onda 9): gravador de PDF MÍNIMO — uma imagem por página A4, sem dependência nenhuma.
///
/// Por que imagem e não texto PDF: o relatório é desenhado pelo próprio WPF (mesma fonte, acentos
/// e layout que a tela), e escrever texto PDF exigiria embutir fonte e medir larguras à mão. Por que
/// sem perda (Flate, o "zip" do PDF) e não JPEG: texto em JPEG fica borrado; uma página quase toda
/// branca comprime muito bem em Flate (poucas centenas de KB).
///
/// Estrutura gerada (PDF 1.4): catálogo → árvore de páginas → para cada página: objeto da página,
/// fluxo de conteúdo ("desenhe a imagem cobrindo a folha") e a imagem; depois Info (título) e a
/// tabela xref com o deslocamento de cada objeto — leitores de PDF exigem esses números exatos.
/// </summary>
public static class PdfDeImagens
{
    /// <summary>A4 em pontos (1/72 pol): 210 × 297 mm.</summary>
    public const double LarguraA4Pt = 595.28;
    public const double AlturaA4Pt = 841.89;

    public static byte[] Gerar(IReadOnlyList<PaginaRgb> paginas, string titulo)
    {
        if (paginas.Count == 0)
        {
            throw new ArgumentException("O PDF precisa de pelo menos uma página.", nameof(paginas));
        }

        var saida = new MemoryStream();
        var deslocamentos = new List<long>(); // índice = número do objeto - 1

        void Escrever(string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s);
            saida.Write(b, 0, b.Length);
        }

        void IniciarObjeto(int numero)
        {
            // Os objetos são escritos em ordem: o deslocamento do nº N vai na posição N-1
            while (deslocamentos.Count < numero)
            {
                deslocamentos.Add(0);
            }

            deslocamentos[numero - 1] = saida.Position;
            Escrever($"{numero} 0 obj\n");
        }

        // Cabeçalho + linha com bytes > 127 (convenção: avisa que o arquivo é binário)
        Escrever("%PDF-1.4\n");
        saida.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]);

        int n = paginas.Count;
        int objInfo = 3 + 3 * n;

        IniciarObjeto(1);
        Escrever("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        IniciarObjeto(2);
        string filhos = string.Join(" ", Enumerable.Range(0, n).Select(i => $"{3 + 3 * i} 0 R"));
        Escrever($"<< /Type /Pages /Kids [{filhos}] /Count {n} >>\nendobj\n");

        string larg = Num(LarguraA4Pt), alt = Num(AlturaA4Pt);
        for (int i = 0; i < n; i++)
        {
            PaginaRgb p = paginas[i];
            if (p.Rgb.Length != p.LarguraPx * p.AlturaPx * 3)
            {
                throw new ArgumentException($"Página {i + 1}: o tamanho dos pixels não bate com {p.LarguraPx}×{p.AlturaPx}.", nameof(paginas));
            }

            int objPagina = 3 + 3 * i, objConteudo = objPagina + 1, objImagem = objPagina + 2;

            IniciarObjeto(objPagina);
            Escrever($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {larg} {alt}] " +
                     $"/Resources << /XObject << /Im0 {objImagem} 0 R >> >> /Contents {objConteudo} 0 R >>\nendobj\n");

            // Matriz "cm": a imagem (unitária) esticada para a folha inteira
            string conteudo = $"q {larg} 0 0 {alt} 0 0 cm /Im0 Do Q\n";
            IniciarObjeto(objConteudo);
            Escrever($"<< /Length {conteudo.Length} >>\nstream\n{conteudo}endstream\nendobj\n");

            byte[] comprimido = Comprimir(p.Rgb);
            IniciarObjeto(objImagem);
            Escrever($"<< /Type /XObject /Subtype /Image /Width {p.LarguraPx} /Height {p.AlturaPx} " +
                     $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length {comprimido.Length} >>\nstream\n");
            saida.Write(comprimido, 0, comprimido.Length);
            Escrever("\nendstream\nendobj\n");
        }

        // Título em UTF-16BE com BOM (aceita acentos: "Relatório…")
        IniciarObjeto(objInfo);
        Escrever($"<< /Title <{TextoHex(titulo)}> /Producer (Status Monitor) >>\nendobj\n");

        long inicioXref = saida.Position;
        Escrever($"xref\n0 {objInfo + 1}\n0000000000 65535 f \n");
        foreach (long d in deslocamentos)
        {
            // Cada entrada tem EXATAMENTE 20 bytes (10 + espaço + 5 + espaço + n + espaço + \n)
            Escrever($"{d:D10} 00000 n \n");
        }

        Escrever($"trailer\n<< /Size {objInfo + 1} /Root 1 0 R /Info {objInfo} 0 R >>\nstartxref\n{inicioXref}\n%%EOF\n");
        return saida.ToArray();
    }

    /// <summary>Flate com cabeçalho zlib (é o formato que o /FlateDecode do PDF espera).</summary>
    private static byte[] Comprimir(byte[] dados)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(dados, 0, dados.Length);
        }

        return ms.ToArray();
    }

    private static string TextoHex(string s)
    {
        var sb = new StringBuilder("FEFF");
        foreach (byte b in Encoding.BigEndianUnicode.GetBytes(s))
        {
            sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
