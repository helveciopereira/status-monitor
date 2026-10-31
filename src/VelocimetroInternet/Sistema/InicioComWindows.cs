using Microsoft.Win32;

namespace VelocimetroInternet.Sistema;

/// <summary>
/// Liga/desliga "Iniciar com o Windows" pela chave Run do USUÁRIO (sem administrador).
/// Referência: 02-ARQUITETURA.md §6.
///
/// Pegadinha: o usuário pode desligar o app em Configurações › Apps › Inicialização (ou no
/// Gerenciador de Tarefas). Isso NÃO apaga a chave Run — grava um "desaprovado" em
/// Explorer\StartupApproved\Run. Por isso:
/// - "está ligado?" = valor em Run E não desaprovado;
/// - ligar = grava Run E marca como aprovado (senão o Windows continuaria ignorando).
/// </summary>
public sealed class InicioComWindows
{
    public const string NomeValor = "VelocimetroInternet";

    private readonly string _subchaveRun;
    private readonly string _subchaveAprovado;

    /// <param name="subchaveRun">Sobrescrito só em testes (para não mexer na inicialização real).</param>
    public InicioComWindows(
        string subchaveRun = @"Software\Microsoft\Windows\CurrentVersion\Run",
        string subchaveAprovado = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run")
    {
        _subchaveRun = subchaveRun;
        _subchaveAprovado = subchaveAprovado;
    }

    /// <summary>Linha de comando registrada: caminho entre aspas + marcador de início automático.</summary>
    public static string MontarComando(string caminhoExe) => $"\"{caminhoExe}\" --autostart";

    public bool EstaLigado()
    {
        using RegistryKey? run = Registry.CurrentUser.OpenSubKey(_subchaveRun);
        if (run?.GetValue(NomeValor) is not string comando || string.IsNullOrWhiteSpace(comando))
        {
            return false;
        }

        // StartupApproved: 12 bytes; 1º byte PAR (0x02/0x06) = aprovado, ÍMPAR (0x03/0x07) = desligado
        using RegistryKey? aprovado = Registry.CurrentUser.OpenSubKey(_subchaveAprovado);
        if (aprovado?.GetValue(NomeValor) is byte[] { Length: > 0 } estado)
        {
            return (estado[0] & 0x01) == 0;
        }

        return true; // sem registro de aprovação = aprovado (comportamento do Windows)
    }

    /// <summary>
    /// Devolve true se a chave Run aponta para OUTRO executável (ex.: o app foi movido de pasta).
    /// Nesse caso vale religar para corrigir o caminho.
    /// </summary>
    public bool ApontaParaOutroCaminho(string caminhoExeAtual)
    {
        using RegistryKey? run = Registry.CurrentUser.OpenSubKey(_subchaveRun);
        return run?.GetValue(NomeValor) is string comando
               && !string.Equals(comando, MontarComando(caminhoExeAtual), StringComparison.OrdinalIgnoreCase);
    }

    public void Definir(bool ligar, string caminhoExe)
    {
        using RegistryKey run = Registry.CurrentUser.CreateSubKey(_subchaveRun, writable: true);
        using RegistryKey aprovado = Registry.CurrentUser.CreateSubKey(_subchaveAprovado, writable: true);

        if (ligar)
        {
            run.SetValue(NomeValor, MontarComando(caminhoExe), RegistryValueKind.String);
            // 0x02 + 11 zeros = "aprovado" (o mesmo que o Gerenciador de Tarefas grava ao habilitar)
            aprovado.SetValue(NomeValor, new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
        }
        else
        {
            run.DeleteValue(NomeValor, throwOnMissingValue: false);
            aprovado.DeleteValue(NomeValor, throwOnMissingValue: false);
        }
    }
}
