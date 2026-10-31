using System.Diagnostics;
using System.IO;
using System.Windows;
using VelocimetroInternet.Apresentacao;
using VelocimetroInternet.Medicao;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet;

/// <summary>
/// Ponto de entrada do aplicativo silencioso (sem janela principal e fora do Alt+Tab).
/// Na Onda 0, executa simultaneamente:
/// - PoC 1 / 1b: Encaixe na vaga do Clima da barra do Windows 11 (Modo A ou Modo B) + UI Automation;
/// - PoC 2: Leitura real de contadores de 64 bits via GetIfTable2 a cada 1 s com log em %LocalAppData%.
/// </summary>
public partial class App : Application
{
    private Mutex? _mutexInstanciaUnica;
    private TaskbarWidget? _widget;
    private TaskbarHost? _host;
    private CancellationTokenSource? _ctsMedicao;
    private string _caminhoLog = string.Empty;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string pastaDados = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VelocimetroInternet");
        Directory.CreateDirectory(pastaDados);
        _caminhoLog = Path.Combine(pastaDados, "poc-onda0.log");

        // Garante instância única (01-ANALISE.md §6)
        _mutexInstanciaUnica = new Mutex(initiallyOwned: true, @"Local\VelocimetroInternet_Mutex_v1", out bool criouNovo);
        if (!criouNovo)
        {
            RegistrarLog("Outra instância já está em execução. Encerrando silenciosamente.");
            Shutdown(0);
            return;
        }

        // Interpreta argumentos de linha de comando para permitir testar Modo A vs Modo B e texto fixo da PoC 1
        // v0.1.1: Modo B é o padrão (Modo A invisível na barra XAML do Win 11 — ver TaskbarHost).
        // --modo-a fica só para experimentos; --modo-b é aceito por compatibilidade (já é o padrão).
        bool usarModoA = e.Args.Any(a => a.Equals("--modo-a", StringComparison.OrdinalIgnoreCase));
        bool textoFixoPoC = e.Args.Any(a => a.Equals("--poc-fixo", StringComparison.OrdinalIgnoreCase));
        bool usarBits = e.Args.Any(a => a.Equals("--bits", StringComparison.OrdinalIgnoreCase));
        bool usarTemaClaro = e.Args.Any(a => a.Equals("--tema-claro", StringComparison.OrdinalIgnoreCase));
        bool usarCompacto = e.Args.Any(a => a.Equals("--compacto", StringComparison.OrdinalIgnoreCase));

        ModoEncaixeBarra modoPreferido = usarModoA
            ? ModoEncaixeBarra.ModoA_FilhaDaBarra
            : ModoEncaixeBarra.ModoB_TopmostSobreBarra;

        ModoUnidadeVelocidade unidade = usarBits
            ? ModoUnidadeVelocidade.BitsPorSegundo
            : ModoUnidadeVelocidade.BytesPorSegundo;

        RegistrarLog($"=== Iniciando Velocímetro de Internet v0.1.1 (Onda 0) | Modo={modoPreferido} | Unidade={unidade} | FixoPoC={textoFixoPoC} ===");

