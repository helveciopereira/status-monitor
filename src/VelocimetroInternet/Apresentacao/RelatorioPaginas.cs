using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VelocimetroInternet.Medicao;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// v1.4.0 (Onda 9): desenha o relatório do mês em folhas A4 com o próprio WPF e devolve os pixels
/// para o <see cref="PdfDeImagens"/>.
///
/// É PAPEL, não tela: fundo branco e cores fixas (não segue o tema escuro), contraste alto para
/// impressão em preto e branco, e a situação de cada teste escrita por extenso (a cor só reforça —
/// impressa em cinza, a informação continua lá).
///
/// Paginação: o conteúdo é uma lista de blocos (título, quadros, linhas de tabela). Cada bloco é
/// medido e vai para a folha atual se couber; senão abre outra. Linhas de tabela que passam para a
/// folha seguinte levam o cabeçalho da tabela junto, e um título nunca fica sozinho no pé da folha.
/// </summary>
public static class RelatorioPaginas
{
    // Folha A4 em DIP (1/96 pol) e a resolução de saída (200 ppp: texto nítido impresso, PDF de poucas centenas de KB)
    public const double LarguraDip = 793.7;
    public const double AlturaDip = 1122.5;
    public const double Dpi = 200;
    private const double Margem = 56;
    private const double AlturaRodape = 84;
    private static double LarguraUtil => LarguraDip - 2 * Margem;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    // Paleta do papel
    private static readonly Brush Texto = Pincel(0x1A, 0x1A, 0x1A);
    private static readonly Brush TextoSec = Pincel(0x5A, 0x5F, 0x66);
    private static readonly Brush Linha = Pincel(0xD9, 0xDC, 0xE1);
    private static readonly Brush Faixa = Pincel(0xF4, 0xF5, 0xF7);
    private static readonly Brush Destaque = Pincel(0x0B, 0x4F, 0xA8);
    private static readonly Brush Bom = Pincel(0x1B, 0x6E, 0x34);
    private static readonly Brush Atencao = Pincel(0x8A, 0x5A, 0x00);
    private static readonly Brush Ruim = Pincel(0xB0, 0x22, 0x1A);
    private static readonly FontFamily Fonte = new("Segoe UI");

    /// <summary>Um pedaço do conteúdo. <paramref name="Cabecalho"/>: repetir no topo se a tabela continuar na folha seguinte.</summary>
    private sealed record Bloco(FrameworkElement Elemento, Func<FrameworkElement>? Cabecalho = null, int ManterComProximos = 0);

    /// <summary>Desenha todas as folhas do relatório.</summary>
    public static List<PaginaRgb> Desenhar(ResumoMes r, DateTime geradoEm, string versaoApp)
    {
        List<Bloco> blocos = Montar(r, geradoEm, versaoApp);
        List<List<FrameworkElement>> folhas = Paginar(blocos);
        var paginas = new List<PaginaRgb>(folhas.Count);
        for (int i = 0; i < folhas.Count; i++)
        {
            paginas.Add(Rasterizar(MontarFolha(folhas[i], i + 1, folhas.Count, r)));
        }

        return paginas;
    }

    // ------------------------------------------------------------------ conteúdo

    private static List<Bloco> Montar(ResumoMes r, DateTime geradoEm, string versaoApp)
    {
        var b = new List<Bloco>();

        // Cabeçalho
        var topo = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        topo.Children.Add(T("Relatório de qualidade da internet", 22, FontWeights.SemiBold));
        topo.Children.Add(T(PrimeiraMaiuscula(r.NomeMes), 15, FontWeights.Normal, Destaque, new Thickness(0, 2, 0, 8)));
        string plano = r.PlanoMbps > 0 ? $"{r.PlanoMbps.ToString("0.##", PtBr)} Mbps de download" : "não informado (configure no estúdio › Medição)";
        topo.Children.Add(Par("Plano contratado", plano));
        topo.Children.Add(Par("Computador", Environment.MachineName));
        topo.Children.Add(Par("Tempo observado", r.TempoObservado > TimeSpan.Zero
            ? $"{CalculoRelatorio.Duracao(r.TempoObservado)} em {r.DiasObservados} dia(s) do mês"
            : "nenhum (o app não estava aberto neste mês)"));
        topo.Children.Add(Par("Gerado em", $"{geradoEm.ToString("dd/MM/yyyy 'às' HH:mm", PtBr)} · Status Monitor {versaoApp}"));
        b.Add(new Bloco(topo));

        // Quatro números principais
        b.Add(new Bloco(Indicadores(r)));

        // Conclusão
        var conclusao = new Border
        {
            Background = Faixa,
            BorderBrush = Linha,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 22),
        };
        var lista = new StackPanel();
        lista.Children.Add(T("Conclusão", 12, FontWeights.SemiBold, Texto, new Thickness(0, 0, 0, 4)));
        foreach (string frase in r.Conclusoes)
        {
            lista.Children.Add(T("•  " + frase, 11, FontWeights.Normal, Texto, new Thickness(0, 2, 0, 0)));
        }

