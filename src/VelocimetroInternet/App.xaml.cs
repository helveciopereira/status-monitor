using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
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
///
/// v0.4.0 (Onda 4) — instalador (Velopack) e atualização automática SILENCIOSA: consulta o
/// GitHub 45 s após abrir e depois a cada 5 min (v0.4.2), baixa em segundo plano e reinicia sozinho
/// num momento tranquilo (ver <see cref="ServicoAtualizacao"/> e <see cref="RegraMomentoTranquilo"/>).
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

    /// <summary>
    /// "Pausar medição" do menu (v0.3.0). Só vale na sessão — reabrir o app volta a medir,
    /// para ninguém esquecer o velocímetro parado para sempre.
    /// </summary>
    private volatile bool _pausado;

    /// <summary>
    /// Preenchido pelo Program.cs quando o processo foi reaberto pelo atualizador (versão nova).
    /// Só vai para o log: o pedido do dono é atualizar sem atrapalhar — nada de aviso na tela.
    /// </summary>
    public static string? AtualizadoParaVersao { get; set; }

    /// <summary>Atualização automática (v0.4.0). Só age na cópia instalada pelo Setup.</summary>
    private readonly ServicoAtualizacao _atualizacao = new();

    /// <summary>Relógio da atualização: consulta (45 s, depois 5 min) e, com versão baixada, procura o momento tranquilo (1 min).</summary>
    private DispatcherTimer? _timerAtualizacao;

    /// <summary>Menu do clique direito aberto — não reiniciar por baixo do usuário.</summary>
    private bool _menuAberto;

    private static readonly TimeSpan PrimeiraConsulta = TimeSpan.FromSeconds(45);
    /// <summary>
    /// v0.4.2 (pedido do dono): 1 h → 5 min, para a versão nova chegar logo. Barato: o feed
    /// direto (releases/latest/download) não tem o limite de 60 consultas/h da API do GitHub e
    /// cada consulta baixa só o releases.win.json (~1 KB) — 12 por hora é desprezível.
    /// </summary>
    private static readonly TimeSpan IntervaloConsulta = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IntervaloMomentoTranquilo = TimeSpan.FromMinutes(1);
    /// <summary>Após acordar da suspensão: espera o Wi-Fi voltar antes de consultar.</summary>
    private static readonly TimeSpan ConsultaAposRetomada = TimeSpan.FromSeconds(30);

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
        RegistrarLog($"=== Status Monitor v{VersaoApp} | Unidade={cfg.Unidade} | Paleta={cfg.Paleta} | Fixar={cfg.FixarNaBarra} | Bandeja={cfg.MostrarIconeBandeja} | CpuRam={cfg.MostrarCpuRam} | Intervalo={cfg.IntervaloSegundos}s | Fonte={cfg.Fonte}/{cfg.TamanhoTexto} | DownEmCima={cfg.DownloadEmCima} | TodasBarras={cfg.MostrarEmTodasAsBarras} | Manuais={cfg.PosicoesManuais.Count} ===");
        RegistrarLog(_atualizacao.EstaInstalado
            ? $"Instalado pelo Setup (v{_atualizacao.VersaoInstalada}) — atualização automática ativa ({Environment.ProcessPath})."
            : $"Cópia solta, fora da instalação — sem atualização automática ({Environment.ProcessPath}).");
        if (AtualizadoParaVersao is not null)
        {
            RegistrarLog($"Reaberto pelo atualizador: agora na versão {AtualizadoParaVersao}.");
        }

        ServicoTema.Aplicar(TemaWindows.AppsClaros());

        _widget = CriarWidgetConfigurado();
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
            ForcarModoCompactoTeste = usarCompacto,
            // v0.5.0: antes do InicializarAsync, para a 1ª posição já sair com a largura certa
            MostrarSistema = cfg.MostrarCpuRam,
            // v0.8.0: idem para a fonte/tamanho e a posição escolhida com Ctrl + arrastar
            FatorTexto = _fatorTexto,
            PosicaoManualLogica = PosicaoManualDe(cfg, "principal")
        };
        _host.PosicaoManualEscolhida += GravarPosicaoManual;
        await _host.InicializarAsync();
        _host.FixadoNaBarra = cfg.FixarNaBarra;

        // v0.8.0: widgets nas barras dos outros monitores (opção do estúdio, padrão desligada)
        _secundarias = new GerenciadorBarrasSecundarias(CriarWidgetConfigurado, ConfigurarHostSecundario, RegistrarLog);
        _secundarias.Ativo = cfg.MostrarEmTodasAsBarras;
        _historico.DefinirIntervalo(cfg.IntervaloSegundos);

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

        TaskbarHost hostPrincipal = _host;
        _widget.CliqueEsquerdo += () => AlternarPainel(veioDaBandeja: false, hostPrincipal);
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

        // Entrada de início apontando para outro .exe: a cópia INSTALADA assume (é assim que a
        // instalação substitui o antigo dist\); cópia solta só corrige se o .exe registrado
        // sumiu — rodar um teste de bin\ não rouba mais a entrada do app instalado (v0.4.0).
        if (Environment.ProcessPath is { } exe && _inicio.DeveReapontar(exe, _atualizacao.EstaInstalado))
        {
            _inicio.Definir(true, exe);
            RegistrarLog($"Entrada de início com o Windows reapontada para {exe}.");
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

        IniciarAtualizacaoAutomatica();
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

    // ------------------------------------------------------------------ v0.8.0: aparência, posição, monitores

    /// <summary>Widgets das barras dos outros monitores (null antes do OnStartup terminar).</summary>
    private GerenciadorBarrasSecundarias? _secundarias;

    /// <summary>Fator de largura da fonte/tamanho atuais (medido uma vez por mudança, não a cada tick).</summary>
    private double _fatorTexto = 1.0;

    private static AparenciaWidget AparenciaDe(Configuracoes cfg) => new(cfg.Fonte, cfg.TamanhoTexto, cfg.DownloadEmCima);

    private static int? PosicaoManualDe(Configuracoes cfg, string chave) =>
        cfg.PosicoesManuais.TryGetValue(chave, out int x) ? x : null;

    /// <summary>Widget principal e os das barras secundárias — tudo que mostra números na tela.</summary>
    private IEnumerable<TaskbarWidget> TodosWidgets()
    {
        if (_widget is not null)
        {
            yield return _widget;
        }

        if (_secundarias is not null)
        {
            foreach ((TaskbarWidget w, _) in _secundarias.Pares)
            {
                yield return w;
            }
        }
    }

    /// <summary>Hosts de todas as barras (principal primeiro).</summary>
    private IEnumerable<TaskbarHost> TodosHosts()
    {
        if (_host is not null)
        {
            yield return _host;
        }

        if (_secundarias is not null)
        {
            foreach ((_, TaskbarHost h) in _secundarias.Pares)
            {
                yield return h;
            }
        }
    }

    /// <summary>Widget novo já com unidade, CPU/RAM, cores e aparência da configuração atual.</summary>
    private TaskbarWidget CriarWidgetConfigurado()
    {
        Configuracoes cfg = _config.Atual;
        var widget = new TaskbarWidget(UnidadeDe(cfg));
        AparenciaWidget aparencia = AparenciaDe(cfg);
        _fatorTexto = aparencia.FatorLargura();
        widget.AplicarAparencia(aparencia, _fatorTexto);
        widget.DefinirMostrarSistema(cfg.MostrarCpuRam);
        widget.AplicarCores(Paletas.Resolver(
            Paletas.Obter(cfg.Paleta, cfg.CorUploadPersonalizada, cfg.CorDownloadPersonalizada),
            TemaWindows.BarraClara()));
        // v1.0.0 (Onda 5): dica ao passar o mouse e duplo clique → Gerenciador de Tarefas (todas as barras)
        widget.PedirDica = MontarDicaWidget;
        widget.DuploClique += AbrirGerenciadorDeTarefas;
        return widget;
    }

    // ------------------------------------------------------------------ v1.0.0: dica, duplo clique, franquia

    /// <summary>Última leitura do loop: sem interface física conectada (a dica diz "Sem conexão").</summary>
    private volatile bool _semConexao;

    /// <summary>Texto da dica do widget, montado só quando ela vai abrir (IP lido na hora).</summary>
    private string? MontarDicaWidget()
    {
        if (_pausado)
        {
            return "Status Monitor — medição pausada (clique direito › Pausar medição para retomar)";
        }

        Configuracoes cfg = _config.Atual;
        InstantaneoHistorico h = _historico.Capturar();
        return DicaWidget.Montar(h, _semConexao ? null : InfoRede.IpLocal(h.NomeInterface), UnidadeDe(cfg),
            _historico.FracaoFranquia(cfg.FranquiaMensalGb), _semConexao);
    }

    /// <summary>
    /// Duplo clique: o 1º clique abriu o painel — fecha e abre o Gerenciador de Tarefas.
    /// UseShellExecute: o taskmgr se eleva sozinho; se o Windows pedir confirmação e o usuário
    /// cancelar, vem Win32Exception — vai para o log, sem janela de erro (app silencioso).
    /// </summary>
    private void AbrirGerenciadorDeTarefas()
    {
        _painel?.Fechar();
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            RegistrarLog($"Gerenciador de Tarefas não abriu: {ex.Message}");
        }
    }

    /// <summary>
    /// Referência da cor por intensidade (bytes/s): plano contratado; sem plano, o link da placa.
    /// 0 = opção desligada (ou nada conhecido) → números com opacidade normal.
    /// </summary>
    private static double ReferenciaIntensidade(Configuracoes cfg, ulong linkBitsPorSegundo) =>
        !cfg.CorPorIntensidade ? 0
        : cfg.VelocidadeContratadaMbps > 0 ? cfg.VelocidadeContratadaMbps * 1_000_000 / 8
        : linkBitsPorSegundo / 8.0;

    /// <summary>
    /// Franquia: confere o marco (80/100 %) e avisa uma vez. Com o ícone da bandeja oculto não há
    /// de onde a notificação sair — fica no log, e o painel mostra o uso do mês de qualquer forma.
    /// </summary>
    private void ConferirFranquia()
    {
        double franquia = _config.Atual.FranquiaMensalGb;
        if (_historico.MarcoFranquiaParaAvisar(franquia) is not int marco)
        {
            return;
        }

        string texto = marco >= 100
            ? $"A franquia de {franquia:0.##} GB deste mês acabou. A operadora pode reduzir a velocidade."
            : $"Você já usou {marco} % da franquia de {franquia:0.##} GB deste mês.";
        _ = Dispatcher.InvokeAsync(() =>
        {
            bool mostrou = _bandeja?.MostrarAviso("Status Monitor — franquia", texto) == true;
            RegistrarLog($"Franquia {marco} %: {(mostrou ? "aviso mostrado" : "ícone oculto, só no painel")}.");
        });
    }

    /// <summary>Host de barra secundária: mesmas opções do principal; clique abre o painel ancorado NELE.</summary>
    private void ConfigurarHostSecundario(TaskbarWidget widget, TaskbarHost host)
    {
        Configuracoes cfg = _config.Atual;
        host.MostrarSistema = cfg.MostrarCpuRam;
        host.FatorTexto = _fatorTexto;
        host.PosicaoManualLogica = PosicaoManualDe(cfg, host.ChavePosicao);
        host.FixadoNaBarra = cfg.FixarNaBarra;
        host.PosicaoManualEscolhida += GravarPosicaoManual;
        widget.CliqueEsquerdo += () => AlternarPainel(veioDaBandeja: false, host);
        widget.CliqueDireito += AbrirMenu;
    }

    /// <summary>Ctrl + arrastar terminou: grava (ou apaga, se voltou ao automático) a posição daquela barra.</summary>
    private void GravarPosicaoManual(string chave, int? xLogico) =>
        _config.Alterar(c =>
        {
            if (xLogico is int x)
            {
                c.PosicoesManuais[chave] = x;
            }
            else
            {
                c.PosicoesManuais.Remove(chave);
            }
        });

    /// <summary>Aplica tudo o que mudou. Chamado após cada Salvar (menu, painel ou estúdio).</summary>
    private void AoAlterarConfiguracoes(Configuracoes cfg)
    {
        if (_widget is null || _host is null)
        {
            return;
        }

        // v0.8.0: a fonte/tamanho só são medidos de novo quando mudam
        AparenciaWidget aparencia = AparenciaDe(cfg);
        _fatorTexto = aparencia.FatorLargura();

        foreach (TaskbarWidget w in TodosWidgets())
        {
            w.DefinirUnidade(UnidadeDe(cfg));
            w.AplicarAparencia(aparencia, _fatorTexto);
            w.DefinirMostrarSistema(cfg.MostrarCpuRam);
        }

        foreach (TaskbarHost h in TodosHosts())
        {
            h.MostrarSistema = cfg.MostrarCpuRam; // recalcula a largura na barra se mudou
            h.FatorTexto = _fatorTexto;
            h.PosicaoManualLogica = PosicaoManualDe(cfg, h.ChavePosicao);
            h.FixadoNaBarra = cfg.FixarNaBarra;
        }

        if (_secundarias is not null)
        {
            _secundarias.Ativo = cfg.MostrarEmTodasAsBarras;
        }

        _historico.DefinirIntervalo(cfg.IntervaloSegundos);
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
        foreach (TaskbarWidget w in TodosWidgets()) // v0.8.0: inclui as barras dos outros monitores
        {
            w.AplicarCores(cores);
        }

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

        // 3) Voltou de suspensão/hibernação: linha de base nova (a média móvel não pode carregar
        //    o valor de antes de dormir) e reencaixe (a barra pode ter mudado de monitor/DPI)
        if (msg == NativeMethods.WM_POWERBROADCAST && wParam == NativeMethods.PBT_APMRESUMEAUTOMATIC)
        {
            RegistrarLog("Retomada após suspensão: medidor reiniciado e widget reencaixado.");
            RecriarMedidor(_config.Atual);
            if (_host is not null)
            {
                _ = _host.AtualizarPosicaoAsync(forcarReencaixe: true);
            }

            // O temporizador atrasado dispararia ao acordar ANTES de o Wi-Fi voltar: reagenda
            ReagendarConsulta(ConsultaAposRetomada);
            return nint.Zero;
        }

        // 4) Tema claro/escuro alterado nas Configurações do Windows
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

    /// <param name="origem">v0.8.0: host do widget clicado (outro monitor) — o painel abre junto DELE. null = principal.</param>
    private void AlternarPainel(bool veioDaBandeja, TaskbarHost? origem = null)
    {
        // Clique no widget/bandeja com o painel aberto: o painel já fechou ao perder o foco
        // seguido deste clique → não reabrir (o clique era para FECHAR)
        if (_painel is { IsVisible: true } || _painel?.FechadoHaPouco == true)
        {
            _painel?.Fechar();
            return;
        }

        AbrirPainel(veioDaBandeja, origem);
    }

    private void AbrirPainel(bool veioDaBandeja, TaskbarHost? origem = null)
    {
        TaskbarHost? ancora = origem ?? _host;
        if (ancora is null)
        {
            return;
        }

        if (_painel is null)
        {
            _painel = new PainelRapido(_historico);
            _painel.UnidadeEscolhida += bits => _config.Alterar(c => c.Unidade = bits ? "bits" : "bytes");
            _painel.FixarAlterado += fixar => _config.Alterar(c => c.FixarNaBarra = fixar);
            _painel.MostrarSistemaAlterado += mostrar => _config.Alterar(c => c.MostrarCpuRam = mostrar);
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
        _painel.AbrirAncorado(ancora.UltimaPosicao, ancora.HwndWidget, veioDaBandeja);
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

        _estudio = new EstudioCores(_config, TextoVersaoRodape(), _historico); // v1.0.0: CSV e consumo do mês
        _estudio.Closed += (_, _) =>
        {
            _estudio = null;
            AparaMemoriaEmBreve();
        };
        _estudio.Show();
        _estudio.Activate();
    }

    /// <summary>Rodapé do estúdio: versão e se/como a atualização automática está agindo.</summary>
    private string TextoVersaoRodape()
    {
        if (!_atualizacao.EstaInstalado)
        {
            return $"Versão {VersaoApp} · cópia sem instalador (sem auto update)";
        }

        string estado = _atualizacao.TemPendente
            ? $"versão {_atualizacao.VersaoPendente} pronta, instala sozinha"
            // v0.4.3 (pedido do dono): o rótulo "atualiza sozinho" virou "auto update", mais curto
            : _atualizacao.UltimaConsulta is { } u ? $"auto update · consulta {u}" : "auto update";
        return $"Versão {_atualizacao.VersaoInstalada ?? VersaoApp} · {estado}";
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

        // v0.5.0: CPU/RAM (%) à esquerda da internet
        var itemSistema = new MenuItem { Header = "Mostrar CPU e RAM", IsChecked = cfg.MostrarCpuRam };
        itemSistema.Click += (_, _) => _config.Alterar(c => c.MostrarCpuRam = !c.MostrarCpuRam);
        menu.Items.Add(itemSistema);

        // v0.8.0: desfaz o Ctrl + arrastar (todas as barras voltam à vaga ao lado do '^')
        var itemAutomatica = new MenuItem
        {
            Header = "Voltar à posição automática",
            IsEnabled = cfg.PosicoesManuais.Count > 0,
            ToolTip = "Para mudar o widget de lugar: segure Ctrl e arraste-o pela barra."
        };
        itemAutomatica.Click += (_, _) => _config.Alterar(c => c.PosicoesManuais.Clear());
        menu.Items.Add(itemAutomatica);

        // v1.0.0: atalho também pelo menu (o duplo clique não é descobrível sozinho)
        var itemTaskmgr = new MenuItem { Header = "Abrir Gerenciador de Tarefas" };
        itemTaskmgr.Click += (_, _) => AbrirGerenciadorDeTarefas();
        menu.Items.Add(itemTaskmgr);

        var itemPausar = new MenuItem { Header = "Pausar medição", IsChecked = _pausado };
        itemPausar.Click += (_, _) => DefinirPausa(!_pausado);
        menu.Items.Add(itemPausar);

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
        itemSair.Click += (_, _) =>
        {
            // Versão nova já baixada: instala depois que o processo fechar, sem reabrir
            if (_atualizacao.TemPendente)
            {
                RegistrarLog($"Sair com a versão {_atualizacao.VersaoPendente} baixada: instala ao fechar.");
                try
                {
                    _atualizacao.AplicarAoSair();
                }
                catch (Exception ex)
                {
                    RegistrarLog($"Falha ao agendar a atualização na saída: {ex.Message}");
                }
            }

            Shutdown(0);
        };
        menu.Items.Add(itemSair);

        // O widget é NOACTIVATE: sem trazer o processo para frente, o menu não fecharia ao
        // clicar fora (regra clássica dos menus de bandeja — SetForegroundWindow antes de abrir)
        if (_host is not null)
        {
            NativeMethods.SetForegroundWindow(_host.HwndWidget);
        }

        menu.Opened += (_, _) => _menuAberto = true;
        menu.Closed += (_, _) => _menuAberto = false;
        menu.IsOpen = true;
    }

    /// <summary>Pausa/retoma. Ao retomar, linha de base nova — senão o 1º tick somaria a pausa toda.</summary>
    private void DefinirPausa(bool pausar)
    {
        _pausado = pausar;
        if (pausar)
        {
            foreach (TaskbarWidget w in TodosWidgets())
            {
                w.MostrarPausado();
            }

            _bandeja?.DefinirDica("Status Monitor — medição pausada");
        }
        else
        {
            RecriarMedidor(_config.Atual);
        }

        RegistrarLog(pausar ? "Medição pausada pelo usuário." : "Medição retomada.");
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

    // ------------------------------------------------------------------ atualização automática

    /// <summary>
    /// Liga o relógio da atualização. Fora da instalação (bin\, dist\) não faz nada — o Velopack
    /// não tem onde aplicar e o desenvolvedor não quer o app se substituindo.
    /// </summary>
    private void IniciarAtualizacaoAutomatica()
    {
        if (!_atualizacao.EstaInstalado)
        {
            return;
        }

        _timerAtualizacao = new DispatcherTimer(DispatcherPriority.Background);
        _timerAtualizacao.Tick += async (_, _) => await AoTickAtualizacaoAsync();
        ReagendarConsulta(PrimeiraConsulta);
    }

    private void ReagendarConsulta(TimeSpan espera)
    {
        if (_timerAtualizacao is null || _atualizacao.TemPendente)
        {
            return; // fora da instalação, ou já baixou e está só esperando o momento tranquilo
        }

        _timerAtualizacao.Stop();
        _timerAtualizacao.Interval = espera;
        _timerAtualizacao.Start();
    }

    /// <summary>
    /// Sem versão baixada: consulta/baixa e volta em 5 min. Com versão baixada: a cada minuto
    /// confere o momento tranquilo e, se for, salva e reinicia na versão nova.
    /// </summary>
    private async Task AoTickAtualizacaoAsync()
    {
        if (_timerAtualizacao is null)
        {
            return;
        }

        _timerAtualizacao.Stop();

        if (!_atualizacao.TemPendente)
        {
            bool baixou = await _atualizacao.VerificarEBaixarAsync();
            RegistrarLog($"Atualização: {_atualizacao.UltimaConsulta}.");
            _timerAtualizacao.Interval = baixou ? IntervaloMomentoTranquilo : IntervaloConsulta;
            _timerAtualizacao.Start();
            return;
        }

        if (!MomentoTranquiloAgora())
        {
            _timerAtualizacao.Start(); // tenta de novo daqui a 1 min
            return;
        }

        RegistrarLog($"Momento tranquilo: aplicando a versão {_atualizacao.VersaoPendente} e reiniciando.");
        // O processo termina dentro do Apply (não passa pelo OnExit): salva o consumo do dia e
        // tira o ícone da bandeja agora, senão sobraria um ícone "fantasma" até passar o mouse.
        _historico.SalvarConsumo();
        if (_bandeja is not null)
        {
            _bandeja.Visivel = false;
        }

        try
        {
            _atualizacao.AplicarEReiniciar();
        }
        catch (Exception ex)
        {
            // Falhou antes de encerrar: o velocímetro segue normal na versão atual
            RegistrarLog($"Falha ao aplicar a atualização: {ex.Message}");
            AtualizarVisibilidadeBandeja();
            _timerAtualizacao.Interval = IntervaloConsulta;
            _timerAtualizacao.Start();
        }
    }

    /// <summary>Lê o estado real da tela e aplica a <see cref="RegraMomentoTranquilo"/>.</summary>
    private bool MomentoTranquiloAgora()
    {
        bool mouseSobre = false;
        if (_host is not null && NativeMethods.GetCursorPos(out NativeMethods.POINT p))
        {
            // Janela sob o cursor, subindo até a de topo. NÃO usar IsMouseOver/IsActive do WPF:
            // abertas por processo em segundo plano (o próprio reinício) elas ficam presas e enganam.
            nint sob = NativeMethods.WindowFromPoint(p);
            nint raiz = sob == nint.Zero ? nint.Zero : NativeMethods.GetAncestor(sob, NativeMethods.GA_ROOT);
            mouseSobre = raiz != nint.Zero && TodosHosts().Any(h => h.HwndWidget == raiz); // v0.8.0: qualquer monitor
        }

        return RegraMomentoTranquilo.Avaliar(
            painelAberto: _painel is { IsVisible: true },
            estudioAberto: _estudio is not null,
            menuAberto: _menuAberto,
            mouseSobreOApp: mouseSobre,
            telaCheia: _host?.EmTelaCheia == true,
            pausado: _pausado);
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

        // v0.5.0: CPU/RAM do Windows. Lido em TODO tick (custo: 2 chamadas de kernel, sem
        // alocação) — assim, ao ligar a opção, a base da CPU já é a do segundo anterior.
        var medidorSistema = new MedidorSistema();

        // v0.8.0: intervalo configurável (0,5 / 1 / 2 / 5 s). PeriodicTimer.Period aceita troca em
        // pleno funcionamento — a mudança no estúdio vale a partir do próximo tick, sem recriar o loop.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_config.Atual.IntervaloSegundos));
        // Log "longo" a cada 10 min por RELÓGIO (antes: a cada 600 ticks, que só era 10 min com 1 s)
        TimeSpan proximoLogLongo = TimeSpan.FromMinutes(10);
        // v1.0.0: 1ª conferência da franquia logo no 1º tick (aviso de quem abriu o PC já acima de 80 %)
        TimeSpan proximaConferenciaFranquia = TimeSpan.Zero;
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                TimeSpan periodoDesejado = TimeSpan.FromSeconds(_config.Atual.IntervaloSegundos);
                if (timer.Period != periodoDesejado)
                {
                    timer.Period = periodoDesejado;
                    RegistrarLog($"Intervalo de atualização: {periodoDesejado.TotalSeconds:0.#} s.");
                }

                tick++;
                TimeSpan agora = cronometro.Elapsed;
                TimeSpan decurso = agora - ultimoInstante;
                ultimoInstante = agora;

                UsoSistema uso = medidorSistema.Ler();

                MedidorAtual? medidor = _medidor;
                if (medidor is null || _pausado)
                {
                    continue;
                }

                LeituraAcumuladaRede leitura = medidor.Sampler.LerContadoresAtuais();
                VelocidadeRede vel = medidor.Calculadora.CalcularProximaAmostra(
                    leitura.TotalBytesRecebidos, leitura.TotalBytesEnviados, decurso);
                bool semConexao = leitura.InterfacesFisicasAtivas == 0;
                _semConexao = semConexao;

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

                // v1.0.0: franquia conferida a cada 30 s (soma o mês inteiro — não precisa ser por tick)
                if (agora >= proximaConferenciaFranquia)
                {
                    proximaConferenciaFranquia = agora + TimeSpan.FromSeconds(30);
                    ConferirFranquia();
                }

                double referencia = ReferenciaIntensidade(_config.Atual, leitura.VelocidadeLinkBitsPorSegundo);

                if (!manterTextoFixoNaTela && _widget is not null)
                {
                    _ = _widget.Dispatcher.InvokeAsync(() =>
                    {
                        // v0.8.0: lista tirada NA thread da UI (o gerenciador de monitores só mexe nela lá)
                        List<TaskbarWidget> widgets = TodosWidgets().ToList();
                        bool mostrarSistema = _config.Atual.MostrarCpuRam;
                        if (semConexao)
                        {
                            widgets.ForEach(w => w.MostrarSemConexao());
                            _bandeja?.DefinirDica("Status Monitor — sem conexão");
                        }
                        else if (vel.AmostraValida)
                        {
                            widgets.ForEach(w =>
                            {
                                w.DefinirReferenciaIntensidade(referencia); // v1.0.0: 0 = desligada
                                w.AtualizarVelocidades(vel.UploadBytesPorSegundo, vel.DownloadBytesPorSegundo);
                            });
                            formatador.ModoUnidade = UnidadeDe(_config.Atual);
                            // v0.5.0: com CPU/RAM ligados, a dica da bandeja ganha uma 3ª linha
                            string linhaSistema = mostrarSistema
                                ? $"\nCPU {MedidorSistema.Formatar(uso.CpuPercentual)} · RAM {MedidorSistema.Formatar(uso.RamPercentual)}"
                                : "";
                            _bandeja?.DefinirDica(
                                $"Status Monitor\n{formatador.FormatarComSeta("↑", vel.UploadBytesPorSegundo)}   " +
                                $"{formatador.FormatarComSeta("↓", vel.DownloadBytesPorSegundo)}{linhaSistema}");
                        }

                        // v0.5.0: CPU/RAM independem da rede — atualizam mesmo "sem conexão"
                        if (mostrarSistema)
                        {
                            widgets.ForEach(w => w.AtualizarSistema(uso.CpuPercentual, uso.RamPercentual));
                        }

                        _painel?.AtualizarAoVivo(semConexao);
                    });
                }

                // Log enxuto: 1ª dezena de ticks e depois a cada 10 min (RAM para acompanhar a meta)
                bool logLongo = agora >= proximoLogLongo;
                if (logLongo)
                {
                    proximoLogLongo = agora + TimeSpan.FromMinutes(10);
                }

                if (tick <= 10 || logLongo)
                {
                    using var proc = Process.GetCurrentProcess();
                    RegistrarLog(
                        $"[Tick #{tick}] ↑{vel.UploadBytesPorSegundo:F0} B/s ↓{vel.DownloadBytesPorSegundo:F0} B/s | " +
                        $"CPU {MedidorSistema.Formatar(uso.CpuPercentual)} RAM {MedidorSistema.Formatar(uso.RamPercentual)} | " +
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
        _timerAtualizacao?.Stop();
        _ctsMedicao?.Cancel();
        _historico.SalvarConsumo();
        _bandeja?.Dispose();
        _secundarias?.Dispose();
        _host?.Dispose();
        _mutexInstanciaUnica?.Dispose();
        base.OnExit(e);
    }
}
