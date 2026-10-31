using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using VelocimetroInternet.Config;
using VelocimetroInternet.Medicao;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// Estúdio de Cores &amp; Configurações (Onda 3). Trabalha numa cópia (<see cref="_rascunho"/>):
/// a pré-visualização muda na hora, a barra só muda ao clicar "Aplicar".
/// </summary>
public partial class EstudioCores : Window
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private readonly ServicoConfiguracoes _servico;
    private Configuracoes _rascunho;
    private bool _carregando;
    private readonly Dictionary<string, RadioButton> _cartoes = [];

    /// <param name="textoVersao">Rodapé (v0.4.0), ex.: "Versão 0.4.3 · auto update".</param>
    public EstudioCores(ServicoConfiguracoes servico, string textoVersao = "")
    {
        InitializeComponent();
        TextoVersao.Text = textoVersao;
        _servico = servico;
        _rascunho = servico.Atual.Clonar();
        MontarGaleria();
        CarregarNosControles();
        SourceInitialized += (_, _) => AplicarBarraDeTituloDoTema();
    }

    // ------------------------------------------------------------------ montagem

    /// <summary>Um cartão por paleta: nome, descrição e as duas amostras de cor (como no design).</summary>
    private void MontarGaleria()
    {
        IEnumerable<PaletaCores> todas = Paletas.Predefinidas.Append(
            new PaletaCores(Paletas.IdPersonalizada, "Personalizada", "Escolha as duas cores em hexadecimal.",
                Paletas.Hex(_rascunho.CorUploadPersonalizada), Paletas.Hex(_rascunho.CorDownloadPersonalizada)));

        foreach (PaletaCores p in todas)
        {
            var conteudo = new StackPanel();
            conteudo.Children.Add(new TextBlock { Text = p.Nome, FontWeight = FontWeights.SemiBold });
            conteudo.Children.Add(new TextBlock
            {
                Text = p.Descricao,
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
            System.Windows.Automation.AutomationProperties.SetName(cartao, $"Paleta {p.Nome}");
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
                ? _rascunho.VelocidadeContratadaMbps.ToString("0.##", PtBr)
                : string.Empty;
            CampoExcluir.Text = string.Join(Environment.NewLine, _rascunho.InterfacesExcluir);
            ChaveBandeja.IsChecked = _rascunho.MostrarIconeBandeja || !_rascunho.FixarNaBarra;
            ChaveBandeja.IsEnabled = _rascunho.FixarNaBarra;
        }
        finally
        {
            _carregando = false;
        }

        AtualizarPreVisualizacao();
        AtualizarTextoSuavizacao();
        AtualizarInterfacesAtivas();
        AtualizarEstadoSalvo();
    }

    // ------------------------------------------------------------------ pré-visualização

    private void AtualizarPreVisualizacao()
    {
        bool personalizada = _rascunho.Paleta == Paletas.IdPersonalizada;
        PainelPersonalizada.Visibility = personalizada ? Visibility.Visible : Visibility.Collapsed;

        PaletaCores paleta = Paletas.Obter(_rascunho.Paleta, _rascunho.CorUploadPersonalizada, _rascunho.CorDownloadPersonalizada);
        CoresResolvidas escura = Paletas.Resolver(paleta, barraClara: false);
        CoresResolvidas clara = Paletas.Resolver(paleta, barraClara: true);

        AmostraEscura.Content = MiniWidget(escura);
        AmostraClara.Content = MiniWidget(clara);
        PreencherSelo(SeloEscuro, TextoSeloEscuro, Math.Min(escura.ContrasteUpload, escura.ContrasteDownload));
        PreencherSelo(SeloClaro, TextoSeloClaro, Math.Min(clara.ContrasteUpload, clara.ContrasteDownload));
    }

    /// <summary>Réplica do widget (2 pílulas) com as cores resolvidas — mesmo desenho da barra.</summary>
    private static UIElement MiniWidget(CoresResolvidas c)
    {
        var pilha = new StackPanel { Width = 104 };
        pilha.Children.Add(Pilula("↑", "1,24 MB/s", c.Upload, c.FundoPilulaUpload, c.BordaPilulaUpload));
        pilha.Children.Add(new Border { Height = 2 });
        pilha.Children.Add(Pilula("↓", "38,7 MB/s", c.Download, c.FundoPilulaDownload, c.BordaPilulaDownload));
        return pilha;
    }

    private static Border Pilula(string seta, string valor, Color texto, Color fundo, Color borda)
    {
        var grade = new Grid();
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grade.ColumnDefinitions.Add(new ColumnDefinition());
        var pincel = new SolidColorBrush(texto);
        grade.Children.Add(new TextBlock { Text = seta, Foreground = pincel, FontWeight = FontWeights.Bold, FontSize = 10.5 });
        var t = new TextBlock
        {
            Text = valor,
            Foreground = pincel,
            FontWeight = FontWeights.SemiBold,
            FontSize = 10.5,
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
            Padding = new Thickness(6, 1, 6, 1),
            Child = grade
        };
    }

    /// <summary>Selo "Contraste 9,2:1 (AAA)" — verde se passa AA, âmbar se não.</summary>
    private static void PreencherSelo(Border selo, TextBlock texto, double razao)
    {
        string nivel = Paletas.NivelWcag(razao);
        texto.Text = $"Contraste {razao.ToString("0.0", PtBr)}:1 ({nivel})";
        bool ok = razao >= 4.5;
        // O selo fica sobre a superfície do APP (segue o tema dos apps, não a amostra da barra)
        Color cor = ServicoTema.ClaroAtivo
            ? (ok ? Color.FromRgb(0x0F, 0x7B, 0x0F) : Color.FromRgb(0x9D, 0x5D, 0x00))
            : (ok ? Color.FromRgb(0x6D, 0xCC, 0x60) : Color.FromRgb(0xFC, 0xE1, 0x00));
        texto.Foreground = new SolidColorBrush(cor);
        selo.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, cor.R, cor.G, cor.B));
    }

    private void AtualizarTextoSuavizacao() =>
        TextoSuavizacao.Text = ControleSuavizacao.Value.ToString("0.0", PtBr);

    private void AtualizarInterfacesAtivas()
    {
        LeituraAcumuladaRede l = new NetworkSampler(_rascunho.InterfacesIncluir, _rascunho.InterfacesExcluir).LerContadoresAtuais();
        TextoInterfacesAtivas.Text = $"Medindo agora: {l.ResumoInterfacesAtivas}";
    }

    /// <summary>"Alterações não aplicadas" quando o rascunho difere do que está valendo.</summary>
    private void AtualizarEstadoSalvo()
    {
        bool diferente = System.Text.Json.JsonSerializer.Serialize(_rascunho)
                         != System.Text.Json.JsonSerializer.Serialize(_servico.Atual);
        TextoEstadoSalvo.Text = diferente ? "Alterações não aplicadas" : string.Empty;
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
            AvisoCor.Text = "Formato #RRGGBB, ex.: #4CC2FF. Pense na barra escura; a versão clara é ajustada sozinha.";
            AvisoCor.SetResourceReference(TextBlock.ForegroundProperty, "Tema.TextoSecundario");
            AtualizarPreVisualizacao();
            AtualizarEstadoSalvo();
        }
        else
        {
            AvisoCor.Text = "Cor inválida — use 6 dígitos hexadecimais, ex.: #4CC2FF.";
            AvisoCor.Foreground = new SolidColorBrush(ServicoTema.ClaroAtivo ? Color.FromRgb(0xC4, 0x2B, 0x1C) : Color.FromRgb(0xFF, 0x99, 0xA4));
        }
    }

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
        else if (double.TryParse(t, NumberStyles.Number, PtBr, out double v) && v >= 0)
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

    private void AoRestaurarPadrao(object sender, RoutedEventArgs e)
    {
        // Mantém o que não é "aparência/medição": fixar na barra e unidade continuam como estão
        var padrao = new Configuracoes
        {
            FixarNaBarra = _rascunho.FixarNaBarra,
            Unidade = _rascunho.Unidade,
            // Sem isto, o próximo início religaria o "Iniciar com o Windows" que o usuário desligou
            InicioAutomaticoJaConfigurado = _rascunho.InicioAutomaticoJaConfigurado
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
        _rascunho.InicioAutomaticoJaConfigurado = atual.InicioAutomaticoJaConfigurado;
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