        conclusao.Child = lista;
        b.Add(new Bloco(conclusao));

        // Testes de velocidade
        // v1.5.0 (Onda 10): + coluna "Link" (velocidade do PC com o roteador) e banda do Wi-Fi na "Conexão"
        double[] colTestes = [100, 80, 78, 74, 74, 56, 60, 0]; // 0 = o que sobrar
        FrameworkElement CabTestes() => LinhaTabela(colTestes, true, false,
            "Data e hora", "Conexão", "Link", "Download", "Upload", "Latência", "% plano", "Situação");
        b.Add(new Bloco(Secao("Testes de velocidade", $"{r.Testes.Count} no mês · servidor speed.cloudflare.com"), ManterComProximos: 2));
        if (r.Testes.Count == 0)
        {
            b.Add(new Bloco(T("Nenhum teste neste mês. Use \"Testar velocidade da internet…\" no menu do Status Monitor — de preferência por cabo, em horários diferentes.", 11, FontWeights.Normal, TextoSec, new Thickness(0, 0, 0, 22))));
        }
        else
        {
            b.Add(new Bloco(CabTestes()));
            for (int i = 0; i < r.Testes.Count; i++)
            {
                TesteGuardado t = r.Testes[i];
                FaixaAnatel faixa = CalculoRelatorio.Classificar(t.DownloadBps, r.PlanoMbps);
                string pct = r.PlanoMbps > 0 ? CalculoRelatorio.Pct(t.DownloadBps / (r.PlanoMbps * 1_000_000)) : "—";
                (string situacao, Brush cor) = faixa switch
                {
                    FaixaAnatel.Dentro => ("OK (80 % ou mais)", Bom),
                    FaixaAnatel.AbaixoDaMedia => ("Abaixo de 80 %", Atencao),
                    FaixaAnatel.AbaixoDoMinimo => ("ABAIXO DE 40 %", Ruim),
                    _ => ("—", TextoSec),
                };
                // Link abaixo do plano ganha "*" (explicado na nota logo abaixo da tabela)
                ConexaoNoTeste con = t.ConexaoCompleta;
                string link = con.LinkDownMbps is double l && l > 0
                    ? ConexaoNoTeste.Link(l) + (con.LimitaODownload(r.PlanoMbps) ? " *" : "")
                    : "—";
                FrameworkElement linha = LinhaTabela(colTestes, false, i % 2 == 1,
                    t.Quando.ToString("dd/MM/yyyy HH:mm", PtBr), con.Banda is null ? con.Tipo : $"{con.Tipo} {con.Banda}", link,
                    CalculoRelatorio.Mbps(t.DownloadBps), CalculoRelatorio.Mbps(t.UploadBps),
                    $"{t.LatenciaMs.ToString("0", PtBr)} ms", pct, situacao);
                ((TextBlock)((Grid)((Border)linha).Child).Children[^1]).Foreground = cor;
                b.Add(new Bloco(linha, CabTestes));
            }

            if (r.TestesLimitadosPeloLink > 0)
            {
                b.Add(new Bloco(T("* Link do computador com o roteador mais lento que o plano: esse teste não podia chegar ao contratado, qualquer que fosse a operadora. \"—\" = link não registrado (testes antigos).",
                    9.5, FontWeights.Normal, TextoSec, new Thickness(0, 6, 0, 0))));
            }

            b.Add(new Bloco(new Border { Height = 22 }));
        }

