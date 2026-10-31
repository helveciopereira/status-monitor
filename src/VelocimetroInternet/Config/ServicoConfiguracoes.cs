using System.IO;
using System.Text.Json;

namespace VelocimetroInternet.Config;

/// <summary>
/// Lê e grava o settings.json.
/// - Leitura TOLERANTE: arquivo ausente → padrões; arquivo corrompido → renomeia para .bak e recria
///   (o app nunca deixa de abrir por causa de configuração).
/// - Gravação ATÔMICA: escreve em .tmp e troca (File.Move com overwrite) — uma queda de energia no
///   meio da gravação não deixa um JSON pela metade.
/// </summary>
public sealed class ServicoConfiguracoes
{
    private static readonly JsonSerializerOptions OpcoesJson = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _caminho;

    /// <summary>Configuração em uso. Substituída por inteiro em <see cref="Salvar"/>.</summary>
    public Configuracoes Atual { get; private set; } = new();

    /// <summary>Avisado depois de cada gravação (widget, painel e bandeja se reajustam na hora).</summary>
    public event Action<Configuracoes>? Alteradas;

    public ServicoConfiguracoes(string? caminho = null)
    {
        _caminho = caminho ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VelocimetroInternet",
            "settings.json");
    }

    public string Caminho => _caminho;

    /// <summary>Carrega do disco. Devolve uma mensagem para o log quando precisou recuperar algo.</summary>
    public string? Carregar()
    {
        string? aviso = null;
        try
        {
            if (File.Exists(_caminho))
            {
                string json = File.ReadAllText(_caminho);
                Atual = JsonSerializer.Deserialize<Configuracoes>(json, OpcoesJson) ?? new Configuracoes();
            }
            else
            {
                Atual = new Configuracoes();
                aviso = "settings.json ausente — criado com os padrões.";
                GravarNoDisco(Atual);
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Arquivo corrompido/editado errado: preserva a cópia para o usuário e recomeça
            string bak = _caminho + ".bak";
            try
            {
                File.Copy(_caminho, bak, overwrite: true);
            }
            catch
            {
                // Sem permissão para a cópia: segue com os padrões mesmo assim
            }

            Atual = new Configuracoes();
            GravarNoDisco(Atual);
            aviso = $"settings.json inválido ({ex.Message}) — cópia em {bak}, recriado com padrões.";
        }

        Atual.Normalizar();
        return aviso;
    }

    /// <summary>Grava e avisa os interessados.</summary>
    public void Salvar(Configuracoes nova)
    {
        nova.Normalizar();
        Atual = nova;
        GravarNoDisco(nova);
        Alteradas?.Invoke(nova);
    }

    /// <summary>Atalho para mudar um campo só: <c>servico.Alterar(c =&gt; c.Unidade = "bits")</c>.</summary>
    public void Alterar(Action<Configuracoes> mudanca)
    {
        Configuracoes copia = Atual.Clonar();
        mudanca(copia);
        Salvar(copia);
    }

    private void GravarNoDisco(Configuracoes cfg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
            string tmp = _caminho + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, OpcoesJson));
            File.Move(tmp, _caminho, overwrite: true);
        }
        catch
        {
            // Disco cheio/sem permissão: o app continua com a configuração em memória (silencioso)
        }
    }
}
