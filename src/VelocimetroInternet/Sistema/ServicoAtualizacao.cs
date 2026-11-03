using System.Globalization;
using System.Net.Http;
using Velopack;
using Velopack.Sources;
using VelocimetroInternet.Recursos;

namespace VelocimetroInternet.Sistema;

/// <summary>
/// Atualização automática SILENCIOSA (v0.4.0, Onda 4): procura versão nova nas Releases do
/// GitHub, baixa em segundo plano (de preferência só a diferença) e a aplica reiniciando o app
/// quando o <see cref="App"/> decide que é um momento tranquilo (ver <see cref="RegraMomentoTranquilo"/>).
/// </summary>
/// <remarks>
/// <para><b>Origem:</b> o endereço DIRETO <see cref="EnderecoFeed"/> do repositório público.
/// Não usa o <c>GithubSource</c> do Velopack porque ele consulta <c>api.github.com</c>, que sem
/// login aceita só 60 consultas por hora POR IP — numa rede compartilhada esgota e a
/// atualização falharia calada (lição do app Lembretes, v1.7.5). Repositório público = nenhuma
/// senha embutida no .exe.</para>
/// <para><b>Quando age:</b> só na cópia INSTALADA pelo Setup (<c>%LOCALAPPDATA%\VelocimetroInternetApp</c>).
/// Rodando de <c>bin\</c> ou <c>dist\</c>, <see cref="EstaInstalado"/> é falso e nada acontece.</para>
/// <para><b>Falhas</b> (sem internet, GitHub fora, disco cheio) nunca interrompem o usuário:
/// o app segue na versão atual, registra o motivo em <see cref="UltimaConsulta"/> e tenta de novo
/// no próximo ciclo.</para>
/// </remarks>
public sealed class ServicoAtualizacao
{
    /// <summary>Repositório cujas Releases são o canal de atualização.</summary>
    // v0.6.0: repositório renomeado de velocimetro-internet para status-monitor (o GitHub redireciona
    // o endereço antigo com 301, por isso as cópias 0.5.x instaladas ainda chegam aqui).
    public const string UrlRepositorio = "https://github.com/helveciopereira/status-monitor";

    /// <summary>
    /// O GitHub redireciona <c>.../releases/latest/download/&lt;arquivo&gt;</c> para o arquivo da
    /// última Release publicada (nem rascunho, nem pré-lançamento) — sem limite de consultas.
    /// </summary>
    public const string EnderecoFeed = UrlRepositorio + "/releases/latest/download/";

    /// <summary>
    /// Variável de ambiente que troca a origem por uma PASTA local — só para o ensaio do ciclo
    /// completo (instalar 1.0.0 → publicar 1.0.1 → ver se atualiza) sem publicar nada no GitHub.
    /// </summary>
    public const string VariavelOrigemTeste = "VELOCIMETRO_UPDATE_SOURCE";

    private readonly UpdateManager _gerenciador;

    /// <summary>Atualização já baixada, esperando um momento tranquilo para ser aplicada.</summary>
    private UpdateInfo? _pendente;

    /// <summary>1 = consulta em andamento (o temporizador e a retomada podem disparar juntos).</summary>
    private int _consultando;

    public ServicoAtualizacao()
    {
        string? origemTeste = Environment.GetEnvironmentVariable(VariavelOrigemTeste);
        // v1.8.0: BaixadorHttp = um HttpClient para o processo inteiro. O baixador padrão do Velopack
        // criava um por pedido e deixava handles para trás (medido: +13 handles em 30 consultas,
        // contra +1 fixo com o nosso). Ver BaixadorHttp.
        _gerenciador = string.IsNullOrWhiteSpace(origemTeste)
            ? new UpdateManager(new SimpleWebSource(EnderecoFeed, new BaixadorHttp()))
            : new UpdateManager(origemTeste);
    }

    /// <summary>true na cópia instalada pelo Setup; false rodando de bin\ ou dist\.</summary>
    public bool EstaInstalado => _gerenciador.IsInstalled;

