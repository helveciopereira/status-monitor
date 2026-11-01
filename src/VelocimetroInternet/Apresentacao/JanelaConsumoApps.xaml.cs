using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using VelocimetroInternet.Medicao;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// v1.3.0 (Onda 8): janela "Consumo por aplicativo". Uma instância por vez (o App reaproveita).
/// Relê a cada 30 s ENQUANTO aberta — fechada, não consulta nada. A leitura vem por delegado
/// (o App passa o <see cref="LeitorConsumoPorApp"/>; o render de teste passa dados fixos).
/// </summary>
public partial class JanelaConsumoApps : Window
{
    /// <summary>De quanto em quanto tempo a lista se refaz (o Windows atualiza ~1 vez por minuto).</summary>
    private static readonly TimeSpan IntervaloAtualizacao = TimeSpan.FromSeconds(30);

    private readonly Func<PeriodoConsumoApp, CancellationToken, Task<List<UsoApp>>> _ler;
    private readonly IconesApps _icones = new();
    private readonly DispatcherTimer _relogio;
    private PeriodoConsumoApp _periodo = PeriodoConsumoApp.Hoje;
    private CancellationTokenSource? _leitura;

    public JanelaConsumoApps(Func<PeriodoConsumoApp, CancellationToken, Task<List<UsoApp>>> ler)
    {
        InitializeComponent();
        _ler = ler;
        _relogio = new DispatcherTimer { Interval = IntervaloAtualizacao };
        _relogio.Tick += async (_, _) => await RecarregarAsync(mostrarLendo: false);

        // "Hoje" é o período mais útil para abrir (marcar dispara AoEscolherPeriodo → 1ª leitura)
        Loaded += (_, _) =>
        {
            OpcaoHoje.IsChecked = true;
            _relogio.Start();
        };
        Closed += (_, _) =>
        {
            _relogio.Stop();
            _leitura?.Cancel();
        };
    }

    private async void AoEscolherPeriodo(object sender, RoutedEventArgs e)
    {
        _periodo = sender == Opcao5Min ? PeriodoConsumoApp.CincoMinutos
            : sender == Opcao1h ? PeriodoConsumoApp.UmaHora
            : sender == OpcaoMes ? PeriodoConsumoApp.EsteMes
            : PeriodoConsumoApp.Hoje;
        await RecarregarAsync(mostrarLendo: true);
    }

    /// <summary>
    /// Lê e mostra. Uma leitura nova CANCELA a anterior (trocar de período rápido não deixa uma
    /// resposta velha sobrescrever a nova).
    /// </summary>
    public async Task RecarregarAsync(bool mostrarLendo)
    {
        _leitura?.Cancel();
        var cts = new CancellationTokenSource();
        _leitura = cts;
        if (mostrarLendo)
        {
            Lista.ItemsSource = null;
            MostrarEstado("Lendo…");
        }

        try
        {
            List<UsoApp> apps = await _ler(_periodo, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            Mostrar(apps);
        }
        catch (OperationCanceledException)
        {
            // trocou de período ou fechou: a leitura nova assume
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Captura ampla justificada: a fonte é uma API do Windows que pode falhar de vários
            // jeitos (serviço parado, política) — a janela explica em vez de derrubar o widget.
            Lista.ItemsSource = null;
            MostrarEstado("Não foi possível ler o consumo do Windows agora. Tente de novo em instantes.");
            System.Diagnostics.Debug.WriteLine($"Consumo por app: {ex}");
        }
    }

    /// <summary>Mostra a lista já ordenada (as linhas e o total do período).</summary>
    public void Mostrar(IReadOnlyList<UsoApp> apps)
    {
        ulong rec = 0, env = 0;
        foreach (UsoApp a in apps)
        {
            rec += a.Recebidos;
            env += a.Enviados;
        }

        TextoTotal.Text = apps.Count == 0 ? "" : $"↓ {SpeedFormatter.FormatarQuantidade(rec)} · ↑ {SpeedFormatter.FormatarQuantidade(env)}";
        TextoAtualizado.Text = $"Atualizado às {DateTime.Now:HH:mm:ss} · refaz a cada 30 s";

        List<UsoApp> linhas = ConsumoPorApp.Resumir(apps);
        ulong maior = linhas.Count == 0 ? 0 : linhas.Max(l => l.Total);
        Lista.ItemsSource = linhas.Select(l => new LinhaApp(l, maior, _icones.Obter(l.Caminho))).ToList();

        if (linhas.Count == 0)
        {
            MostrarEstado(_periodo == PeriodoConsumoApp.CincoMinutos
                ? "Nenhum programa usou a internet nos últimos 5 minutos (ou o Windows ainda não contou — ele atualiza a cada minuto)."
                : "Nenhum programa usou a internet neste período.");
        }
        else
        {
            TextoEstado.Visibility = Visibility.Collapsed;
        }
    }

    private void MostrarEstado(string texto)
    {
        TextoEstado.Text = texto;
        TextoEstado.Visibility = Visibility.Visible;
    }

    private void AoClicarFechar(object sender, RoutedEventArgs e) => Close();

    /// <summary>Uma linha já pronta para a tela (só leitura; a lista é refeita a cada leitura).</summary>
    public sealed class LinhaApp
    {
        public LinhaApp(UsoApp app, ulong maior, ImageSource? icone)
        {
            Nome = app.Nome;
            Icone = icone;
            Dica = app.Caminho ?? (string.IsNullOrEmpty(app.Id) ? app.Nome : app.Id);
            TextoBaixado = $"↓ {SpeedFormatter.FormatarQuantidade(app.Recebidos)}";
            TextoEnviado = $"↑ {SpeedFormatter.FormatarQuantidade(app.Enviados)}";

            // Barra proporcional ao MAIOR da lista; um mínimo visível para quem usou pouco
            double f = maior == 0 ? 0 : Math.Max(0.015, (double)app.Total / maior);
            ColunaUsada = new GridLength(f, GridUnitType.Star);
            ColunaResto = new GridLength(1 - f, GridUnitType.Star);
        }

        public string Nome { get; }
        public ImageSource? Icone { get; }
        public string Dica { get; }
        public string TextoBaixado { get; }
        public string TextoEnviado { get; }
        public GridLength ColunaUsada { get; }
        public GridLength ColunaResto { get; }

        /// <summary>Glifo "app" do Segoe Fluent quando não há ícone de arquivo.</summary>
        public string Glifo => "";

        public Visibility VisivelIcone => Icone is null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility VisivelGlifo => Icone is null ? Visibility.Visible : Visibility.Collapsed;
    }
}
