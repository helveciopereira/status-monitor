using VelocimetroInternet.Sistema;
using Velopack;

namespace VelocimetroInternet;

/// <summary>
/// Ponto de entrada próprio (v0.4.0), no lugar do <c>Main</c> que o WPF gera para o App.xaml.
/// </summary>
/// <remarks>
/// Por que existe: o instalador/atualizador (Velopack) executa o próprio .exe com argumentos
/// especiais na instalação, na atualização e na desinstalação. O <see cref="VelopackApp"/>
/// precisa ser a PRIMEIRA coisa a rodar: nesses casos ele executa o gancho pedido e encerra o
/// processo antes de existir qualquer janela. Num início normal ele volta em microssegundos e
/// o WPF segue como antes. O .csproj aponta este tipo em <c>StartupObject</c>.
/// </remarks>
public static class Program
{
    /// <summary>Inicia o Velopack e, em seguida, o aplicativo WPF.</summary>
    /// <param name="args">Argumentos da linha de comando (o Velopack lê os dele; o App lê os de diagnóstico).</param>
    [STAThread] // o WPF exige thread de apartamento único na thread da interface
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            // Desinstalação: tira a entrada "Iniciar com o Windows" — senão, a cada logon, o
            // Windows tentaria abrir um .exe que não existe mais. Só apaga se a entrada aponta
            // para ESTA cópia (uma cópia de teste desinstalada não derruba a do usuário).
            .OnBeforeUninstallFastCallback(_ => InicioComWindows.DesligarSeApontaPara(Environment.ProcessPath))
            // Reaberto pelo atualizador: guarda a versão nova só para o log (nada de aviso na
            // tela — o pedido do dono é atualizar SEM atrapalhar).
            .OnRestarted(versao => App.AtualizadoParaVersao = versao.ToString())
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
