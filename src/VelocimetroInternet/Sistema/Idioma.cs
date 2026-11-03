using System.Globalization;
using VelocimetroInternet.Recursos;

namespace VelocimetroInternet.Sistema;

/// <summary>
/// v1.9.0 (acesso amplo): idioma da interface. Dois idiomas — português do Brasil (o neutro, de
/// sempre) e inglês. A escolha vem de <c>Configuracoes.Idioma</c>: "auto" (padrão) segue o idioma
/// de exibição do Windows (qualquer português → pt-BR; o resto → inglês), "pt-BR" e "en" forçam.
///
/// O que NÃO muda de idioma, por decisão do plano (03-PLANO-DE-ONDAS, "Inglês"): o relatório PDF e
/// o CSV (vão para a operadora brasileira) e o log (quem lê é o dono). Esses pontos passam pt-BR
/// explicitamente aos formatadores.
///
/// Estático e sem WPF: testável em xUnit comum. Os testes NUNCA chamam <see cref="Aplicar"/> (xunit
/// roda as classes em paralelo e a cultura é global) — usam <see cref="Resolver"/> e passam a
/// cultura aos formatadores.
/// </summary>
public static class Idioma
{
    public const string Automatico = "auto";
    public const string Portugues = "pt-BR";
    public const string Ingles = "en";

    /// <summary>Cultura dos textos e dos números da interface. Padrão pt-BR (o comportamento de sempre).</summary>
    public static CultureInfo Cultura { get; private set; } = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>"auto", "pt-BR" ou "en" — qualquer outra coisa (arquivo editado à mão) vira "auto".</summary>
    public static string Normalizar(string? escolha) => escolha?.Trim() switch
    {
        Portugues => Portugues,
        Ingles => Ingles,
        _ => Automatico
    };

    /// <summary>
    /// Cultura para a escolha do usuário. Em "auto", decide pelo idioma de EXIBIÇÃO do Windows
    /// (<paramref name="culturaDoWindows"/>; null = a da interface atual): "pt", "pt-BR", "pt-PT"…
    /// → pt-BR; qualquer outra → en-US. Só existem textos nesses dois idiomas, então um alemão vê
    /// inglês (melhor que português).
    /// </summary>
    public static CultureInfo Resolver(string? escolha, CultureInfo? culturaDoWindows = null)
    {
        switch (Normalizar(escolha))
        {
            case Portugues:
                return CultureInfo.GetCultureInfo("pt-BR");
            case Ingles:
                return CultureInfo.GetCultureInfo("en-US");
            default:
                CultureInfo windows = culturaDoWindows ?? CultureInfo.CurrentUICulture;
                return windows.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase)
                    ? CultureInfo.GetCultureInfo("pt-BR")
                    : CultureInfo.GetCultureInfo("en-US");
        }
    }

    /// <summary>
    /// Aplica a escolha ao app inteiro: textos (<see cref="Textos.Culture"/>), a cultura de UI padrão
    /// das threads novas e a da thread atual (a da UI — chamar na thread do WPF). Vale para as
    /// janelas criadas a partir daqui; as já abertas continuam como estão (o estúdio avisa isso).
    /// </summary>
    public static void Aplicar(string? escolha)
    {
        CultureInfo cultura = Resolver(escolha);
        Cultura = cultura;
        Textos.Culture = cultura;
        CultureInfo.DefaultThreadCurrentUICulture = cultura;
        CultureInfo.CurrentUICulture = cultura;
    }

    /// <summary>"en" quando a cultura em uso é inglês (atalhos de formatação que diferem nos dois idiomas).</summary>
    public static bool EmIngles => Cultura.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase);
}
