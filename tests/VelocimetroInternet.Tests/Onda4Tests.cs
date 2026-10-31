using System.Net;
using System.Net.Http;
using Microsoft.Win32;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Tests;

/// <summary>
/// Onda 4 (v0.4.0): regras da atualização automática que dá para provar sem rede nem
/// instalação — o ciclo completo (instalar → publicar → atualizar sozinho) é provado no ensaio
/// local descrito no walkthrough.
/// </summary>
public sealed class Onda4Tests
{
    // ---------------------------------------------------------------- momento tranquilo

    [Fact]
    public void MomentoTranquilo_SoQuandoNadaEstaAcontecendo()
    {
        Assert.True(RegraMomentoTranquilo.Avaliar(false, false, false, false, false, false));
    }

    [Theory]
    [InlineData(true, false, false, false, false, false)]  // painel aberto
    [InlineData(false, true, false, false, false, false)]  // estúdio aberto (edição não aplicada)
    [InlineData(false, false, true, false, false, false)]  // menu aberto
    [InlineData(false, false, false, true, false, false)]  // mouse sobre o widget
    [InlineData(false, false, false, false, true, false)]  // tela cheia (jogo/vídeo)
    [InlineData(false, false, false, false, false, true)]  // medição pausada (reiniciar despausaria)
    public void MomentoTranquilo_QualquerAtividadeAdia(bool painel, bool estudio, bool menu, bool mouse, bool cheia, bool pausado)
    {
        Assert.False(RegraMomentoTranquilo.Avaliar(painel, estudio, menu, mouse, cheia, pausado));
    }

    // ---------------------------------------------------------------- classificação de falhas

    [Fact]
    public void Falhas_ViramFrasesLegiveis()
    {
        Assert.Equal("nenhuma versão publicada ainda",
            ServicoAtualizacao.Classificar(new HttpRequestException("x", null, HttpStatusCode.NotFound)));
        Assert.Equal("o GitHub limitou as consultas",
            ServicoAtualizacao.Classificar(new HttpRequestException("x", null, HttpStatusCode.TooManyRequests)));
        Assert.Equal("sem internet ou GitHub indisponível",
            ServicoAtualizacao.Classificar(new HttpRequestException("sem rede")));
        Assert.Equal("tempo esgotado", ServicoAtualizacao.Classificar(new TaskCanceledException()));
        Assert.StartsWith("falhou (", ServicoAtualizacao.Classificar(new InvalidOperationException()));
    }

    [Fact]
    public void Feed_EhOEnderecoDiretoDaUltimaRelease()
    {
        // Endereço direto (sem a API do GitHub e seu limite de 60 consultas/h por IP)
        Assert.Equal("https://github.com/helveciopereira/status-monitor/releases/latest/download/",
            ServicoAtualizacao.EnderecoFeed);
    }

    // ---------------------------------------------------------------- entrada "Iniciar com o Windows"

    /// <summary>Executa o teste numa subchave descartável do HKCU (nunca na inicialização real).</summary>
    private static void ComRegistroDeTeste(Action<InicioComWindows> teste)
    {
        string raiz = @"Software\VelocimetroInternetTestes_" + Guid.NewGuid().ToString("N");
        try
        {
            teste(new InicioComWindows(raiz + @"\Run", raiz + @"\Aprovado"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(raiz, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void Inicio_CaminhoRegistradoTiraAspasEArgumentos() => ComRegistroDeTeste(inicio =>
    {
        Assert.Null(inicio.CaminhoRegistrado());
        inicio.Definir(true, @"C:\Pasta Com Espaço\StatusMonitor.exe");
        Assert.Equal(@"C:\Pasta Com Espaço\StatusMonitor.exe", inicio.CaminhoRegistrado());
    });

    [Fact]
    public void Inicio_CopiaInstaladaSempreAssumeAEntrada() => ComRegistroDeTeste(inicio =>
    {
        inicio.Definir(true, @"C:\Projetos\dist\StatusMonitor.exe");
        Assert.True(inicio.DeveReapontar(@"C:\Users\x\AppData\Local\VelocimetroInternetApp\current\StatusMonitor.exe",
            estaInstalado: true, existe: _ => true));
    });

    [Fact]
    public void Inicio_CopiaSoltaNaoRoubaEntradaDeExeQueExiste() => ComRegistroDeTeste(inicio =>
    {
        inicio.Definir(true, @"C:\Instalado\current\StatusMonitor.exe");
        // Teste rodando de bin\: o .exe registrado existe → não mexe
        Assert.False(inicio.DeveReapontar(@"C:\Projetos\bin\StatusMonitor.exe", false, _ => true));
        // O .exe registrado sumiu (pasta movida) → corrige para o atual
        Assert.True(inicio.DeveReapontar(@"C:\Projetos\bin\StatusMonitor.exe", false, _ => false));
    });

    [Fact]
    public void Inicio_DesligadoOuJaCertoNaoReaponta() => ComRegistroDeTeste(inicio =>
    {
        // Desligado pelo usuário: nunca religa sozinho, nem a cópia instalada
        Assert.False(inicio.DeveReapontar(@"C:\I\current\V.exe", estaInstalado: true, existe: _ => false));
        inicio.Definir(true, @"C:\I\current\V.exe");
        Assert.False(inicio.DeveReapontar(@"C:\I\current\V.exe", estaInstalado: true, existe: _ => true));
    });
}
