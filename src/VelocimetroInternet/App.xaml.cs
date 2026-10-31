using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using VelocimetroInternet.Apresentacao;
using VelocimetroInternet.Config;
using VelocimetroInternet.Medicao;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet;

/// <summary>
/// Ponto de entrada do aplicativo silencioso (sem janela principal e fora do Alt+Tab).
///
/// v0.2.0 (Onda 1) — o app deixa de ser PoC e vira produto:
/// - configurações persistidas (settings.json) aplicadas ao vivo;
/// - cores pelo motor de paletas (Modelo 3) seguindo o tema da BARRA do Windows;
/// - ícone na bandeja + menu (bandeja e clique direito no widget) com as opções do design;
/// - "Iniciar com o Windows" real (HKCU\Run + StartupApproved);
/// - 2ª execução do .exe não abre outra cópia: avisa a 1ª, que reexibe o widget/abre o painel.
/// </summary>
public partial class App : Application
{
    /// <summary>Mensagem que a 2ª instância transmite para a 1ª ("alguém me abriu de novo").</summary>
    private const string NomeMensagemAtivar = "VelocimetroInternet_Ativar_v1";

    private Mutex? _mutexInstanciaUnica;
    private TaskbarWidget? _widget;
    private TaskbarHost? _host;
    private IconeBandeja? _bandeja;
    private PainelRapido? _painel;
    private CancellationTokenSource? _ctsMedicao;
    private string _caminhoLog = string.Empty;
    private uint _msgAtivar;

    private readonly ServicoConfiguracoes _config = new();
    private readonly InicioComWindows _inicio = new();
    private readonly HistoricoRede _historico = new();

    /// <summary>Medidor recriado quando muda filtro de interfaces ou suavização (lido pelo loop).</summary>
    private volatile MedidorAtual? _medidor;

    private sealed record MedidorAtual(NetworkSampler Sampler, SpeedCalculator Calculadora);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string pastaDados = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VelocimetroInternet");
        Directory.CreateDirectory(pastaDados);
        _caminhoLog = Path.Combine(pastaDados, "velocimetro.log");
        LimitarTamanhoLog();

        _msgAtivar = NativeMethods.RegisterWindowMessageW(NomeMensagemAtivar);

        // Instância única (01-ANALISE.md §6). A 2ª cópia avisa a 1ª e sai — o usuário que
        // clicou de novo no atalho vê o widget voltar / o painel abrir, em vez de "nada".
        _mutexInstanciaUnica = new Mutex(initiallyOwned: true, @"Local\VelocimetroInternet_Mutex_v1", out bool criouNovo);
        if (!criouNovo)
        {
            RegistrarLog("Outra instância já está em execução — avisando-a e encerrando.");
            AvisarPrimeiraInstancia();
            Shutdown(0);
            return;
        }

        // Argumentos de diagnóstico (mantidos da Onda 0): --modo-a, --poc-fixo, --compacto
        bool usarModoA = e.Args.Any(a => a.Equals("--modo-a", StringComparison.OrdinalIgnoreCase));
        bool textoFixoPoC = e.Args.Any(a => a.Equals("--poc-fixo", StringComparison.OrdinalIgnoreCase));
        bool usarCompacto = e.Args.Any(a => a.Equals("--compacto", StringComparison.OrdinalIgnoreCase));
        bool abrirPainel = e.Args.Any(a => a.Equals("--painel", StringComparison.OrdinalIgnoreCase));

        string? aviso = _config.Carregar();
        if (aviso is not null)
        {
            RegistrarLog(aviso);
        }

        Configuracoes cfg = _config.Atual;
        RegistrarLog($"=== Velocímetro de Internet v{VersaoApp} | Unidade={cfg.Unidade} | Paleta={cfg.Paleta} | Fixar={cfg.FixarNaBarra} | Bandeja={cfg.MostrarIconeBandeja} ===");

        ServicoTema.Aplicar(TemaWindows.AppsClaros());

