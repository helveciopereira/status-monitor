using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using VelocimetroInternet.Config;
using VelocimetroInternet.Medicao;
using VelocimetroInternet.Recursos;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// Estúdio de Cores &amp; Configurações (Onda 3). Trabalha numa cópia (<see cref="_rascunho"/>):
/// a pré-visualização muda na hora, a barra só muda ao clicar "Aplicar".
/// </summary>
public partial class EstudioCores : Window
{
    /// <summary>v1.9.0: números digitados/mostrados no idioma da interface (antes era pt-BR fixo).</summary>
    private static CultureInfo Cultura => Idioma.Cultura;

    private readonly ServicoConfiguracoes _servico;
    private Configuracoes _rascunho;
    private bool _carregando;
    private readonly Dictionary<string, RadioButton> _cartoes = [];

    /// <summary>v1.0.0: histórico de consumo (exportar CSV e mostrar o mês). null = sem histórico (testes).</summary>
    private readonly HistoricoRede? _historico;

    /// <param name="textoVersao">Rodapé (v0.4.0), ex.: "Versão 0.4.3 · auto update".</param>
    /// <param name="historico">v1.0.0: para "Exportar CSV" e o consumo do mês ao lado da franquia.</param>
    public EstudioCores(ServicoConfiguracoes servico, string textoVersao = "", HistoricoRede? historico = null)
    {
        InitializeComponent();
        TextoVersao.Text = textoVersao;
        _historico = historico;
        BotaoExportar.IsEnabled = historico is not null;
        _servico = servico;
        _rascunho = servico.Atual.Clonar();
        Seletor.CorAlterada += AoMoverSeletor; // v0.9.0: seletor visual de cor
        MontarGaleria();
        CarregarNosControles();
        SourceInitialized += (_, _) => AplicarBarraDeTituloDoTema();
    }

    // ------------------------------------------------------------------ montagem

