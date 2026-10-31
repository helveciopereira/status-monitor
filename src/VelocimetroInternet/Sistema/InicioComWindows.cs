using System.IO;
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

    /// <summary>
    /// Caminho do .exe gravado na entrada Run (sem aspas nem argumentos), ou null se não há entrada.
    /// </summary>
    public string? CaminhoRegistrado()
    {
        using RegistryKey? run = Registry.CurrentUser.OpenSubKey(_subchaveRun);
        if (run?.GetValue(NomeValor) is not string comando || string.IsNullOrWhiteSpace(comando))
        {
            return null;
        }

        // Formato gravado por MontarComando: "C:\...\App.exe" --autostart
        string c = comando.Trim();
        if (c.StartsWith('"'))
        {
            int fim = c.IndexOf('"', 1);
            return fim > 1 ? c[1..fim] : null;
        }

        int espaco = c.IndexOf(' ');
        return espaco > 0 ? c[..espaco] : c;
    }

    /// <summary>
    /// Decide se a entrada Run deve ser reapontada para o .exe que está rodando (v0.4.0).
    /// - Cópia INSTALADA (Velopack): sempre — ela é a "oficial", e a pasta <c>current\</c> é
    ///   estável entre atualizações. É assim que a instalação nova assume o lugar do antigo
    ///   <c>dist\StatusMonitor.exe</c>.
    /// - Cópia solta (bin\, dist\): só se o .exe registrado NÃO existe mais (foi movido).
    ///   Antes da v0.4.0 qualquer execução de teste a partir de bin\ roubava a entrada do
    ///   app instalado.
    /// </summary>
    /// <param name="caminhoExeAtual">Caminho do processo atual.</param>
    /// <param name="estaInstalado">true se esta cópia foi instalada pelo Setup (Velopack).</param>
    /// <param name="existe">Teste de existência de arquivo (injetável para testes).</param>
    public bool DeveReapontar(string caminhoExeAtual, bool estaInstalado, Func<string, bool>? existe = null)
    {
        if (!EstaLigado() || !ApontaParaOutroCaminho(caminhoExeAtual))
        {
            return false; // desligado (respeita o usuário) ou já aponta para cá
        }

        if (estaInstalado)
        {
            return true;
        }

        string? registrado = CaminhoRegistrado();
        return registrado is null || !(existe ?? File.Exists)(registrado);
    }

    /// <summary>
    /// Usado na DESINSTALAÇÃO (gancho do Velopack): apaga a entrada só se ela aponta para o .exe
    /// que está sendo desinstalado. Nunca lança — o desinstalador não pode falhar por isso.
    /// </summary>
    public static void DesligarSeApontaPara(string? caminhoExe)
    {
        try
        {
            var inicio = new InicioComWindows();
            if (caminhoExe is not null && inicio.CaminhoRegistrado() is { } reg
                && string.Equals(Path.GetFullPath(reg), Path.GetFullPath(caminhoExe), StringComparison.OrdinalIgnoreCase))
            {
                inicio.Definir(false, caminhoExe);
            }
        }
        catch
        {
            // Registro inacessível: segue a desinstalação; no pior caso sobra uma entrada órfã
        }
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