        _widget = new TaskbarWidget(UnidadeDe(cfg));
        AplicarPaleta();
        if (textoFixoPoC)
        {
            _widget.DefinirTextosFixosPoC();
        }

        // NÃO chamar _widget.Show() aqui: o TaskbarHost posiciona primeiro e só então mostra
        // (evita o "piscar no topo da tela" corrigido na v0.1.1).
        _host = new TaskbarHost(
            _widget,
            usarModoA ? ModoEncaixeBarra.ModoA_FilhaDaBarra : ModoEncaixeBarra.ModoB_TopmostSobreBarra,
            RegistrarLog)
        {
            ForcarModoCompactoTeste = usarCompacto
        };
        await _host.InicializarAsync();
        _host.FixadoNaBarra = cfg.FixarNaBarra;

        // Mensagens do Windows que chegam pela janela do widget: tema, bandeja e "2ª instância"
        _host.AdicionarGanchoMensagens(GanchoMensagens);
        _host.TaskbarRecriada += () => _bandeja?.Readicionar();
        _host.TelaCheiaMudou += cheia =>
        {
            if (cheia)
            {
                _painel?.Fechar();
            }
        };

        _bandeja = new IconeBandeja(_host.HwndWidget);
        _bandeja.CliqueEsquerdo += () => AlternarPainel(veioDaBandeja: true);
        _bandeja.CliqueDireito += AbrirMenu;
        AtualizarVisibilidadeBandeja();

        _widget.CliqueEsquerdo += () => AlternarPainel(veioDaBandeja: false);
        _widget.CliqueDireito += AbrirMenu;

        _config.Alteradas += AoAlterarConfiguracoes;

        // 1º uso: liga o início com o Windows (o pedido é "iniciar com o sistema"). Só uma vez —
        // se o usuário desligar depois, o marcador impede religar.
        if (!cfg.InicioAutomaticoJaConfigurado && Environment.ProcessPath is { } exeNovo)
        {
            DefinirInicioComWindows(true);
            _config.Alterar(c => c.InicioAutomaticoJaConfigurado = true);
            RegistrarLog($"1º uso: início com o Windows ligado ({exeNovo}).");
        }

        // Se o .exe foi movido de pasta, a entrada de início aponta para o lugar velho: corrige
        if (Environment.ProcessPath is { } exe && _inicio.EstaLigado() && _inicio.ApontaParaOutroCaminho(exe))
        {
            _inicio.Definir(true, exe);
            RegistrarLog("Entrada de início com o Windows corrigida para o caminho atual do .exe.");
        }

        // Reduz o working set inicial após o JIT carregar o XAML (02-ARQUITETURA.md §8)
        AparaMemoriaEmBreve();

        RecriarMedidor(cfg);
        _ctsMedicao = new CancellationTokenSource();
        _ = ExecutarLoopMedicaoAsync(textoFixoPoC, _ctsMedicao.Token);