    /// <summary>Versão instalada (ex.: "0.4.0"), ou null fora da instalação.</summary>
    public string? VersaoInstalada => _gerenciador.CurrentVersion?.ToString();

    /// <summary>Há versão nova baixada esperando para ser aplicada.</summary>
    public bool TemPendente => _pendente is not null;

    /// <summary>Versão que será aplicada (ex.: "0.4.1"), ou null.</summary>
    public string? VersaoPendente => _pendente?.TargetFullRelease.Version.ToString();

    /// <summary>Resumo da última consulta concluída ("14:05 — em dia", "sem internet"...), para log e estúdio.</summary>
    public string? UltimaConsulta { get; private set; }

    /// <summary>
    /// Procura versão nova e, se houver, baixa. Silencioso e à prova de falhas.
    /// </summary>
    /// <returns>true se ficou uma atualização baixada pronta para aplicar.</returns>
    public async Task<bool> VerificarEBaixarAsync(CancellationToken ct = default)
    {
        if (!EstaInstalado || _pendente is not null)
        {
            return _pendente is not null;
        }

        if (Interlocked.Exchange(ref _consultando, 1) == 1)
        {
            return false; // outra consulta ainda em andamento
        }

        try
        {
            UpdateInfo? info = await _gerenciador.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
            {
                _ultima = new ResultadoConsulta(DateTime.Now, null, null, null);
                UltimaConsulta = $"{DateTime.Now:dd/MM HH:mm} — em dia";
                return false;
            }

            await _gerenciador.DownloadUpdatesAsync(info, null, ct).ConfigureAwait(false);
            _pendente = info;
            _ultima = new ResultadoConsulta(DateTime.Now, VersaoPendente, null, null);
            UltimaConsulta = $"{DateTime.Now:dd/MM HH:mm} — versão {VersaoPendente} baixada, instala sozinha";
            return true;
        }
        catch (Exception ex)
        {
            // Captura ampla INTENCIONAL: o Velopack não documenta um conjunto fechado de exceções
            // e esta rotina roda sozinha em segundo plano — qualquer falha só adia a atualização,
            // NUNCA derruba o velocímetro. O motivo vira texto legível em vez de sumir.
            _ultima = new ResultadoConsulta(DateTime.Now, null, ClassificarMotivo(ex), ex.GetType().Name);
            UltimaConsulta = $"{DateTime.Now:dd/MM HH:mm} — {Classificar(ex)}";
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _consultando, 0);
        }
    }

    /// <summary>
    /// Aplica a versão baixada e reabre o app já atualizado. O processo atual TERMINA aqui —
    /// o chamador precisa ter salvo tudo antes (consumo do dia, ícone da bandeja removido).
    /// </summary>
    public void AplicarEReiniciar()
    {
        if (_pendente is { } info)
        {
            _gerenciador.ApplyUpdatesAndRestart(info.TargetFullRelease);
        }
    }

    /// <summary>
    /// "Sair" do menu com versão baixada: instala assim que o processo terminar, SEM reabrir
    /// (o usuário pediu para fechar). Na próxima abertura já é a versão nova.
    /// </summary>
    public void AplicarAoSair()
    {
        if (_pendente is { } info)
        {
            _gerenciador.WaitExitThenApplyUpdates(info.TargetFullRelease, silent: true, restart: false);
        }
    }

    /// <summary>Traduz a falha para uma frase curta em português (vai para o log; testes da Onda 4).</summary>
    public static string Classificar(Exception ex) =>
        TextoMotivo(ClassificarMotivo(ex), ex.GetType().Name, CultureInfo.GetCultureInfo("pt-BR"));

    // ---------------------------------------------------------------- v1.9.0: a consulta no idioma da interface

    /// <summary>Por que a última consulta falhou (classes de erro que o rodapé do estúdio distingue).</summary>
    public enum MotivoFalha { NaoPublicada, Limitou, SemInternet, TempoEsgotado, Disco, Outro }

    /// <summary>Última consulta: quando, versão baixada (se houve) ou a falha (motivo + tipo da exceção).</summary>
    private sealed record ResultadoConsulta(DateTime Quando, string? VersaoBaixada, MotivoFalha? Falha, string? TipoExcecao);

    private ResultadoConsulta? _ultima;

    /// <summary>A mesma classificação de <see cref="Classificar"/>, sem texto — para traduzir depois.</summary>
    public static MotivoFalha ClassificarMotivo(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } => MotivoFalha.NaoPublicada,
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests } => MotivoFalha.Limitou,
        HttpRequestException => MotivoFalha.SemInternet,
        TaskCanceledException or TimeoutException => MotivoFalha.TempoEsgotado,
        System.IO.IOException => MotivoFalha.Disco,
        _ => MotivoFalha.Outro
    };

    /// <summary>Frase curta do motivo na cultura pedida ("tempo esgotado" / "timed out").</summary>
    public static string TextoMotivo(MotivoFalha motivo, string? tipoExcecao, CultureInfo cultura) => motivo switch
    {
        MotivoFalha.NaoPublicada => Textos.Obter("Atualizacao_NaoPublicada", cultura),
        MotivoFalha.Limitou => Textos.Obter("Atualizacao_Limitou", cultura),
        MotivoFalha.SemInternet => Textos.Obter("Atualizacao_SemInternet", cultura),
        MotivoFalha.TempoEsgotado => Textos.Obter("Atualizacao_TempoEsgotado", cultura),
        MotivoFalha.Disco => Textos.Obter("Atualizacao_Disco", cultura),
        _ => string.Format(cultura, Textos.Obter("Atualizacao_Falhou", cultura), tipoExcecao)
    };

    /// <summary>
    /// "03/11 18:20 — em dia" / "11/03 18:20 — up to date": a última consulta no idioma da interface, para
    /// o rodapé do estúdio. null enquanto nenhuma consulta terminou. (<see cref="UltimaConsulta"/> é a
    /// versão em português, do log.)
    /// </summary>
    public string? TextoUltimaConsulta(CultureInfo cultura)
    {
        if (_ultima is not { } u)
        {
            return null;
        }

        string quando = u.Quando.ToString(cultura.TwoLetterISOLanguageName == "en" ? "MM/dd HH:mm" : "dd/MM HH:mm", cultura);
        string estado = u.Falha is { } f ? TextoMotivo(f, u.TipoExcecao, cultura)
            : u.VersaoBaixada is { } v ? string.Format(cultura, Textos.Obter("Atualizacao_Baixada", cultura), v)
            : Textos.Obter("Atualizacao_EmDia", cultura);
        return $"{quando} — {estado}";
    }
}

/// <summary>
/// Regra pura (testável) do "momento tranquilo" para reiniciar e atualizar: o usuário não pode
/// perceber nada — então nada aberto, nada sob o mouse, nada em tela cheia, medição não pausada.
/// </summary>
public static class RegraMomentoTranquilo
{
    /// <param name="painelAberto">Painel rápido visível.</param>
    /// <param name="estudioAberto">Estúdio de cores aberto (pode ter edição não aplicada).</param>
    /// <param name="menuAberto">Menu do clique direito aberto.</param>
    /// <param name="mouseSobreOApp">Cursor sobre o widget (o usuário pode estar prestes a clicar).</param>
    /// <param name="telaCheia">Jogo/vídeo em tela cheia — o reinício pode gerar um soluço de CPU.</param>
    /// <param name="pausado">"Pausar medição" ligado — vale só na sessão; reiniciar despausaria.</param>
    public static bool Avaliar(bool painelAberto, bool estudioAberto, bool menuAberto,
        bool mouseSobreOApp, bool telaCheia, bool pausado) =>
        !painelAberto && !estudioAberto && !menuAberto && !mouseSobreOApp && !telaCheia && !pausado;
}