        // Quedas de internet
        double[] colQuedas = [150, 150, 0];
        FrameworkElement CabQuedas() => LinhaTabela(colQuedas, true, false, "Início", "Fim", "Duração");
        b.Add(new Bloco(Secao("Quedas de internet",
            $"{r.QuedasInternet.Count} no mês · total {CalculoRelatorio.Duracao(r.TempoSemInternet)} · computador conectado ao roteador, Windows sem acesso à internet"), ManterComProximos: 2));
        if (r.QuedasInternet.Count == 0)
        {
            b.Add(new Bloco(T(r.TempoObservado > TimeSpan.Zero
                    ? "Nenhuma queda de 1 minuto ou mais no tempo observado."
                    : "Sem tempo observado neste mês.", 11, FontWeights.Normal, TextoSec, new Thickness(0, 0, 0, 16))));
        }
        else
        {
            b.Add(new Bloco(CabQuedas()));
            for (int i = 0; i < r.QuedasInternet.Count; i++)
            {
                Queda q = r.QuedasInternet[i];
                b.Add(new Bloco(LinhaTabela(colQuedas, false, i % 2 == 1,
                    q.Inicio.ToString("dd/MM/yyyy HH:mm", PtBr), q.Fim.ToString("dd/MM/yyyy HH:mm", PtBr),
                    CalculoRelatorio.Duracao(q.Duracao)), CabQuedas));
            }

            b.Add(new Bloco(new Border { Height = 16 }));
        }

        if (r.QuedasRede.Count > 0)
        {
            b.Add(new Bloco(T($"Fora da conta da operadora: {r.QuedasRede.Count} período(s) com o computador desconectado do roteador (Wi-Fi desligado, cabo solto ou roteador desligado), somando {CalculoRelatorio.Duracao(r.TempoSemRede)}.",
                10.5, FontWeights.Normal, TextoSec, new Thickness(0, 0, 0, 16))));
        }

        // Como usar
        var uso = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        uso.Children.Add(T("Como usar este relatório", 12, FontWeights.SemiBold, Texto, new Thickness(0, 0, 0, 4)));
        uso.Children.Add(T("1.  Reclame primeiro na operadora e anote o número do protocolo.", 11, FontWeights.Normal, Texto, new Thickness(0, 2, 0, 0)));
        uso.Children.Add(T("2.  Sem solução, registre a reclamação na Anatel (telefone 1331 ou gov.br/anatel), informe o protocolo da operadora e anexe este relatório.", 11, FontWeights.Normal, Texto, new Thickness(0, 2, 0, 0)));
        uso.Children.Add(T("3.  Testes por cabo, em vários dias e horários, têm mais peso que testes isolados ou por Wi-Fi.", 11, FontWeights.Normal, Texto, new Thickness(0, 2, 0, 0)));
        b.Add(new Bloco(uso));