        if (abrirPainel)
        {
            AlternarPainel(veioDaBandeja: false);
        }
    }

    /// <summary>
    /// Entrega o "abra o painel" direto na janela do widget da 1ª instância.
    /// NÃO usar HWND_BROADCAST: janela WPF com ShowInTaskbar=False ganha um DONO oculto, e o
    /// broadcast só alcança janelas de topo SEM dono — a mensagem nunca chegava (visto na v0.2.0).
    /// AllowSetForegroundWindow: esta cópia acabou de ser aberta pelo usuário (tem o direito de
    /// foco) e o repassa, senão o painel abriria atrás da janela ativa.
    /// </summary>
    private void AvisarPrimeiraInstancia()
    {
        nint alvo = NativeMethods.FindWindowW(null, "VelocimetroInternetWidget");
        if (alvo == nint.Zero)
        {
            RegistrarLog("Janela da 1ª instância não encontrada — nada a avisar.");
            return;
        }

        NativeMethods.GetWindowThreadProcessId(alvo, out uint pid);
        NativeMethods.AllowSetForegroundWindow(pid);
        NativeMethods.PostMessageW(alvo, _msgAtivar, nint.Zero, nint.Zero);
    }

    /// <summary>
    /// Devolve ao Windows a memória das janelas fechadas. Painel/estúdio carregam muito XAML
    /// (medido: ~185 MB com o estúdio aberto e NADA devolvido ao fechar); aqui coletamos e
    /// aparamos o working set 1,5 s depois — o widget sozinho volta à faixa de ~50 MB.
    /// </summary>
    private static void AparaMemoriaEmBreve()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                using var proc = Process.GetCurrentProcess();
                NativeMethods.EmptyWorkingSet(proc.Handle);
            }
            catch
            {
                // Ignora se não permitido
            }
        });
    }

    // ------------------------------------------------------------------ configurações

    private static string VersaoApp =>
        typeof(App).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";

    private static ModoUnidadeVelocidade UnidadeDe(Configuracoes cfg) =>
        cfg.Unidade == "bits" ? ModoUnidadeVelocidade.BitsPorSegundo : ModoUnidadeVelocidade.BytesPorSegundo;

    /// <summary>Aplica tudo o que mudou. Chamado após cada Salvar (menu, painel ou estúdio).</summary>
    private void AoAlterarConfiguracoes(Configuracoes cfg)
    {
        if (_widget is null || _host is null)
        {
            return;
        }

        _widget.DefinirUnidade(UnidadeDe(cfg));
        _host.FixadoNaBarra = cfg.FixarNaBarra;
        AtualizarVisibilidadeBandeja();
        AplicarPaleta();
        RecriarMedidor(cfg);
        _painel?.SincronizarComConfiguracoes(cfg, _inicio.EstaLigado());
    }

    /// <summary>
    /// Bandeja: obedece a preferência, MAS é forçada quando o widget está desafixado — senão o
    /// usuário ficaria sem nenhum jeito de reabrir o app (só pelo atalho).
    /// </summary>
    private void AtualizarVisibilidadeBandeja()
    {
        if (_bandeja is null)
        {
            return;
        }

        Configuracoes cfg = _config.Atual;
        _bandeja.Visivel = cfg.MostrarIconeBandeja || !cfg.FixarNaBarra;
    }

    /// <summary>Resolve a paleta escolhida para o tema ATUAL da barra e pinta o widget.</summary>
    private void AplicarPaleta()
    {
        if (_widget is null)
        {
            return;
        }

        Configuracoes cfg = _config.Atual;
        PaletaCores paleta = Paletas.Obter(cfg.Paleta, cfg.CorUploadPersonalizada, cfg.CorDownloadPersonalizada);
        CoresResolvidas cores = Paletas.Resolver(paleta, TemaWindows.BarraClara());
        _widget.AplicarCores(cores);
        _painel?.AplicarPaleta(paleta);
    }

    private void RecriarMedidor(Configuracoes cfg)
    {
        var sampler = new NetworkSampler(cfg.InterfacesIncluir, cfg.InterfacesExcluir);
        var calculadora = new SpeedCalculator(cfg.Suavizacao);
        // Linha de base imediata: sem ela o 1º tick depois da troca daria um pico falso
        LeituraAcumuladaRede leitura = sampler.LerContadoresAtuais();
        calculadora.CalcularProximaAmostra(leitura.TotalBytesRecebidos, leitura.TotalBytesEnviados, TimeSpan.Zero);
        _medidor = new MedidorAtual(sampler, calculadora);
    }

    // ------------------------------------------------------------------ mensagens do Windows

    private nint GanchoMensagens(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // 1) Bandeja (callback registrado do Shell_NotifyIcon)
        nint r = _bandeja?.ProcessarMensagem(hwnd, msg, wParam, lParam, ref handled) ?? nint.Zero;
        if (handled)
        {
            return r;
        }

        // 2) Outra cópia do .exe foi aberta → reexibe o widget e abre o painel
        if (_msgAtivar != 0 && (uint)msg == _msgAtivar)
        {
            if (!_config.Atual.FixarNaBarra)
            {
                _config.Alterar(c => c.FixarNaBarra = true);
            }

            AbrirPainel(veioDaBandeja: false);
            handled = true;
            return nint.Zero;
        }

        // 3) Tema claro/escuro alterado nas Configurações do Windows
        if (msg == NativeMethods.WM_SETTINGCHANGE && lParam != nint.Zero
            && Marshal.PtrToStringUni(lParam) == TemaWindows.SinalMudancaTema)
        {
            ServicoTema.Aplicar(TemaWindows.AppsClaros());
            AplicarPaleta();
            RegistrarLog($"Tema mudou: barra {(TemaWindows.BarraClara() ? "clara" : "escura")}, apps {(TemaWindows.AppsClaros() ? "claros" : "escuros")}.");
        }

        return nint.Zero;
    }

    // ------------------------------------------------------------------ painel rápido

    private void AlternarPainel(bool veioDaBandeja)
    {
        // Clique no widget/bandeja com o painel aberto: o painel já fechou ao perder o foco
        // seguido deste clique → não reabrir (o clique era para FECHAR)
        if (_painel is { IsVisible: true } || _painel?.FechadoHaPouco == true)
        {
            _painel?.Fechar();
            return;
        }

        AbrirPainel(veioDaBandeja);
    }

    private void AbrirPainel(bool veioDaBandeja)
    {
        if (_host is null)
        {
            return;
        }

        if (_painel is null)
        {
            _painel = new PainelRapido(_historico);
            _painel.UnidadeEscolhida += bits => _config.Alterar(c => c.Unidade = bits ? "bits" : "bytes");
            _painel.FixarAlterado += fixar => _config.Alterar(c => c.FixarNaBarra = fixar);
            _painel.IniciarComWindowsAlterado += DefinirInicioComWindows;
            _painel.AbrirEstudioPedido += AbrirEstudio;
            _painel.IsVisibleChanged += (_, ev) =>
            {
                if (ev.NewValue is false)
                {
                    AparaMemoriaEmBreve();
                }
            };
        }

        Configuracoes cfg = _config.Atual;
        _painel.SincronizarComConfiguracoes(cfg, _inicio.EstaLigado());
        _painel.AplicarPaleta(Paletas.Obter(cfg.Paleta, cfg.CorUploadPersonalizada, cfg.CorDownloadPersonalizada));
        _painel.AbrirAncorado(_host.UltimaPosicao, _host.HwndWidget, veioDaBandeja);
    }

    // ------------------------------------------------------------------ estúdio de cores

    private EstudioCores? _estudio;

    private void AbrirEstudio()
    {
        _painel?.Fechar();
        if (_estudio is { IsLoaded: true })
        {
            _estudio.Activate();
            return;
        }

        _estudio = new EstudioCores(_config);
        _estudio.Closed += (_, _) =>
        {
            _estudio = null;
            AparaMemoriaEmBreve();
        };
        _estudio.Show();
        _estudio.Activate();
    }

    // ------------------------------------------------------------------ menu

    /// <summary>
    /// Menu do clique direito (widget ou bandeja). Montado na hora para refletir o estado atual;
    /// estilizado pelos tokens Tema.* do App.xaml (segue o tema claro/escuro dos apps).
    /// </summary>
    private void AbrirMenu()
    {
        _painel?.Fechar();
        Configuracoes cfg = _config.Atual;
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };

        var itemPainel = new MenuItem { Header = "Abrir painel rápido" };
        itemPainel.Click += (_, _) => AbrirPainel(veioDaBandeja: false);
        menu.Items.Add(itemPainel);

        var itemEstudio = new MenuItem { Header = "Estúdio de cores e configurações…" };
        itemEstudio.Click += (_, _) => AbrirEstudio();
        menu.Items.Add(itemEstudio);

        menu.Items.Add(new Separator());

        var itemUnidade = new MenuItem { Header = "Unidade" };
        var itemBytes = new MenuItem { Header = "MB/s (bytes)", IsChecked = cfg.Unidade != "bits" };
        itemBytes.Click += (_, _) => _config.Alterar(c => c.Unidade = "bytes");
        var itemBits = new MenuItem { Header = "Mbps (bits, como a operadora)", IsChecked = cfg.Unidade == "bits" };
        itemBits.Click += (_, _) => _config.Alterar(c => c.Unidade = "bits");
        itemUnidade.Items.Add(itemBytes);
        itemUnidade.Items.Add(itemBits);
        menu.Items.Add(itemUnidade);

        var itemFixar = new MenuItem { Header = "Fixar na barra", IsChecked = cfg.FixarNaBarra };
        itemFixar.Click += (_, _) => _config.Alterar(c => c.FixarNaBarra = !c.FixarNaBarra);
        menu.Items.Add(itemFixar);

        var itemInicio = new MenuItem { Header = "Iniciar com o Windows", IsChecked = _inicio.EstaLigado() };
        itemInicio.Click += (_, _) => DefinirInicioComWindows(!_inicio.EstaLigado());
        menu.Items.Add(itemInicio);

        // Desligar a bandeja com o widget desafixado deixaria o app inalcançável → desabilitado
        var itemBandeja = new MenuItem
        {
            Header = "Mostrar ícone na bandeja",
            IsChecked = cfg.MostrarIconeBandeja || !cfg.FixarNaBarra,
            IsEnabled = cfg.FixarNaBarra
        };
        itemBandeja.Click += (_, _) => _config.Alterar(c => c.MostrarIconeBandeja = !c.MostrarIconeBandeja);
        menu.Items.Add(itemBandeja);

        menu.Items.Add(new Separator());

        var itemSair = new MenuItem { Header = "Sair" };
        itemSair.Click += (_, _) => Shutdown(0);
        menu.Items.Add(itemSair);

        // O widget é NOACTIVATE: sem trazer o processo para frente, o menu não fecharia ao
        // clicar fora (regra clássica dos menus de bandeja — SetForegroundWindow antes de abrir)
        if (_host is not null)
        {
            NativeMethods.SetForegroundWindow(_host.HwndWidget);
        }

        menu.IsOpen = true;
    }

    private void DefinirInicioComWindows(bool ligar)
    {
        if (Environment.ProcessPath is not { } exe)
        {
            return;
        }

        try
        {
            _inicio.Definir(ligar, exe);
            RegistrarLog($"Iniciar com o Windows: {(ligar ? "ligado" : "desligado")} ({exe}).");
        }
        catch (Exception ex)
        {
            RegistrarLog($"Falha ao alterar o início com o Windows: {ex.Message}");
        }

        _painel?.SincronizarComConfiguracoes(_config.Atual, _inicio.EstaLigado());
    }

    // ------------------------------------------------------------------ medição

    private async Task ExecutarLoopMedicaoAsync(bool manterTextoFixoNaTela, CancellationToken ct)
    {
        var cronometro = Stopwatch.StartNew();
        TimeSpan ultimoInstante = cronometro.Elapsed;
        var formatador = new SpeedFormatter();
        int tick = 0;
        int ultimaQtdInterfaces = -1;

        // Contadores da leitura anterior, para o consumo do dia em BYTES REAIS. Zerados quando
        // o medidor é recriado (outro filtro de interfaces = outra soma, a diferença não vale).
        MedidorAtual? medidorAnterior = null;
        ulong recebidosAnt = 0, enviadosAnt = 0;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                tick++;
                TimeSpan agora = cronometro.Elapsed;
                TimeSpan decurso = agora - ultimoInstante;
                ultimoInstante = agora;

                MedidorAtual? medidor = _medidor;
                if (medidor is null)
                {
                    continue;
                }

                LeituraAcumuladaRede leitura = medidor.Sampler.LerContadoresAtuais();
                VelocidadeRede vel = medidor.Calculadora.CalcularProximaAmostra(
                    leitura.TotalBytesRecebidos, leitura.TotalBytesEnviados, decurso);
                bool semConexao = leitura.InterfacesFisicasAtivas == 0;

                ulong deltaRecebidos = 0, deltaEnviados = 0;
                if (ReferenceEquals(medidor, medidorAnterior))
                {
                    // Contador menor que o anterior = interface caiu/trocou: intervalo descartado
                    if (leitura.TotalBytesRecebidos >= recebidosAnt)
                    {
                        deltaRecebidos = leitura.TotalBytesRecebidos - recebidosAnt;
                    }

                    if (leitura.TotalBytesEnviados >= enviadosAnt)
                    {
                        deltaEnviados = leitura.TotalBytesEnviados - enviadosAnt;
                    }
                }

                medidorAnterior = medidor;
                recebidosAnt = leitura.TotalBytesRecebidos;
                enviadosAnt = leitura.TotalBytesEnviados;

                if (leitura.InterfacesFisicasAtivas != ultimaQtdInterfaces)
                {
                    ultimaQtdInterfaces = leitura.InterfacesFisicasAtivas;
                    RegistrarLog($"Interfaces físicas ativas [{leitura.InterfacesFisicasAtivas}]: {leitura.ResumoInterfacesAtivas}");
                }

                if (vel.AmostraValida && !semConexao)
                {
                    _historico.Registrar(
                        vel.UploadBytesPorSegundo, vel.DownloadBytesPorSegundo,
                        deltaEnviados, deltaRecebidos,
                        leitura.NomeInterfacePrincipal, leitura.VelocidadeLinkBitsPorSegundo);
                }

                if (!manterTextoFixoNaTela && _widget is not null)
                {
                    TaskbarWidget w = _widget;
                    _ = w.Dispatcher.InvokeAsync(() =>
                    {
                        if (semConexao)
                        {
                            w.MostrarSemConexao();
                            _bandeja?.DefinirDica("Velocímetro de Internet — sem conexão");
                        }
                        else if (vel.AmostraValida)
                        {
                            w.AtualizarVelocidades(vel.UploadBytesPorSegundo, vel.DownloadBytesPorSegundo);
                            formatador.ModoUnidade = UnidadeDe(_config.Atual);
                            _bandeja?.DefinirDica(
                                $"Velocímetro de Internet\n{formatador.FormatarComSeta("↑", vel.UploadBytesPorSegundo)}   " +
                                $"{formatador.FormatarComSeta("↓", vel.DownloadBytesPorSegundo)}");
                        }

                        _painel?.AtualizarAoVivo(semConexao);
                    });
                }

                // Log enxuto: 1ª dezena de ticks e depois a cada 10 min (RAM para acompanhar a meta)
                if (tick <= 10 || tick % 600 == 0)
                {
                    using var proc = Process.GetCurrentProcess();
                    RegistrarLog(
                        $"[Tick #{tick}] ↑{vel.UploadBytesPorSegundo:F0} B/s ↓{vel.DownloadBytesPorSegundo:F0} B/s | " +
                        $"RAM WorkingSet={proc.WorkingSet64 / 1048576.0:F1} MB (Privada={proc.PrivateMemorySize64 / 1048576.0:F1} MB)");
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

    // ------------------------------------------------------------------ log

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

    /// <summary>Rodízio simples: passou de 1 MB, o log antigo vira .old (app roda 24/7).</summary>
    private void LimitarTamanhoLog()
    {
        try
        {
            var info = new FileInfo(_caminhoLog);
            if (info.Exists && info.Length > 1_048_576)
            {
                File.Move(_caminhoLog, _caminhoLog + ".old", overwrite: true);
            }
        }
        catch
        {
            // Sem permissão: segue gravando no mesmo arquivo
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _ctsMedicao?.Cancel();
        _historico.SalvarConsumo();
        _bandeja?.Dispose();
        _host?.Dispose();
        _mutexInstanciaUnica?.Dispose();
        base.OnExit(e);
    }
}