    /// <summary>Um cartão por paleta: nome, descrição e as duas amostras de cor (como no design).</summary>
    private void MontarGaleria()
    {
        IEnumerable<PaletaCores> todas = Paletas.Predefinidas.Append(
            new PaletaCores(Paletas.IdPersonalizada, Textos.Estudio_PaletaPersonalizada, Textos.Estudio_PaletaPersonalizadaDescricao,
                Paletas.Hex(_rascunho.CorUploadPersonalizada), Paletas.Hex(_rascunho.CorDownloadPersonalizada)));

        foreach (PaletaCores p in todas)
        {
            // v1.9.0: nome/descrição exibidos no idioma da interface (o Id continua sendo o que se grava)
            var conteudo = new StackPanel();
            conteudo.Children.Add(new TextBlock { Text = Paletas.NomeExibido(p), FontWeight = FontWeights.SemiBold });
            conteudo.Children.Add(new TextBlock
            {
                Text = Paletas.DescricaoExibida(p),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 8),
                Height = 30
            }.ComRecurso(TextBlock.ForegroundProperty, "Tema.TextoSecundario"));

            var amostras = new StackPanel { Orientation = Orientation.Horizontal };
            amostras.Children.Add(Amostra("↑", p.UploadEscuro));
            amostras.Children.Add(Amostra("↓", p.DownloadEscuro));
            conteudo.Children.Add(amostras);

            var cartao = new RadioButton
            {
                GroupName = "Paleta",
                Style = (Style)FindResource("CartaoPaleta"),
                Content = conteudo,
                Tag = p.Id
            };
            System.Windows.Automation.AutomationProperties.SetName(cartao, Textos.Formatar(Textos.Estudio_PaletaNomeUia, Paletas.NomeExibido(p)));
            cartao.Checked += AoEscolherPaleta;
            _cartoes[p.Id] = cartao;
            Galeria.Children.Add(cartao);
        }
    }

    /// <summary>Retângulo de cor com seta + hex, sobre a barra escura (onde a paleta é definida).</summary>
    private static Border Amostra(string seta, Color cor) => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(6, 2, 6, 2),
        Margin = new Thickness(0, 0, 6, 0),
        Child = new TextBlock
        {
            Text = $"{seta} {Paletas.ParaHex(cor)}",
            Foreground = new SolidColorBrush(cor),
            FontSize = 11,
            FontFamily = new FontFamily("Cascadia Mono, Consolas")
        }
    };

    private void CarregarNosControles()
    {
        _carregando = true;
        try
        {
            if (_cartoes.TryGetValue(_rascunho.Paleta, out RadioButton? c))
            {
                c.IsChecked = true;
            }
            else
            {
                _cartoes["classico"].IsChecked = true;
            }

            CampoCorUpload.Text = _rascunho.CorUploadPersonalizada;
            CampoCorDownload.Text = _rascunho.CorDownloadPersonalizada;
            ControleSuavizacao.Value = _rascunho.Suavizacao;
            CampoPlano.Text = _rascunho.VelocidadeContratadaMbps > 0
                ? _rascunho.VelocidadeContratadaMbps.ToString("0.##", Cultura)
                : string.Empty;
            CampoExcluir.Text = string.Join(Environment.NewLine, _rascunho.InterfacesExcluir);
            ChaveBandeja.IsChecked = _rascunho.MostrarIconeBandeja || !_rascunho.FixarNaBarra;
            ChaveBandeja.IsEnabled = _rascunho.FixarNaBarra;

            // v0.8.0: opções da barra (cada grupo de segmentos marca o botão cujo Tag bate)
            MarcarSegmento(GrupoTamanho, _rascunho.TamanhoTexto);
            MarcarSegmento(GrupoFonte, _rascunho.Fonte);
            MarcarSegmento(GrupoOrdem, _rascunho.DownloadEmCima ? "download" : "envio");
            MarcarSegmento(GrupoModo, _rascunho.ModoExibicao); // v1.2.0
            MarcarSegmento(GrupoIntervalo, _rascunho.IntervaloSegundos.ToString(CultureInfo.InvariantCulture));
            MarcarSegmento(GrupoIdioma, _rascunho.Idioma); // v1.9.0
            ChaveMonitores.IsChecked = _rascunho.MostrarEmTodasAsBarras;
            ChaveMiniGrafico.IsChecked = _rascunho.MiniGrafico; // v1.7.0

            // v1.0.0 (Onda 5)
            ChaveIntensidade.IsChecked = _rascunho.CorPorIntensidade;
            ChaveLatencia.IsChecked = _rascunho.MedirLatencia; // v1.1.0
            ChaveAvisoVolta.IsChecked = _rascunho.AvisarQuandoInternetVoltar; // v1.8.1
            CampoFranquia.Text = _rascunho.FranquiaMensalGb > 0
                ? _rascunho.FranquiaMensalGb.ToString("0.##", Cultura)
                : string.Empty;

            // v1.9.1: memória
            ChaveRenderSoftware.IsChecked = _rascunho.RenderizarPorSoftware;
            ChaveReinicio.IsChecked = _rascunho.ReinicioAutomatico;
            CampoTetoMemoria.Text = _rascunho.ReinicioTetoMb.ToString(Cultura);
            LinhaTeto.IsEnabled = _rascunho.ReinicioAutomatico;
        }
        finally
        {
            _carregando = false;
        }

        AtualizarPreVisualizacao();
        SincronizarSeletor();
        AtualizarTextoSuavizacao();
        AtualizarInterfacesAtivas();
        AtualizarConsumoMes();
        AtualizarEstadoSalvo();
    }

    /// <summary>v1.0.0: "Este mês: 8,2 GB · 82 % da franquia" abaixo do campo (com o valor do rascunho).</summary>
    private void AtualizarConsumoMes()
    {
        if (_historico is null)
        {
            TextoConsumoMes.Text = string.Empty;
            return;
        }

        InstantaneoHistorico h = _historico.Capturar();
        ulong total = h.BytesEnviadosMes + h.BytesRecebidosMes;
        string texto = Textos.Formatar(Textos.Estudio_EsteMesAteAgora, SpeedFormatter.FormatarQuantidade(total));
        if (_rascunho.FranquiaMensalGb > 0)
        {
            texto += Textos.Formatar(Textos.Estudio_DaFranquiaSufixo, DicaWidget.TextoPercentual(total / (_rascunho.FranquiaMensalGb * 1_000_000_000d)));
        }

        TextoConsumoMes.Text = texto;
    }

    private void AoMudarFranquia(object sender, TextChangedEventArgs e)
    {
        if (_carregando)
        {
            return;
        }

        // Mesmo critério do campo do plano: vazio = 0; texto inválido mantém o último valor bom
        string t = CampoFranquia.Text.Trim();
        if (t.Length == 0)
        {
            _rascunho.FranquiaMensalGb = 0;
        }
        else if (double.TryParse(t, NumberStyles.Number, Cultura, out double v) && v >= 0)
        {
            _rascunho.FranquiaMensalGb = Math.Min(v, 100_000);
        }

        AtualizarConsumoMes();
        AtualizarEstadoSalvo();
    }

    private void AoAlternarIntensidade(object sender, RoutedEventArgs e)
    {
        _rascunho.CorPorIntensidade = ChaveIntensidade.IsChecked == true;
        AtualizarEstadoSalvo();
    }

    /// <summary>v1.7.0 (Onda 13): mini gráfico atrás dos números; a amostra do estúdio já o mostra.</summary>
    private void AoAlternarMiniGrafico(object sender, RoutedEventArgs e)
    {
        _rascunho.MiniGrafico = ChaveMiniGrafico.IsChecked == true;
        AtualizarPreVisualizacao();
        AtualizarEstadoSalvo();
    }

    /// <summary>v1.1.0: liga/desliga o ping (vale ao salvar, como as demais opções).</summary>
    private void AoAlternarLatencia(object sender, RoutedEventArgs e)
    {
        _rascunho.MedirLatencia = ChaveLatencia.IsChecked == true;
        AtualizarEstadoSalvo();
    }

    /// <summary>v1.8.1 (Onda 14): notificação ao fim de uma queda (vale ao salvar).</summary>
    private void AoAlternarAvisoVolta(object sender, RoutedEventArgs e)
    {
        _rascunho.AvisarQuandoInternetVoltar = ChaveAvisoVolta.IsChecked == true;
        AtualizarEstadoSalvo();
    }

    /// <summary>v1.9.1: desenho pela CPU. O App troca o modo ao aplicar, mas o ganho pleno vem ao reabrir (a nota avisa).</summary>
    private void AoAlternarRenderSoftware(object sender, RoutedEventArgs e)
    {
        _rascunho.RenderizarPorSoftware = ChaveRenderSoftware.IsChecked == true;
        AtualizarEstadoSalvo();
    }

    /// <summary>v1.9.1: reinício automático por teto de memória; o campo do teto só faz sentido ligado.</summary>
    private void AoAlternarReinicio(object sender, RoutedEventArgs e)
    {
        _rascunho.ReinicioAutomatico = ChaveReinicio.IsChecked == true;
        LinhaTeto.IsEnabled = _rascunho.ReinicioAutomatico;
        AtualizarEstadoSalvo();
    }

    /// <summary>v1.9.1: teto em MB. Mesmo critério da franquia: fora da faixa 100–4 000 mantém o último valor bom.</summary>
    private void AoMudarTetoMemoria(object sender, TextChangedEventArgs e)
    {
        if (_carregando)
        {
            return;
        }

        if (int.TryParse(CampoTetoMemoria.Text.Trim(), NumberStyles.Integer, Cultura, out int v)
            && v is >= Configuracoes.ReinicioTetoMinimoMb and <= Configuracoes.ReinicioTetoMaximoMb)
        {
            _rascunho.ReinicioTetoMb = v;
        }

        AtualizarEstadoSalvo();
    }

    /// <summary>
    /// Salva o histórico diário em CSV (";" e vírgula decimal, como o Excel em português espera).
    /// UTF-8 COM BOM: sem ele, o Excel abre "Mês"/"Data" com acentos quebrados.
    /// </summary>
    private void AoExportarCsv(object sender, RoutedEventArgs e)
    {
        if (_historico is null)
        {
            return;
        }

        var dialogo = new Microsoft.Win32.SaveFileDialog
        {
            Title = Textos.Estudio_ExportarConsumoPorDia,
            FileName = $"status-monitor-consumo-{DateTime.Now:yyyy-MM-dd}.csv",
            DefaultExt = ".csv",
            Filter = Textos.Comum_FiltroCsv,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialogo.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            System.IO.File.WriteAllText(dialogo.FileName, _historico.ExportarCsv(), new System.Text.UTF8Encoding(true));
            TextoEstadoSalvo.Text = Textos.Estudio_CsvExportado;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            TextoEstadoSalvo.Text = Textos.Formatar(Textos.Estudio_CsvFalhou, ex.Message);
        }
    }

    /// <summary>Marca, num grupo de segmentos, o RadioButton cujo Tag é <paramref name="valor"/>.</summary>
    private static void MarcarSegmento(Panel grupo, string valor)
    {
        foreach (RadioButton rb in grupo.Children.OfType<RadioButton>())
        {
            rb.IsChecked = string.Equals(rb.Tag as string, valor, StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------ pré-visualização

    private void AtualizarPreVisualizacao()
    {
        bool personalizada = _rascunho.Paleta == Paletas.IdPersonalizada;
        PainelPersonalizada.Visibility = personalizada ? Visibility.Visible : Visibility.Collapsed;

        PaletaCores paleta = Paletas.Obter(_rascunho.Paleta, _rascunho.CorUploadPersonalizada, _rascunho.CorDownloadPersonalizada);
        CoresResolvidas escura = Paletas.Resolver(paleta, barraClara: false);
        CoresResolvidas clara = Paletas.Resolver(paleta, barraClara: true);

        // v0.8.0: a amostra também mostra fonte, tamanho e ordem escolhidos
        var aparencia = new AparenciaWidget(_rascunho.Fonte, _rascunho.TamanhoTexto, _rascunho.DownloadEmCima);
        // v1.2.0: e o modo de exibição (duas linhas / uma linha / só download)
        ModoExibicaoWidget modo = ModosExibicao.DeCodigo(_rascunho.ModoExibicao);
        AmostraEscura.Content = MiniWidget(escura, aparencia, modo, _rascunho.MiniGrafico);
        AmostraClara.Content = MiniWidget(clara, aparencia, modo, _rascunho.MiniGrafico);
        PreencherSelo(SeloEscuro, TextoSeloEscuro, Math.Min(escura.ContrasteUpload, escura.ContrasteDownload));
        PreencherSelo(SeloClaro, TextoSeloClaro, Math.Min(clara.ContrasteUpload, clara.ContrasteDownload));
    }

    /// <summary>v1.9.0: números de exemplo das amostras no idioma da interface ("1,24" / "1.24").</summary>
    private static string ExemploUp => 1.24.ToString("0.00", Cultura);
    private static string ExemploDown => 38.7.ToString("0.0", Cultura);

    /// <summary>Réplica do widget (2 pílulas) com as cores resolvidas — mesmo desenho da barra.</summary>
    private static UIElement MiniWidget(CoresResolvidas c, AparenciaWidget a, ModoExibicaoWidget modo, bool grafico)
    {
        // A largura acompanha o fator da fonte/tamanho, como o widget real faz na barra
        var pilha = new StackPanel { Width = Math.Round(104 * a.FatorLargura()) };
        FontFamily familia = AparenciaWidget.FamiliaDe(a.Fonte);
        double tamanho = AparenciaWidget.TamanhoDe(a.Tamanho);

        // v1.2.0: amostra de cada modo, com o mesmo desenho da barra
        if (modo == ModoExibicaoWidget.SoDownload)
        {
            pilha.Children.Add(Pilula("↓", $"{ExemploDown} MB/s", c.Download, c.FundoPilulaDownload, c.BordaPilulaDownload, familia, tamanho,
                grafico ? c.MascaraGrafico : null, ExemploDownload));
            return pilha;
        }

        if (modo == ModoExibicaoWidget.UmaLinha)
        {
            return LinhaUnica(c, a, familia, tamanho - 0.5);
        }

        Border envio = Pilula("↑", $"{ExemploUp} MB/s", c.Upload, c.FundoPilulaUpload, c.BordaPilulaUpload, familia, tamanho,
            grafico ? c.MascaraGrafico : null, ExemploUpload);
        Border download = Pilula("↓", $"{ExemploDown} MB/s", c.Download, c.FundoPilulaDownload, c.BordaPilulaDownload, familia, tamanho,
            grafico ? c.MascaraGrafico : null, ExemploDownload);
        pilha.Children.Add(a.DownloadEmCima ? download : envio);
        pilha.Children.Add(new Border { Height = 2 });
        pilha.Children.Add(a.DownloadEmCima ? envio : download);
        return pilha;
    }

    /// <summary>v1.2.0: amostra da linha única "↑ 1,24 | ↓ 38,7 MB/s" (como o modo compacto da barra).</summary>
    private static UIElement LinhaUnica(CoresResolvidas c, AparenciaWidget a, FontFamily familia, double tamanho)
    {
        TextBlock Texto(string t, Color cor) => new()
        {
            Text = t,
            Foreground = new SolidColorBrush(cor),
            FontWeight = FontWeights.SemiBold,
            FontSize = tamanho,
            FontFamily = familia,
            VerticalAlignment = VerticalAlignment.Center
        };

        var linha = new StackPanel { Orientation = Orientation.Horizontal };
        TextBlock envio = Texto(a.DownloadEmCima ? $"↑ {ExemploUp} MB/s" : $"↑ {ExemploUp}", c.Upload);
        TextBlock download = Texto(a.DownloadEmCima ? $"↓ {ExemploDown}" : $"↓ {ExemploDown} MB/s", c.Download);
        linha.Children.Add(a.DownloadEmCima ? download : envio);
        linha.Children.Add(Texto("  |  ", c.Separador));
        linha.Children.Add(a.DownloadEmCima ? envio : download);
        return new Border
        {
            Background = new SolidColorBrush(c.FundoCompacto),
            BorderBrush = new SolidColorBrush(c.BordaCompacto),
            BorderThickness = new Thickness(0.75),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = linha
        };
    }

    /// <summary>v1.7.0: um minuto de exemplo para a amostra (download com um pico; envio quase parado).</summary>
    private static readonly IReadOnlyList<(double X, double Y)> ExemploDownload = Exemplo(i => 0.25 + 0.6 * Math.Exp(-Math.Pow((i - 40) / 6.0, 2)) + 0.08 * Math.Sin(i * 0.9));
    private static readonly IReadOnlyList<(double X, double Y)> ExemploUpload = Exemplo(i => 0.12 + 0.18 * Math.Exp(-Math.Pow((i - 22) / 4.0, 2)) + 0.05 * Math.Sin(i * 1.3));

    private static List<(double X, double Y)> Exemplo(Func<int, double> f) =>
        [.. Enumerable.Range(0, 61).Select(i => (i / 60.0, Math.Clamp(f(i), 0, 1)))];

    private static Border Pilula(string seta, string valor, Color texto, Color fundo, Color borda,
        FontFamily familia, double tamanho,
        Color? corGrafico = null, IReadOnlyList<(double X, double Y)>? pontos = null)
    {
        var grade = new Grid { Margin = new Thickness(6, 1, 6, 1) }; // v1.7.0: respiro saiu do Padding
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grade.ColumnDefinitions.Add(new ColumnDefinition());
        var pincel = new SolidColorBrush(texto);
        grade.Children.Add(new TextBlock { Text = seta, Foreground = pincel, FontWeight = FontWeights.Bold, FontSize = tamanho, FontFamily = familia });
        var t = new TextBlock
        {
            Text = valor,
            Foreground = pincel,
            FontWeight = FontWeights.SemiBold,
            FontSize = tamanho,
            FontFamily = familia,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(t, 1);
        grade.Children.Add(t);
        return new Border
        {
            Background = new SolidColorBrush(fundo),
            BorderBrush = new SolidColorBrush(borda),
            BorderThickness = new Thickness(0.75),
            CornerRadius = new CornerRadius(4),
            Child = ComGrafico(grade, corGrafico, pontos)
        };
    }

    /// <summary>v1.7.0: põe o mini gráfico atrás do conteúdo da pílula (igual ao widget da barra).</summary>
    private static UIElement ComGrafico(UIElement conteudo, Color? cor, IReadOnlyList<(double X, double Y)>? pontos)
    {
        if (cor is null || pontos is null)
        {
            return conteudo;
        }

        var grafico = new MiniGrafico();
        grafico.DefinirCor(cor.Value);
        grafico.DefinirPontos(pontos);
        var camadas = new Grid();
        camadas.Children.Add(grafico);
        camadas.Children.Add(conteudo);
        return camadas;
    }

    /// <summary>Selo "Contraste 9,2:1 (AAA)" — verde se passa AA, âmbar se não.</summary>
    private static void PreencherSelo(Border selo, TextBlock texto, double razao)
    {
        string nivel = Paletas.NivelWcag(razao);
        texto.Text = Textos.Formatar(Textos.Estudio_Contraste, razao.ToString("0.0", Cultura), nivel);
        bool ok = razao >= 4.5;
        // O selo fica sobre a superfície do APP (segue o tema dos apps, não a amostra da barra)
        Color cor = ServicoTema.ClaroAtivo
            ? (ok ? Color.FromRgb(0x0F, 0x7B, 0x0F) : Color.FromRgb(0x9D, 0x5D, 0x00))
            : (ok ? Color.FromRgb(0x6D, 0xCC, 0x60) : Color.FromRgb(0xFC, 0xE1, 0x00));
        texto.Foreground = new SolidColorBrush(cor);
        selo.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, cor.R, cor.G, cor.B));
    }

    private void AtualizarTextoSuavizacao() =>
        TextoSuavizacao.Text = ControleSuavizacao.Value.ToString("0.0", Cultura);

    private void AtualizarInterfacesAtivas()
    {
        LeituraAcumuladaRede l = new NetworkSampler(_rascunho.InterfacesIncluir, _rascunho.InterfacesExcluir).LerContadoresAtuais();
        TextoInterfacesAtivas.Text = Textos.Formatar(Textos.Estudio_MedindoAgora, l.ResumoInterfacesAtivas);
    }

    /// <summary>"Alterações não aplicadas" quando o rascunho difere do que está valendo.</summary>
    private void AtualizarEstadoSalvo()
    {
        bool diferente = System.Text.Json.JsonSerializer.Serialize(_rascunho)
                         != System.Text.Json.JsonSerializer.Serialize(_servico.Atual);
        TextoEstadoSalvo.Text = diferente ? Textos.Estudio_AlteracoesNaoAplicadas : string.Empty;
        BotaoAplicar.IsEnabled = diferente;
    }

    // ------------------------------------------------------------------ eventos

    private void AoEscolherPaleta(object sender, RoutedEventArgs e)
    {
        if (_carregando || sender is not RadioButton { Tag: string id })
        {
            return;
        }

        _rascunho.Paleta = id;
        AtualizarPreVisualizacao();
        AtualizarEstadoSalvo();
    }

    private void AoMudarCorPersonalizada(object sender, TextChangedEventArgs e)
    {
        if (_carregando)
        {
            return;
        }

        bool upOk = Paletas.TentarHex(CampoCorUpload.Text, out Color up);
        bool downOk = Paletas.TentarHex(CampoCorDownload.Text, out Color down);
        if (upOk && downOk)
        {
            _rascunho.CorUploadPersonalizada = Paletas.ParaHex(up);
            _rascunho.CorDownloadPersonalizada = Paletas.ParaHex(down);
            AvisoCor.Text = Textos.Estudio_FormatoRRGGBBEx4CC2FFPense;
            AvisoCor.SetResourceReference(TextBlock.ForegroundProperty, "Tema.TextoSecundario");
            AtualizarPreVisualizacao();
            SincronizarSeletor(); // digitou um hex válido: o seletor e as amostras acompanham
            AtualizarEstadoSalvo();
        }
        else
        {
            AvisoCor.Text = Textos.Estudio_CorInvalida;
            AvisoCor.Foreground = new SolidColorBrush(ServicoTema.ClaroAtivo ? Color.FromRgb(0xC4, 0x2B, 0x1C) : Color.FromRgb(0xFF, 0x99, 0xA4));
        }
    }

    // ------------------------------------------------------------------ v0.9.0: seletor visual de cor

    /// <summary>Qual campo hex o seletor está editando agora (segmento "↑ Envio" / "↓ Download").</summary>
    private TextBox CampoDoAlvo => AlvoDownload.IsChecked == true ? CampoCorDownload : CampoCorUpload;

    /// <summary>
    /// Leva as cores do rascunho para o seletor e para as amostras do segmento. O campo hex é a fonte
    /// da verdade: atribuir <see cref="SeletorCor.Cor"/> não dispara <see cref="SeletorCor.CorAlterada"/>,
    /// então não há laço campo → seletor → campo.
    /// </summary>
    private void SincronizarSeletor()
    {
        if (Seletor is null || _rascunho is null)
        {
            return; // Checked do segmento disparado durante o InitializeComponent
        }

        bool upOk = Paletas.TentarHex(_rascunho.CorUploadPersonalizada, out Color up);
        bool downOk = Paletas.TentarHex(_rascunho.CorDownloadPersonalizada, out Color down);
        AmostraAlvoUpload.Background = upOk ? new SolidColorBrush(up) : Brushes.Transparent;
        AmostraAlvoDownload.Background = downOk ? new SolidColorBrush(down) : Brushes.Transparent;

        bool editandoDownload = AlvoDownload.IsChecked == true;
        if (editandoDownload ? downOk : upOk)
        {
            Seletor.Cor = editandoDownload ? down : up;
        }
    }

    private void AoEscolherAlvoCor(object sender, RoutedEventArgs e) => SincronizarSeletor();

    /// <summary>
    /// O usuário mexeu no seletor: escreve o hex no campo do alvo. O TextChanged do campo faz o resto
    /// (valida, atualiza rascunho, pré-visualização e "Alterações não aplicadas") — um caminho só.
    /// </summary>
    private void AoMoverSeletor(Color cor) => CampoDoAlvo.Text = Paletas.ParaHex(cor);

    private void AoMudarSuavizacao(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TextoSuavizacao is null)
        {
            return; // disparado durante o InitializeComponent
        }

        AtualizarTextoSuavizacao();
        if (_carregando)
        {
            return;
        }

        _rascunho.Suavizacao = Math.Round(ControleSuavizacao.Value, 1);
        AtualizarEstadoSalvo();
    }

    private void AoMudarPlano(object sender, TextChangedEventArgs e)
    {
        if (_carregando)
        {
            return;
        }

        string t = CampoPlano.Text.Trim();
        if (t.Length == 0)
        {
            _rascunho.VelocidadeContratadaMbps = 0;
        }
        else if (double.TryParse(t, NumberStyles.Number, Cultura, out double v) && v >= 0)
        {
            _rascunho.VelocidadeContratadaMbps = v;
        }

        AtualizarEstadoSalvo();
    }

    private void AoMudarExclusoes(object sender, TextChangedEventArgs e)
    {
        if (_carregando)
        {
            return;
        }

        _rascunho.InterfacesExcluir = CampoExcluir.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        AtualizarInterfacesAtivas();
        AtualizarEstadoSalvo();
    }

    private void AoAlternarBandeja(object sender, RoutedEventArgs e)
    {
        _rascunho.MostrarIconeBandeja = ChaveBandeja.IsChecked == true;
        AtualizarEstadoSalvo();
    }

    /// <summary>
    /// v0.8.0: um só tratador para os grupos de segmentos (tamanho, fonte, ordem, intervalo).
    /// O nome do grupo diz qual campo do rascunho muda; o Tag traz o valor.
    /// </summary>
    private void AoEscolherOpcao(object sender, RoutedEventArgs e)
    {
        if (_carregando || _rascunho is null || sender is not RadioButton { Tag: string valor } rb)
        {
            return; // também dispara durante o InitializeComponent, antes do rascunho existir
        }

        switch (rb.GroupName)
        {
            case "Tamanho":
                _rascunho.TamanhoTexto = valor;
                break;
            case "Fonte":
                _rascunho.Fonte = valor;
                break;
            case "Ordem":
                _rascunho.DownloadEmCima = valor == "download";
                break;
            case "Modo": // v1.2.0
                _rascunho.ModoExibicao = valor;
                break;
            case "Intervalo":
                _rascunho.IntervaloSegundos = double.Parse(valor, CultureInfo.InvariantCulture);
                break;
            case "Idioma": // v1.9.0: "auto" / "pt-BR" / "en" — vale ao Aplicar, para as janelas abertas depois
                _rascunho.Idioma = valor;
                break;
        }

        AtualizarPreVisualizacao();
        AtualizarEstadoSalvo();
    }

    private void AoAlternarMonitores(object sender, RoutedEventArgs e)
    {
        _rascunho.MostrarEmTodasAsBarras = ChaveMonitores.IsChecked == true;
        AtualizarEstadoSalvo();
    }

    private void AoRestaurarPadrao(object sender, RoutedEventArgs e)
    {
        // Mantém o que não é "aparência/medição": fixar na barra e unidade continuam como estão
        var padrao = new Configuracoes
        {
            FixarNaBarra = _rascunho.FixarNaBarra,
            Unidade = _rascunho.Unidade,
            MostrarCpuRam = _rascunho.MostrarCpuRam, // v0.5.0: escolha do painel/menu, não é "aparência"
            // Sem isto, o próximo início religaria o "Iniciar com o Windows" que o usuário desligou
            InicioAutomaticoJaConfigurado = _rascunho.InicioAutomaticoJaConfigurado,
            // v0.8.0: o lugar escolhido arrastando tem botão próprio no menu ("Voltar à posição automática")
            PosicoesManuais = new Dictionary<string, int>(_rascunho.PosicoesManuais),
            // v1.9.0: idioma não é "aparência" — restaurar o padrão não troca a língua de quem lê
            Idioma = _rascunho.Idioma
        };
        _rascunho = padrao;
        CarregarNosControles();
    }

    private void AoAplicar(object sender, RoutedEventArgs e)
    {
        // O app pode ter mudado unidade/fixar pelo menu com o estúdio aberto: preserva isso
        Configuracoes atual = _servico.Atual;
        _rascunho.Unidade = atual.Unidade;
        _rascunho.FixarNaBarra = atual.FixarNaBarra;
        _rascunho.MostrarCpuRam = atual.MostrarCpuRam; // v0.5.0: pode ter mudado pelo painel/menu
        _rascunho.InicioAutomaticoJaConfigurado = atual.InicioAutomaticoJaConfigurado;
        // v0.8.0: o widget pode ter sido arrastado (Ctrl+arrastar) com o estúdio aberto
        _rascunho.PosicoesManuais = new Dictionary<string, int>(atual.PosicoesManuais);
        _servico.Salvar(_rascunho.Clonar());
        AtualizarEstadoSalvo();
    }

    private void AoFechar(object sender, RoutedEventArgs e) => Close();

    /// <summary>Barra de título escura quando os apps estão no tema escuro (DWM, Win 11).</summary>
    private void AplicarBarraDeTituloDoTema()
    {
        int escuro = ServicoTema.ClaroAtivo ? 0 : 1;
        NativeMethods.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle,
            NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref escuro, sizeof(int));
    }
}

/// <summary>Atalho para ligar uma propriedade a um recurso dinâmico ao montar elementos em código.</summary>
internal static class ExtensoesElemento
{
    public static T ComRecurso<T>(this T elemento, DependencyProperty propriedade, string chave)
        where T : FrameworkElement
    {
        elemento.SetResourceReference(propriedade, chave);
        return elemento;
    }
}