        return b;
    }

    private static FrameworkElement Indicadores(ResumoMes r)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        for (int i = 0; i < 4; i++)
        {
            if (i > 0)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            }

            g.ColumnDefinitions.Add(new ColumnDefinition());
        }

        string media = r.MediaDownloadBps is double md ? CalculoRelatorio.Mbps(md) : "—";
        string mediaSub = r.FracaoMedia is double f ? $"{CalculoRelatorio.Pct(f)} do plano (meta: 80 %)"
            : r.Testes.Count == 0 ? "sem testes no mês" : "plano não informado";
        Brush mediaCor = r.FracaoMedia is double f2 ? (f2 >= CalculoRelatorio.MinimoMedia ? Bom : Ruim) : Texto;

        string disp = r.Disponibilidade is double d
            ? $"{(Math.Floor(d * 1000) / 10).ToString("0.0", PtBr)} %"
            : "—";

        (string Rotulo, string Valor, string Sub, Brush Cor)[] cartoes =
        [
            ("Média de download", media, mediaSub, mediaCor),
            ("Testes", r.Testes.Count.ToString(PtBr), $"{r.TestesAbaixoDoMinimo} abaixo de 40 % · {r.TestesPorWiFi} por Wi-Fi", Texto),
            ("Quedas de internet", r.QuedasInternet.Count.ToString(PtBr), $"total {CalculoRelatorio.Duracao(r.TempoSemInternet)}", r.QuedasInternet.Count > 0 ? Ruim : Texto),
            ("Internet disponível", disp, "no tempo observado com o roteador conectado", Texto),
        ];

        for (int i = 0; i < cartoes.Length; i++)
        {
            var c = new Border
            {
                BorderBrush = Linha,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 9),
            };
            var s = new StackPanel();
            s.Children.Add(T(cartoes[i].Rotulo, 10, FontWeights.Normal, TextoSec));
            s.Children.Add(T(cartoes[i].Valor, 19, FontWeights.SemiBold, cartoes[i].Cor, new Thickness(0, 2, 0, 1)));
            s.Children.Add(T(cartoes[i].Sub, 9.5, FontWeights.Normal, TextoSec));
            c.Child = s;
            Grid.SetColumn(c, i * 2);
            g.Children.Add(c);
        }

        return g;
    }

    private static FrameworkElement Secao(string titulo, string sub)
    {
        var s = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        s.Children.Add(T(titulo, 14, FontWeights.SemiBold));
        s.Children.Add(T(sub, 10, FontWeights.Normal, TextoSec, new Thickness(0, 1, 0, 0)));
        return s;
    }

    /// <summary>Linha de tabela com colunas fixas (0 = estrela). A partir da 3ª coluna, números alinhados à direita.</summary>
    private static FrameworkElement LinhaTabela(double[] larguras, bool cabecalho, bool listrada, params string[] celulas)
    {
        var g = new Grid();
        foreach (double l in larguras)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = l > 0 ? new GridLength(l) : new GridLength(1, GridUnitType.Star) });
        }

        for (int i = 0; i < celulas.Length; i++)
        {
            bool numero = i >= 2 && i < celulas.Length - 1 && larguras.Length > 3;
            TextBlock t = T(celulas[i], cabecalho ? 9.5 : 10.5, cabecalho ? FontWeights.SemiBold : FontWeights.Normal,
                cabecalho ? TextoSec : Texto);
            t.TextWrapping = TextWrapping.NoWrap;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            t.Margin = new Thickness(numero ? 0 : 6, 0, numero ? 12 : 6, 0);
            t.HorizontalAlignment = numero ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            Grid.SetColumn(t, i);
            g.Children.Add(t);
        }

        return new Border
        {
            Child = g,
            Padding = new Thickness(0, cabecalho ? 5 : 4, 0, cabecalho ? 5 : 4),
            Background = listrada ? Faixa : Brushes.Transparent,
            BorderBrush = cabecalho ? Texto : Linha,
            BorderThickness = new Thickness(0, 0, 0, cabecalho ? 1 : 0.5),
        };
    }

    private static FrameworkElement Par(string rotulo, string valor)
    {
        var t = new TextBlock { FontFamily = Fonte, FontSize = 11, Foreground = Texto, Margin = new Thickness(0, 1, 0, 0), TextWrapping = TextWrapping.Wrap };
        t.Inlines.Add(new System.Windows.Documents.Run(rotulo + ": ") { Foreground = TextoSec });
        t.Inlines.Add(new System.Windows.Documents.Run(valor));
        return t;
    }

    private static TextBlock T(string texto, double tamanho, FontWeight peso, Brush? cor = null, Thickness? margem = null) => new()
    {
        Text = texto,
        FontFamily = Fonte,
        FontSize = tamanho,
        FontWeight = peso,
        Foreground = cor ?? Texto,
        TextWrapping = TextWrapping.Wrap,
        Margin = margem ?? new Thickness(0),
    };

    // ------------------------------------------------------------------ paginação e desenho

    private static List<List<FrameworkElement>> Paginar(List<Bloco> blocos)
    {
        double alturaUtil = AlturaDip - 2 * Margem - AlturaRodape;
        var folhas = new List<List<FrameworkElement>> { new() };
        double usado = 0;

        double Altura(FrameworkElement e)
        {
            e.Measure(new Size(LarguraUtil, double.PositiveInfinity));
            return e.DesiredSize.Height;
        }

        for (int i = 0; i < blocos.Count; i++)
        {
            Bloco bloco = blocos[i];
            double h = Altura(bloco.Elemento);
            // "Manter com os próximos": um título só entra se couber junto com o cabeçalho e a 1ª linha
            double junto = h;
            for (int k = 1; k <= bloco.ManterComProximos && i + k < blocos.Count; k++)
            {
                junto += Altura(blocos[i + k].Elemento);
            }

            if (usado > 0 && usado + junto > alturaUtil)
            {
                folhas.Add([]);
                usado = 0;
                // Tabela continuando: o cabeçalho dela abre a folha nova
                if (bloco.Cabecalho is not null)
                {
                    FrameworkElement cab = bloco.Cabecalho();
                    folhas[^1].Add(cab);
                    usado += Altura(cab);
                }
            }

            folhas[^1].Add(bloco.Elemento);
            usado += h;
        }

        return folhas;
    }

    private static FrameworkElement MontarFolha(List<FrameworkElement> conteudo, int pagina, int total, ResumoMes r)
    {
        var corpo = new StackPanel { Width = LarguraUtil };
        foreach (FrameworkElement e in conteudo)
        {
            corpo.Children.Add(e);
        }

        var rodape = new StackPanel { Width = LarguraUtil, VerticalAlignment = VerticalAlignment.Bottom };
        rodape.Children.Add(new Border { Height = 1, Background = Linha, Margin = new Thickness(0, 0, 0, 6) });
        rodape.Children.Add(T("Referência: metas da Anatel para banda larga fixa — pelo menos 40 % da velocidade contratada em cada medição e 80 % na média do mês. " +
                              "Medições feitas por este computador com o servidor speed.cloudflare.com, por iniciativa do usuário; não substituem a medição oficial da Anatel. " +
                              "Quedas: estado \"sem internet\" informado pelo Windows, com o computador conectado ao roteador, por 1 minuto ou mais.",
            8.5, FontWeights.Normal, TextoSec));
        rodape.Children.Add(T($"{PrimeiraMaiuscula(r.NomeMes)} · página {pagina} de {total}", 8.5, FontWeights.Normal, TextoSec, new Thickness(0, 4, 0, 0)));

        var folha = new Grid { Width = LarguraDip, Height = AlturaDip, Background = Brushes.White };
        corpo.Margin = new Thickness(Margem, Margem, Margem, 0);
        corpo.VerticalAlignment = VerticalAlignment.Top;
        rodape.Margin = new Thickness(Margem, 0, Margem, Margem * 0.6);
        folha.Children.Add(corpo);
        folha.Children.Add(rodape);
        TextOptions.SetTextFormattingMode(folha, TextFormattingMode.Ideal); // texto de papel, não de tela
        return folha;
    }

    private static PaginaRgb Rasterizar(FrameworkElement folha)
    {
        folha.Measure(new Size(LarguraDip, AlturaDip));
        folha.Arrange(new Rect(0, 0, LarguraDip, AlturaDip));
        folha.UpdateLayout();

        int lpx = (int)Math.Round(LarguraDip * Dpi / 96), apx = (int)Math.Round(AlturaDip * Dpi / 96);
        var rtb = new RenderTargetBitmap(lpx, apx, Dpi, Dpi, PixelFormats.Pbgra32);
        rtb.Render(folha);

        byte[] bgra = new byte[lpx * apx * 4];
        rtb.CopyPixels(bgra, lpx * 4, 0);
        byte[] rgb = new byte[lpx * apx * 3];
        for (int s = 0, d = 0; s < bgra.Length; s += 4, d += 3)
        {
            // Fundo branco opaco: alfa sempre 255, então o BGRA pré-multiplicado já é a cor final
            rgb[d] = bgra[s + 2];
            rgb[d + 1] = bgra[s + 1];
            rgb[d + 2] = bgra[s];
        }

        return new PaginaRgb(lpx, apx, rgb);
    }

    private static string PrimeiraMaiuscula(string s) => s.Length == 0 ? s : char.ToUpper(s[0], PtBr) + s[1..];

    private static SolidColorBrush Pincel(byte r, byte g, byte b)
    {
        var p = new SolidColorBrush(Color.FromRgb(r, g, b));
        p.Freeze();
        return p;
    }
}
