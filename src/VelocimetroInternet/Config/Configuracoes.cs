namespace VelocimetroInternet.Config;

/// <summary>
/// Todas as preferências do usuário, gravadas em %AppData%\VelocimetroInternet\settings.json.
/// Referência: 02-ARQUITETURA.md §5 e o painel/estúdio do Modelo 3 (design/modelo-3-acrylic-pill).
///
/// Regra de compatibilidade: campo novo SEMPRE com valor padrão aqui — um settings.json antigo
/// sem o campo continua válido (a leitura preenche o padrão).
/// </summary>
public sealed class Configuracoes
{
    /// <summary>Versão do formato do arquivo (para migrações futuras).</summary>
    public int Versao { get; set; } = 1;

    /// <summary>"bytes" (MB/s, padrão escolhido pelo dono) ou "bits" (Mbps, igual ao plano da operadora).</summary>
    public string Unidade { get; set; } = "bytes";

    /// <summary>Fator da média móvel exponencial: 0 = valor cru, 0,9 = muito suave. Padrão 0,5.</summary>
    public double Suavizacao { get; set; } = 0.5;

    /// <summary>"Fixar na Barra" do painel rápido: false esconde o widget (a bandeja continua).</summary>
    public bool FixarNaBarra { get; set; } = true;

    /// <summary>
    /// v0.5.0: mostra CPU (em cima) e RAM (embaixo), em %, à esquerda do upload/download.
    /// Padrão LIGADO: foi pedido do dono e chega assim pela atualização automática; quem não
    /// quiser desliga no painel rápido ou no menu do clique direito.
    /// </summary>
    public bool MostrarCpuRam { get; set; } = true;

    /// <summary>Mostrar o ícone na área de notificação. Se o widget também estiver oculto, a bandeja é forçada.</summary>
    public bool MostrarIconeBandeja { get; set; } = true;

    /// <summary>
    /// Nome da paleta ativa (Onda 3): "classico", "neon", "mono", "alto-contraste", "bio" ou "personalizada".
    /// </summary>
    public string Paleta { get; set; } = "classico";

    /// <summary>Cor de upload da paleta personalizada (tema escuro). As variantes do tema claro são derivadas.</summary>
    public string CorUploadPersonalizada { get; set; } = "#4CC2FF";

    /// <summary>Cor de download da paleta personalizada (tema escuro).</summary>
    public string CorDownloadPersonalizada { get; set; } = "#FF6B6B";

    /// <summary>
    /// Velocidade contratada em Mbps (download). 0 = desconhecida → as barras do painel usam a
    /// velocidade de LINK da placa (ex.: "300 Mbps Link" no design).
    /// </summary>
    public double VelocidadeContratadaMbps { get; set; }

    /// <summary>
    /// false até o app ligar o "Iniciar com o Windows" pela 1ª vez (padrão do pedido: iniciar com
    /// o sistema). Depois fica true e a escolha é só do usuário — desligar NÃO é desfeito.
    /// </summary>
    public bool InicioAutomaticoJaConfigurado { get; set; }

    // ---------------------------------------------------------------- v0.8.0 (Onda 2, itens adiados)

    /// <summary>Intervalos de atualização oferecidos (segundos). Fora desta lista, a leitura volta a 1 s.</summary>
    public static readonly double[] IntervalosPermitidos = [0.5, 1, 2, 5];

    /// <summary>
    /// De quanto em quanto tempo o widget mede e redesenha (segundos). Padrão 1 s (o de sempre).
    /// O gráfico do painel continua cobrindo 60 SEGUNDOS: com 0,5 s ele guarda 120 pontos, com 5 s, 12.
    /// </summary>
    public double IntervaloSegundos { get; set; } = 1.0;

    /// <summary>Tamanho do texto no widget: "pequeno", "normal" (padrão, o de sempre) ou "grande".</summary>
    public string TamanhoTexto { get; set; } = "normal";

    /// <summary>Fonte do widget: "segoe" (Segoe UI Variable, padrão), "segoe-classica" (Segoe UI) ou "cascadia" (Cascadia Mono).</summary>
    public string Fonte { get; set; } = "segoe";

    /// <summary>Ordem das linhas: false = envio (↑) em cima, como sempre foi; true = download (↓) em cima.</summary>
    public bool DownloadEmCima { get; set; }

    /// <summary>
    /// Posição escolhida com Ctrl + arrastar, por barra de tarefas. Chave "principal" ou
    /// "monitor:X,Y" (canto do monitor da barra secundária); valor = distância, em px LÓGICOS,
    /// da borda esquerda da barra até a borda esquerda do widget. Sem chave = posição automática
    /// (encostado à esquerda do '^' da bandeja).
    /// </summary>
    public Dictionary<string, int> PosicoesManuais { get; set; } = [];

    /// <summary>Mostrar o widget também nas barras dos outros monitores (padrão: só na principal).</summary>
    public bool MostrarEmTodasAsBarras { get; set; }