        _widget = new TaskbarWidget(unidade);
        if (usarTemaClaro)
        {
            _widget.AplicarTemaBarra(temaClaro: true);
            _widget.ContornoHover.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(243, 243, 243));
        }

        _widget.DefinirTextosFixosPoC(
            usarBits ? "1,24 Mbps" : "1,24 MB/s",
            usarBits ? "38,7 Mbps" : "38,7 MB/s");
        // v0.1.1: NÃO chamar _widget.Show() aqui. O TaskbarHost calcula a posição primeiro e só
        // então torna a janela visível — evita o "piscar no topo da tela" ao abrir.

        _host = new TaskbarHost(_widget, modoPreferido, RegistrarLog)
        {
            ForcarModoCompactoTeste = usarCompacto
        };
        await _host.InicializarAsync();

        // Reduz o working set inicial após o JIT carregar o XAML (02-ARQUITETURA.md §8)
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            try
            {
                using var proc = Process.GetCurrentProcess();
                NativeMethods.EmptyWorkingSet(proc.Handle);
            }
            catch
            {
                // Ignora se não permitido
            }
        });

        // Inicia o loop de amostragem de 1 segundo (GetIfTable2) em background com PeriodicTimer
        _ctsMedicao = new CancellationTokenSource();
        _ = ExecutarLoopMedicaoAsync(textoFixoPoC, unidade, _ctsMedicao.Token);
    }

    private async Task ExecutarLoopMedicaoAsync(
        bool manterTextoFixoNaTela,
        ModoUnidadeVelocidade unidade,
        CancellationToken ct)
    {
        var sampler = new NetworkSampler();
        var calculator = new SpeedCalculator(fatorSuavizacao: 0.5);
        var formatterUp = new SpeedFormatter(unidade);
        var formatterDown = new SpeedFormatter(unidade);
        var cronometro = Stopwatch.StartNew();
        TimeSpan ultimoInstante = cronometro.Elapsed;

        // Leitura inicial (linha de base)
        LeituraAcumuladaRede leituraBase = sampler.LerContadoresAtuais();
        calculator.CalcularProximaAmostra(leituraBase.TotalBytesRecebidos, leituraBase.TotalBytesEnviados, TimeSpan.Zero);
        RegistrarLog($"Interface(s) física(s) ativa(s) [{leituraBase.InterfacesFisicasAtivas}]: {leituraBase.ResumoInterfacesAtivas} | InOctets={leituraBase.TotalBytesRecebidos} OutOctets={leituraBase.TotalBytesEnviados}");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        int tick = 0;

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                tick++;
                TimeSpan agora = cronometro.Elapsed;
                TimeSpan decurso = agora - ultimoInstante;
                ultimoInstante = agora;

                LeituraAcumuladaRede leitura = sampler.LerContadoresAtuais();
                VelocidadeRede vel = calculator.CalcularProximaAmostra(
                    leitura.TotalBytesRecebidos,
                    leitura.TotalBytesEnviados,
                    decurso);

                if (!manterTextoFixoNaTela && vel.AmostraValida && _widget != null)
                {
                    _ = _widget.Dispatcher.InvokeAsync(() =>
                        _widget.AtualizarVelocidades(vel.UploadBytesPorSegundo, vel.DownloadBytesPorSegundo));
                }

                // Registra no log a cada segundo nos primeiros 15 s e depois a cada 10 s
                if (tick <= 15 || tick % 10 == 0)
                {
                    using var proc = Process.GetCurrentProcess();
                    double ramWorkingSetMb = proc.WorkingSet64 / (1024.0 * 1024.0);
                    double ramPrivadaMb = proc.PrivateMemorySize64 / (1024.0 * 1024.0);
                    string upStr = formatterUp.FormatarComSeta("↑", vel.UploadBytesPorSegundo);
                    string downStr = formatterDown.FormatarComSeta("↓", vel.DownloadBytesPorSegundo);

                    RegistrarLog(
                        $"[Tick #{tick:00}] {upStr} | {downStr} | " +
                        $"In={leitura.TotalBytesRecebidos} Out={leitura.TotalBytesEnviados} | " +
                        $"RAM WorkingSet={ramWorkingSetMb:F1} MB (Privada={ramPrivadaMb:F1} MB)");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal
        }
        catch (Exception ex)
        {
            RegistrarLog($"Erro no loop de medição: {ex}");
        }
    }

    private void RegistrarLog(string mensagem)
    {
        try
        {
            string linha = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {mensagem}{Environment.NewLine}";
            File.AppendAllText(_caminhoLog, linha);
        }
        catch
        {
            // Nunca exibir popup de erro — aplicativo 100% silencioso (01-ANALISE.md §6)
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _ctsMedicao?.Cancel();
        _host?.Dispose();
        _mutexInstanciaUnica?.Dispose();
        base.OnExit(e);
    }
}
