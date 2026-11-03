using System.Globalization;
using System.Resources;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Recursos;

/// <summary>
/// v1.9.0 (acesso amplo): parte escrita à mão da classe de textos. As propriedades (uma por chave)
/// estão em <c>Textos.Designer.cs</c>, gerado por <c>tools\gerar-textos.ps1</c> a partir de
/// <c>Textos.resx</c> (pt-BR, idioma neutro) — o inglês vem de <c>Textos.en.resx</c>, compilado
/// pelo SDK no satélite <c>en\StatusMonitor.resources.dll</c>.
///
/// Regra de cultura: <see cref="Culture"/> quando definida; senão <see cref="Idioma.Cultura"/>
/// (pt-BR por padrão). NUNCA a cultura da thread — os testes rodam em paralelo e a interface
/// precisa ser previsível independentemente do Windows da máquina.
/// </summary>
public static partial class Textos
{
    /// <summary>Nome do recurso embutido: RootNamespace + pasta + nome do .resx.</summary>
    private static readonly ResourceManager Rm = new("VelocimetroInternet.Recursos.Textos", typeof(Textos).Assembly);

    /// <summary>Acesso direto (testes: enumerar as chaves de cada idioma).</summary>
    public static ResourceManager ResourceManager => Rm;

    /// <summary>Cultura forçada (null = segue <see cref="Idioma.Cultura"/>).</summary>
    public static CultureInfo? Culture { get; set; }

    /// <summary>Cultura em uso nos textos agora.</summary>
    public static CultureInfo CulturaAtual => Culture ?? Idioma.Cultura;

    /// <summary>Texto da chave na cultura atual; a própria chave se faltar (nunca null, nunca exceção na tela).</summary>
    private static string Obter(string chave) => Rm.GetString(chave, CulturaAtual) ?? chave;

    /// <summary>Texto da chave numa cultura específica (testes e PDF/CSV, que ficam em pt-BR).</summary>
    public static string Obter(string chave, CultureInfo cultura) => Rm.GetString(chave, cultura) ?? chave;

    /// <summary>
    /// <c>string.Format</c> com a cultura dos textos — os números dentro do modelo ("{0:0.##}")
    /// saem com vírgula em português e ponto em inglês, junto com as palavras.
    /// </summary>
    public static string Formatar(string modelo, params object?[] args) =>
        string.Format(CulturaAtual, modelo, args);
}