    // ---------------------------------------------------------------- v1.0.0 (Onda 5)

    /// <summary>
    /// Franquia mensal da internet em GB (decimal, como a operadora conta: 1 GB = 10⁹ bytes).
    /// 0 = sem franquia (padrão) → nenhum aviso. Com valor, avisa UMA vez aos 80 % e UMA vez aos
    /// 100 % do mês (envio + download, que é o que a operadora soma).
    /// </summary>
    public double FranquiaMensalGb { get; set; }

    /// <summary>
    /// Números mais fortes quanto mais perto da velocidade do plano (ou do link, sem plano).
    /// Padrão DESLIGADO: muda o visual de sempre; é para quem quer ler "está cheio?" de relance.
    /// </summary>
    public bool CorPorIntensidade { get; set; }

    // ---------------------------------------------------------------- v1.1.0 (Onda 6)

    /// <summary>
    /// Medir a latência (ping ao roteador e a 1.1.1.1 a cada 5 s). Padrão DESLIGADO: é o único
    /// recurso que manda pacotes para fora do PC, então só com o consentimento do usuário.
    /// </summary>
    public bool MedirLatencia { get; set; }

    /// <summary>Janela do gráfico do painel lembrada entre aberturas: "1min" (padrão), "5min" ou "1h".</summary>
    public string JanelaGrafico { get; set; } = "1min";

    // ---------------------------------------------------------------- v1.2.0 (Onda 7)

    /// <summary>
    /// Modo de exibição na barra: "duas-linhas" (padrão, o de sempre), "uma-linha" (↑ e ↓ lado a
    /// lado, sempre) ou "so-download" (só a pílula ↓).
    /// </summary>
    public string ModoExibicao { get; set; } = "duas-linhas";

    /// <summary>Trechos de nome/descrição de interfaces a SEMPRE contabilizar (raro).</summary>
    public List<string> InterfacesIncluir { get; set; } = [];

    /// <summary>Trechos de nome/descrição de interfaces a NUNCA contabilizar.</summary>
    public List<string> InterfacesExcluir { get; set; } = [];

    /// <summary>Cópia profunda (o estúdio edita uma cópia e só aplica no "Salvar").</summary>
    public Configuracoes Clonar()
    {
        var copia = (Configuracoes)MemberwiseClone();
        copia.InterfacesIncluir = [.. InterfacesIncluir];
        copia.InterfacesExcluir = [.. InterfacesExcluir];
        copia.PosicoesManuais = new Dictionary<string, int>(PosicoesManuais);
        return copia;
    }

    /// <summary>Corrige valores fora da faixa vindos de um arquivo editado à mão.</summary>
    public void Normalizar()
    {
        Unidade = Unidade is "bits" ? "bits" : "bytes";
        Suavizacao = double.IsFinite(Suavizacao) ? Math.Clamp(Suavizacao, 0.0, 0.9) : 0.5;
        VelocidadeContratadaMbps = double.IsFinite(VelocidadeContratadaMbps) ? Math.Clamp(VelocidadeContratadaMbps, 0, 100_000) : 0;
        Paleta = string.IsNullOrWhiteSpace(Paleta) ? "classico" : Paleta.Trim().ToLowerInvariant();
        InterfacesIncluir ??= [];
        InterfacesExcluir ??= [];
        CorUploadPersonalizada ??= "#4CC2FF";
        CorDownloadPersonalizada ??= "#FF6B6B";

        // v0.8.0: só valores conhecidos — um arquivo editado à mão não pode quebrar o widget
        IntervaloSegundos = IntervalosPermitidos.Contains(IntervaloSegundos) ? IntervaloSegundos : 1.0;
        TamanhoTexto = TamanhoTexto is "pequeno" or "normal" or "grande" ? TamanhoTexto : "normal";
        Fonte = Fonte is "segoe" or "segoe-classica" or "cascadia" ? Fonte : "segoe";
        PosicoesManuais ??= [];
        // v1.0.0: franquia entre 0 (desligada) e 100 TB; NaN/negativo de arquivo editado à mão → 0
        FranquiaMensalGb = double.IsFinite(FranquiaMensalGb) ? Math.Clamp(FranquiaMensalGb, 0, 100_000) : 0;
        // v1.1.0: janela desconhecida (arquivo editado à mão) volta ao padrão de 1 minuto
        JanelaGrafico = JanelaGrafico is "1min" or "5min" or "1h" ? JanelaGrafico : "1min";
        // v1.2.0: modo desconhecido volta às duas linhas de sempre
        ModoExibicao = ModoExibicao is "duas-linhas" or "uma-linha" or "so-download" ? ModoExibicao : "duas-linhas";
        // Distância negativa ou absurda (> 20.000 px lógicos) é lixo: descarta e volta ao automático
        foreach (string chave in PosicoesManuais.Where(p => p.Value is < 0 or > 20_000).Select(p => p.Key).ToList())
        {
            PosicoesManuais.Remove(chave);
        }
    }
}
