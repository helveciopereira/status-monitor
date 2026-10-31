using System.Windows.Threading;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// v0.8.0 (Onda 2, item adiado "vários monitores"): mantém um widget em CADA barra secundária
/// (Shell_SecondaryTrayWnd — a barra dos outros monitores, quando o Windows está configurado para
/// "mostrar a barra em todos os monitores").
///
/// As barras secundárias aparecem e somem sozinhas (monitor ligado/desligado, Explorer reiniciado,
/// configuração mudada), e cada recriação troca o hwnd. Em vez de depender de um evento que pode
/// não chegar, um relógio de 3 s compara a lista atual com a anterior: barra nova ganha widget,
/// barra que sumiu tem o widget descartado. O widget da barra PRINCIPAL não passa por aqui.
/// </summary>
public sealed class GerenciadorBarrasSecundarias : IDisposable
{
    private readonly Func<TaskbarWidget> _criarWidget;
    private readonly Action<TaskbarWidget, TaskbarHost> _configurar;
    private readonly Action<string>? _logger;
    private readonly Dictionary<nint, (TaskbarWidget Widget, TaskbarHost Host)> _ativos = [];
    private readonly DispatcherTimer _relogio;
    private bool _ativo;
    private bool _sincronizando;

    /// <param name="criarWidget">Cria o widget já com cores, unidade e aparência atuais.</param>
    /// <param name="configurar">Liga eventos (clique, menu, arrasto) e copia as opções para o host novo — antes de ele aparecer.</param>
    public GerenciadorBarrasSecundarias(
        Func<TaskbarWidget> criarWidget,
        Action<TaskbarWidget, TaskbarHost> configurar,
        Action<string>? logger = null)
    {
        _criarWidget = criarWidget;
        _configurar = configurar;
        _logger = logger;
        _relogio = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
        _relogio.Tick += async (_, _) => await SincronizarAsync();
    }

    /// <summary>Widgets das barras secundárias existentes agora (para o loop de medição e as configurações).</summary>
    public IReadOnlyCollection<(TaskbarWidget Widget, TaskbarHost Host)> Pares => _ativos.Values;

    /// <summary>"Mostrar em todas as barras" ligado/desligado (estúdio). Desligar remove os widgets na hora.</summary>
    public bool Ativo
    {
        get => _ativo;
        set
        {
            if (_ativo == value)
            {
                return;
            }

            _ativo = value;
            if (value)
            {
                _relogio.Start();
                _ = SincronizarAsync();
            }
            else
            {
                _relogio.Stop();
                RemoverTodos();
            }
        }
    }

    /// <summary>Hwnds das barras secundárias abertas agora (FindWindowEx enumera as janelas de topo da classe).</summary>
    public static List<nint> ListarBarrasSecundarias()
    {
        var lista = new List<nint>();
        nint h = nint.Zero;
        while ((h = NativeMethods.FindWindowExW(nint.Zero, h, "Shell_SecondaryTrayWnd", null)) != nint.Zero)
        {
            if (NativeMethods.IsWindowVisible(h))
            {
                lista.Add(h);
            }
        }

        return lista;
    }

    /// <summary>Acerta a lista de widgets com a lista de barras. Chamado pelo relógio e ao ligar a opção.</summary>
    public async Task SincronizarAsync()
    {
        if (!_ativo || _sincronizando)
        {
            return;
        }

        _sincronizando = true;
        try
        {
            List<nint> barras = ListarBarrasSecundarias();

            foreach (nint velha in _ativos.Keys.Where(k => !barras.Contains(k)).ToList())
            {
                Remover(velha, "a barra sumiu");
            }

            foreach (nint nova in barras.Where(b => !_ativos.ContainsKey(b)))
            {
                TaskbarWidget widget = _criarWidget();
                var host = new TaskbarHost(widget, ModoEncaixeBarra.ModoB_TopmostSobreBarra, _logger, barraSecundaria: nova);
                _configurar(widget, host);
                _ativos[nova] = (widget, host);
                _logger?.Invoke($"Barra secundária 0x{nova:X} ({host.ChavePosicao}): widget criado.");
                await host.InicializarAsync();
                if (!_ativo)
                {
                    break; // desligaram no meio da criação: o RemoverTodos já cuidou do resto
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"Erro ao sincronizar barras secundárias: {ex.Message}");
        }
        finally
        {
            _sincronizando = false;
        }
    }

    private void Remover(nint barra, string motivo)
    {
        if (!_ativos.Remove(barra, out (TaskbarWidget Widget, TaskbarHost Host) par))
        {
            return;
        }

        par.Host.Dispose();
        par.Widget.Close();
        _logger?.Invoke($"Barra secundária 0x{barra:X}: widget removido ({motivo}).");
    }

    private void RemoverTodos()
    {
        foreach (nint barra in _ativos.Keys.ToList())
        {
            Remover(barra, "opção desligada");
        }
    }

    public void Dispose()
    {
        _relogio.Stop();
        RemoverTodos();
    }
}
