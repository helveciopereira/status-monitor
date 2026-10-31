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
    }
}
