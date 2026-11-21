using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Medicao;

/// <summary>De que peça é a temperatura. Decide o rótulo curto da barra e o limite do alerta.</summary>
public enum TipoSensorTemperatura
{
    /// <summary>Linha sem sensor resolvido (rótulo "TEMP", valor "—").</summary>
    Nenhum = 0,
    Cpu = 1,
    Gpu = 2,
    Ssd = 3,
    Hd = 4
}

/// <summary>
/// Um sensor de temperatura deste computador. <paramref name="Id"/> é o que vai para o settings.json
/// ("cpu", "gpu:NOME", "ssd:MODELO", "hd:MODELO") — pelo NOME da peça, porque o número do disco e o
/// identificador da placa de vídeo mudam entre reinícios. <paramref name="Celsius"/> null = sem leitura.
/// </summary>
public readonly record struct SensorTemperatura(string Id, string Nome, double? Celsius)
{
    public TipoSensorTemperatura Tipo => Temperaturas.TipoDe(Id);
}

/// <summary>O que as duas linhas da coluna de temperatura mostram agora. null = linha desligada ("nenhuma").</summary>
public readonly record struct LinhasTemperatura(SensorTemperatura? Cima, SensorTemperatura? Baixo);

/// <summary>
/// v2.0.1 — a parte PURA (testável) das temperaturas: códigos gravados no settings.json, escolha do
/// sensor de cada linha, conversões de unidade, texto e alerta.
/// </summary>
public static class Temperaturas
{
    /// <summary>Linha "automática": o app escolhe (CPU, depois GPU, depois disco) o que este PC tiver.</summary>
    public const string Automatica = "auto";

    /// <summary>Linha desligada: a pílula daquela linha some.</summary>
    public const string Nenhuma = "nenhuma";

    public const string IdCpu = "cpu";
    public const string PrefixoGpu = "gpu:";
    public const string PrefixoSsd = "ssd:";
    public const string PrefixoHd = "hd:";

    /// <summary>Tipo do sensor pelo prefixo do Id — assim o rótulo existe mesmo se a peça sumiu (HD externo desligado).</summary>
    public static TipoSensorTemperatura TipoDe(string? id) => id switch
    {
        IdCpu => TipoSensorTemperatura.Cpu,
        not null when id.StartsWith(PrefixoGpu, StringComparison.Ordinal) => TipoSensorTemperatura.Gpu,
        not null when id.StartsWith(PrefixoSsd, StringComparison.Ordinal) => TipoSensorTemperatura.Ssd,
        not null when id.StartsWith(PrefixoHd, StringComparison.Ordinal) => TipoSensorTemperatura.Hd,
        _ => TipoSensorTemperatura.Nenhum
    };

    /// <summary>Rótulo curto da pílula (igual em português e inglês, como "CPU" e "RAM").</summary>
    public static string Rotulo(TipoSensorTemperatura tipo) => tipo switch
    {
        TipoSensorTemperatura.Cpu => "CPU",
        TipoSensorTemperatura.Gpu => "GPU",
        TipoSensorTemperatura.Ssd => "SSD",
        TipoSensorTemperatura.Hd => "HD",
        _ => "TEMP"
    };

    /// <summary>Texto curto para o widget: "54°C"; sem leitura → "—". Sem casas decimais: cabe e não "pisca".</summary>
    public static string Formatar(double? celsius) =>
        celsius is { } c ? $"{Math.Round(c, MidpointRounding.AwayFromZero):0}°C" : "—";

    /// <summary>
    /// A partir de quantos °C o número fica vermelho, por peça. São faixas de "está quente demais para
    /// uso contínuo", abaixo do desligamento de proteção: CPU 90, GPU 85, SSD 70, HD 55.
    /// </summary>
    public static int LimiteAlerta(TipoSensorTemperatura tipo) => tipo switch
    {
        TipoSensorTemperatura.Cpu => 90,
        TipoSensorTemperatura.Gpu => 85,
        TipoSensorTemperatura.Ssd => 70,
        TipoSensorTemperatura.Hd => 55,
        _ => int.MaxValue
    };

    /// <summary>Em alerta? Compara o valor JÁ ARREDONDADO (o mesmo da tela), como o CPU/RAM ≥ 80 %.</summary>
    public static bool EmAlerta(TipoSensorTemperatura tipo, double? celsius) =>
        celsius is { } c && Math.Round(c, MidpointRounding.AwayFromZero) >= LimiteAlerta(tipo);

    /// <summary>Leitura plausível para uma peça de computador ligada? Fora disso é sensor ausente ou lixo.</summary>
    public static bool Plausivel(double celsius) => double.IsFinite(celsius) && celsius is > 0 and < 150;

    /// <summary>
    /// Zona térmica mais quente, em °C, a partir das leituras em Kelvin. Por que a MAIS QUENTE: os PCs
    /// costumam ter uma zona de enfeite parada num valor baixo (medido aqui: 20 °C fixos ao lado da
    /// zona real em 79 °C). Nenhuma leitura plausível → null.
    /// </summary>
    public static double? ZonaMaisQuente(IEnumerable<double> kelvin)
    {
        double? maior = null;
        foreach (double k in kelvin)
        {
            double c = k - 273.15;
            if (Plausivel(c) && (maior is null || c > maior))
            {
                maior = c;
            }
        }

        return maior;
    }

    /// <summary>
    /// Temperatura composta do registro SMART de um SSD NVMe (página de log 2): bytes 1–2, em Kelvin
    /// inteiro, little-endian. Registro curto ou valor absurdo → null.
    /// </summary>
    public static double? DeSmartNvme(ReadOnlySpan<byte> registro)
    {
        if (registro.Length < 3)
        {
            return null;
        }

        double c = (registro[1] | (registro[2] << 8)) - 273;
        return Plausivel(c) ? c : null;
    }

    /// <summary>
    /// Temperatura do descritor genérico do Windows (STORAGE_TEMPERATURE_DATA_DESCRIPTOR): quantidade
    /// de sensores no byte 12 e, a partir do byte 24, blocos de 16 bytes com a temperatura (°C, inteiro
    /// com sinal) nos bytes 2–3. Devolve a mais quente; sem sensores (o caso do NVMe medido aqui) → null.
    /// </summary>
    public static double? DeDescritorDeTemperatura(ReadOnlySpan<byte> descritor, int bytesDevolvidos)
    {
        const int Cabecalho = 24, Bloco = 16;
        if (bytesDevolvidos < Cabecalho || descritor.Length < bytesDevolvidos)
        {
            return null;
        }

        int quantidade = descritor[12] | (descritor[13] << 8);
        double? maior = null;
        for (int i = 0; i < quantidade && Cabecalho + ((i + 1) * Bloco) <= bytesDevolvidos; i++)
        {
            int inicio = Cabecalho + (i * Bloco) + 2;
            double c = (short)(descritor[inicio] | (descritor[inicio + 1] << 8));
            if (Plausivel(c) && (maior is null || c > maior))
            {
                maior = c;
            }
        }

        return maior;
    }

    /// <summary>
    /// Qual sensor cada linha mostra. Código "nenhuma" → null (pílula some). Id de um sensor → ele; se a
    /// peça não está no PC agora, devolve o Id sem leitura (rótulo certo e "—"). "auto" → o 1º sensor
    /// COM leitura que a outra linha não esteja usando, na ordem da lista (CPU, GPUs, discos); sem
    /// nenhum, um sensor vazio (rótulo "TEMP" e "—"), para a coluna não ficar com um buraco.
    /// </summary>
    public static LinhasTemperatura Resolver(string? linhaCima, string? linhaBaixo, IReadOnlyList<SensorTemperatura> sensores)
    {
        // As escolhas explícitas valem primeiro: a automática só pega o que sobrou
        SensorTemperatura? cima = Explicita(linhaCima, sensores);
        SensorTemperatura? baixo = Explicita(linhaBaixo, sensores);
        if (linhaCima == Automatica)
        {
            cima = PrimeiroLivre(sensores, baixo?.Id);
        }

        if (linhaBaixo == Automatica)
        {
            baixo = PrimeiroLivre(sensores, cima?.Id);
        }

        return new LinhasTemperatura(cima, baixo);
    }

    private static SensorTemperatura? Explicita(string? codigo, IReadOnlyList<SensorTemperatura> sensores)
    {
        if (codigo is null or Nenhuma or Automatica)
        {
            return null;
        }

        foreach (SensorTemperatura s in sensores)
        {
            if (s.Id == codigo)
            {
                return s;
            }
        }

        return new SensorTemperatura(codigo, "", null);
    }

    private static SensorTemperatura PrimeiroLivre(IReadOnlyList<SensorTemperatura> sensores, string? idOcupado)
    {
        foreach (SensorTemperatura s in sensores)
        {
            if (s.Celsius is not null && s.Id != idOcupado)
            {
                return s;
            }
        }

        return new SensorTemperatura("", "", null);
    }

    /// <summary>A coluna aparece? Só com a opção ligada E pelo menos uma das linhas diferente de "nenhuma".</summary>
    public static bool ColunaVisivel(bool mostrar, string? linhaCima, string? linhaBaixo) =>
        mostrar && !(linhaCima == Nenhuma && linhaBaixo == Nenhuma);

    /// <summary>Código de linha válido para gravar: "auto", "nenhuma" ou o Id de um sensor; o resto vira o padrão dado.</summary>
    public static string NormalizarLinha(string? codigo, string padrao) =>
        codigo is Automatica or Nenhuma || TipoDe(codigo) != TipoSensorTemperatura.Nenhum ? codigo! : padrao;
}

/// <summary>
/// v2.0.1 (pedido do dono: "medidor de temperatura… CPU, RAM, HD, SSD, GPU") — lê as temperaturas que
/// o Windows entrega SEM administrador e sem driver de kernel:
///
/// • CPU — zona térmica ACPI (contador de desempenho "Thermal Zone Information", via PDH nativo). É a
///   temperatura que a placa-mãe informa ao Windows; pode diferir alguns graus do sensor interno de
///   cada núcleo, que só um driver de kernel alcança (é o que HWiNFO/LibreHardwareMonitor instalam).
///   PCs de mesa às vezes não têm zona térmica: o sensor simplesmente não aparece na lista.
/// • GPU — o mesmo número do Gerenciador de Tarefas (D3DKMT, WDDM 2.4+). GPU integrada Intel
///   costuma não informar (0) e fica fora da lista.
/// • SSD NVMe — temperatura composta do registro SMART (consulta de propriedade, sem abrir o disco
///   para leitura). SSD/HD SATA — descritor genérico de temperatura do Windows, quando o driver o
///   preenche (não havia disco SATA para validar; sem leitura, o disco fica fora da lista).
/// • RAM — NÃO há caminho sem driver de kernel (o sensor fica no barramento SMBus do pente, e só
///   memórias DDR4/DDR5 com sensor têm). Por isso não é oferecida.
///
/// Custo: nada é carregado enquanto a opção está desligada (o PDH custa ~7 MB privados ao iniciar).
/// Em uso, só os sensores EXIBIDOS são lidos — ler a GPU dedicada de um notebook que ninguém pediu
/// poderia impedi-la de dormir. A lista completa só é levantada ao ligar e ao abrir o menu.
/// Seguro entre threads (o loop de medição lê; o menu lista).
/// </summary>
public sealed class MedidorTemperatura : IDisposable
{
    private readonly object _trava = new();

    // PDH: aberto na 1ª leitura da CPU e mantido (reabrir a cada leitura custaria ~100 ms)
    private nint _consultaPdh;
    private nint _contadorPdh;
    private bool _pdhIndisponivel;
    private bool _pdhEmKelvinInteiro;

    /// <summary>Última lista completa (ordem: CPU, GPUs, discos). Base da escolha "automática".</summary>
    private List<SensorTemperatura> _inventario = [];
    private bool _temInventario;
    private long _inventarioEmMs;

    /// <summary>Número do PhysicalDrive e barramento de cada disco listado (o Id é pelo modelo).</summary>
    private readonly Dictionary<string, (int Numero, bool Nvme)> _discos = [];

    /// <summary>Discos mudam devagar e a consulta é a única que toca o hardware: 1 leitura a cada 30 s.</summary>
    private readonly Dictionary<string, (long EmMs, double? Celsius)> _cacheDiscos = [];

    private const long ValidadeDiscoMs = 30_000;
    private const long RefazerInventarioMs = 60_000;
    private const int MaximoDiscos = 32;
    private const int BarramentoNvme = 17; // STORAGE_BUS_TYPE.BusTypeNvme

    /// <summary>Todos os sensores com leitura AGORA (para o menu de escolha). Refaz o inventário.</summary>
    public IReadOnlyList<SensorTemperatura> Listar()
    {
        lock (_trava)
        {
            var lista = new List<SensorTemperatura>();
            if (LerCpu() is { } cpu)
            {
                lista.Add(new SensorTemperatura(Temperaturas.IdCpu, "", cpu));
            }

            ListarGpus(lista);
            ListarDiscos(lista);
            _inventario = lista;
            _temInventario = true;
            _inventarioEmMs = Environment.TickCount64;
            return [.. lista];
        }
    }

    /// <summary>
    /// As duas linhas da barra, com leitura fresca só dos sensores exibidos. O inventário é levantado na
    /// 1ª vez e refeito (no máximo 1× por minuto) enquanto alguma linha estiver sem leitura — a peça
    /// pode ter sido ligada depois (HD externo) ou a GPU pode ter acordado.
    /// </summary>
    public LinhasTemperatura LerLinhas(string linhaCima, string linhaBaixo)
    {
        lock (_trava)
        {
            if (!_temInventario)
            {
                Listar();
            }

            LinhasTemperatura linhas = Temperaturas.Resolver(linhaCima, linhaBaixo, _inventario);
            linhas = new LinhasTemperatura(Atualizar(linhas.Cima), Atualizar(linhas.Baixo));

            bool faltando = linhas.Cima is { Celsius: null } || linhas.Baixo is { Celsius: null };
            if (faltando && Environment.TickCount64 - _inventarioEmMs >= RefazerInventarioMs)
            {
                Listar();
            }

            return linhas;
        }
    }

    /// <summary>Fecha a consulta do PDH e esquece o inventário (opção desligada). A próxima leitura reabre.</summary>
    public void Fechar()
    {
        lock (_trava)
        {
            if (_consultaPdh != nint.Zero)
            {
                NativeMethods.PdhCloseQuery(_consultaPdh);
                _consultaPdh = nint.Zero;
                _contadorPdh = nint.Zero;
            }

            _temInventario = false;
            _inventario = [];
            _discos.Clear();
            _cacheDiscos.Clear();
        }
    }

    public void Dispose() => Fechar();

    /// <summary>Leitura fresca de UM sensor já resolvido (null e sensor vazio passam direto).</summary>
    private SensorTemperatura? Atualizar(SensorTemperatura? sensor)
    {
        if (sensor is not { } s || s.Tipo == TipoSensorTemperatura.Nenhum)
        {
            return sensor;
        }

        double? celsius = s.Tipo switch
        {
            TipoSensorTemperatura.Cpu => LerCpu(),
            TipoSensorTemperatura.Gpu => LerGpu(s.Id),
            _ => LerDiscoComCache(s.Id)
        };
        return s with { Celsius = celsius };
    }

    // ------------------------------------------------------------------ CPU (zona térmica, PDH)

    private double? LerCpu()
    {
        if (_pdhIndisponivel)
        {
            return null;
        }

        try
        {
            if (_consultaPdh == nint.Zero && !AbrirPdh())
            {
                _pdhIndisponivel = true; // sem zona térmica neste PC: não tenta de novo a cada leitura
                return null;
            }

            if (NativeMethods.PdhCollectQueryData(_consultaPdh) != 0)
            {
                return null;
            }

            // 1ª chamada só pergunta o tamanho; a 2ª preenche. Cada item: ponteiro do nome da zona +
            // PDH_FMT_COUNTERVALUE (status 4 bytes, alinhamento 4, double 8).
            uint tamanho = 0;
            int r = NativeMethods.PdhGetFormattedCounterArrayW(_contadorPdh, NativeMethods.PDH_FMT_DOUBLE, ref tamanho, out _, nint.Zero);
            if (r != NativeMethods.PDH_MORE_DATA || tamanho == 0)
            {
                return null;
            }

            nint buffer = Marshal.AllocHGlobal((int)tamanho);
            try
            {
                if (NativeMethods.PdhGetFormattedCounterArrayW(_contadorPdh, NativeMethods.PDH_FMT_DOUBLE, ref tamanho, out uint itens, buffer) != 0)
                {
                    return null;
                }

                // 24 bytes por item em 32 e 64 bits: o double alinha a estrutura em 8 (status no 8, valor no 16)
                const int Passo = 24, PosStatus = 8, PosValor = 16;
                var kelvin = new List<double>((int)itens);
                for (int i = 0; i < itens; i++)
                {
                    int status = Marshal.ReadInt32(buffer, (i * Passo) + PosStatus);
                    double valor = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(buffer, (i * Passo) + PosValor));
                    if (status == 0) // PDH_CSTATUS_VALID_DATA
                    {
                        kelvin.Add(_pdhEmKelvinInteiro ? valor : valor / 10.0);
                    }
                }

                return Temperaturas.ZonaMaisQuente(kelvin);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _pdhIndisponivel = true;
            return null;
        }
    }

    /// <summary>Abre a consulta com o contador de décimos de Kelvin; Windows antigo só tem o de Kelvin inteiro.</summary>
    private bool AbrirPdh()
    {
        if (NativeMethods.PdhOpenQueryW(null, nint.Zero, out nint consulta) != 0)
        {
            return false;
        }

        _pdhEmKelvinInteiro = false;
        if (NativeMethods.PdhAddEnglishCounterW(consulta, @"\Thermal Zone Information(*)\High Precision Temperature", nint.Zero, out nint contador) != 0)
        {
            _pdhEmKelvinInteiro = true;
            if (NativeMethods.PdhAddEnglishCounterW(consulta, @"\Thermal Zone Information(*)\Temperature", nint.Zero, out contador) != 0)
            {
                NativeMethods.PdhCloseQuery(consulta);
                return false;
            }
        }

        _consultaPdh = consulta;
        _contadorPdh = contador;
        return true;
    }

    // ------------------------------------------------------------------ GPU (D3DKMT)

    private void ListarGpus(List<SensorTemperatura> lista) =>
        PercorrerGpus((nome, _) => true, (nome, celsius) =>
        {
            if (celsius is not null)
            {
                lista.Add(new SensorTemperatura(IdUnico(lista, Temperaturas.PrefixoGpu + nome), nome, celsius));
            }
        });

    private double? LerGpu(string id)
    {
        // O Id pode ter o sufixo " #2" de uma 2ª placa igual: conta as ocorrências do nome até chegar nela
        (string nome, int ordem) = SepararOrdem(id[Temperaturas.PrefixoGpu.Length..]);
        double? achada = null;
        int vistas = 0;
        PercorrerGpus(
            (n, _) => n == nome && ++vistas == ordem,
            (_, celsius) => achada = celsius);
        return achada;
    }

    /// <summary>
    /// Enumera os adaptadores de vídeo; para os que <paramref name="querLer"/> aceitar pelo nome, lê a
    /// temperatura e entrega a <paramref name="aoLer"/>. O nome vem do registro do driver (não toca a
    /// placa); só a leitura de temperatura fala com ela. Todo adaptador aberto é fechado aqui.
    /// </summary>
    private static void PercorrerGpus(Func<string, int, bool> querLer, Action<string, double?> aoLer)
    {
        const int TamanhoNomes = 260 * 2 * 4; // D3DKMT_ADAPTERREGISTRYINFO: 4 textos WCHAR[MAX_PATH]
        int tamanhoInfo = Marshal.SizeOf<NativeMethods.D3DKMT_ADAPTERINFO>();
        int tamanhoPerf = Marshal.SizeOf<NativeMethods.D3DKMT_ADAPTER_PERFDATA>();
        nint adaptadores = nint.Zero, nomes = nint.Zero, perf = nint.Zero;
        try
        {
            // Com a lista nula, o Windows devolve quantos adaptadores cabem (medido: 34)
            var enumeracao = new NativeMethods.D3DKMT_ENUMADAPTERS2();
            if (NativeMethods.D3DKMTEnumAdapters2(ref enumeracao) != 0 || enumeracao.NumAdapters is 0 or > 256)
            {
                return;
            }

            adaptadores = Marshal.AllocHGlobal(tamanhoInfo * (int)enumeracao.NumAdapters);
            enumeracao.pAdapters = adaptadores;
            if (NativeMethods.D3DKMTEnumAdapters2(ref enumeracao) != 0)
            {
                return;
            }

            nomes = Marshal.AllocHGlobal(TamanhoNomes);
            perf = Marshal.AllocHGlobal(tamanhoPerf);
            for (int i = 0; i < enumeracao.NumAdapters; i++)
            {
                var info = Marshal.PtrToStructure<NativeMethods.D3DKMT_ADAPTERINFO>(adaptadores + (i * tamanhoInfo));
                try
                {
                    Marshal.WriteInt16(nomes, 0); // texto vazio se a consulta falhar
                    var consulta = new NativeMethods.D3DKMT_QUERYADAPTERINFO
                    {
                        hAdapter = info.hAdapter,
                        Type = NativeMethods.KMTQAITYPE_ADAPTERREGISTRYINFO,
                        pPrivateDriverData = nomes,
                        PrivateDriverDataSize = TamanhoNomes
                    };
                    // Adaptador de software ("Microsoft Basic Render Driver") não tem registro: falha e é pulado
                    if (NativeMethods.D3DKMTQueryAdapterInfo(ref consulta) != 0)
                    {
                        continue;
                    }

                    string nome = (Marshal.PtrToStringUni(nomes) ?? "").Trim();
                    if (nome.Length == 0 || !querLer(nome, i))
                    {
                        continue;
                    }

                    for (int b = 0; b < tamanhoPerf; b++)
                    {
                        Marshal.WriteByte(perf, b, 0); // PhysicalAdapterIndex = 0 e o resto zerado
                    }

                    consulta.Type = NativeMethods.KMTQAITYPE_ADAPTERPERFDATA;
                    consulta.pPrivateDriverData = perf;
                    consulta.PrivateDriverDataSize = (uint)tamanhoPerf;
                    double? celsius = null;
                    if (NativeMethods.D3DKMTQueryAdapterInfo(ref consulta) == 0)
                    {
                        double c = Marshal.PtrToStructure<NativeMethods.D3DKMT_ADAPTER_PERFDATA>(perf).Temperature / 10.0;
                        celsius = Temperaturas.Plausivel(c) ? c : null; // 0 = o driver não informa
                    }

                    aoLer(nome, celsius);
                }
                finally
                {
                    var fechar = new NativeMethods.D3DKMT_CLOSEADAPTER { hAdapter = info.hAdapter };
                    NativeMethods.D3DKMTCloseAdapter(ref fechar);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Windows sem estas funções (anterior ao 10 1803): sem GPU na lista
        }
        finally
        {
            Marshal.FreeHGlobal(adaptadores);
            Marshal.FreeHGlobal(nomes);
            Marshal.FreeHGlobal(perf);
        }
    }

    // ------------------------------------------------------------------ discos (consulta de propriedade)

    private void ListarDiscos(List<SensorTemperatura> lista)
    {
        _discos.Clear();
        for (int numero = 0; numero < MaximoDiscos; numero++)
        {
            using SafeFileHandle disco = AbrirDisco(numero);
            if (disco.IsInvalid)
            {
                continue;
            }

            // Descritor do dispositivo (propriedade 0): modelo no deslocamento do byte 16, barramento no 28
            byte[] descritor = new byte[1024];
            if (!Consultar(disco, propriedade: 0, descritor, out int lidos) || lidos < 32)
            {
                continue;
            }

            int inicioModelo = BitConverter.ToInt32(descritor, 16);
            bool nvme = BitConverter.ToInt32(descritor, 28) == BarramentoNvme;
            string modelo = TextoAscii(descritor, inicioModelo, lidos);
            if (modelo.Length == 0)
            {
                modelo = $"PhysicalDrive{numero}";
            }

            // "Tem custo de busca" (propriedade 7) = prato girando = HD; SSD e NVMe respondem 0
            byte[] busca = new byte[12];
            bool hd = !nvme && Consultar(disco, propriedade: 7, busca, out int lidosBusca) && lidosBusca >= 9 && busca[8] != 0;

            if (LerDisco(disco, nvme) is not { } celsius)
            {
                continue; // disco sem sensor acessível (pendrive, leitor de cartão, SATA sem suporte)
            }

            string id = IdUnico(lista, (hd ? Temperaturas.PrefixoHd : Temperaturas.PrefixoSsd) + modelo);
            lista.Add(new SensorTemperatura(id, modelo, celsius));
            _discos[id] = (numero, nvme);
            _cacheDiscos[id] = (Environment.TickCount64, celsius);
        }
    }

    private double? LerDiscoComCache(string id)
    {
        long agora = Environment.TickCount64;
        if (_cacheDiscos.TryGetValue(id, out var cache) && agora - cache.EmMs < ValidadeDiscoMs)
        {
            return cache.Celsius;
        }

        double? celsius = null;
        if (_discos.TryGetValue(id, out var d))
        {
            using SafeFileHandle disco = AbrirDisco(d.Numero);
            celsius = disco.IsInvalid ? null : LerDisco(disco, d.Nvme);
        }

        _cacheDiscos[id] = (agora, celsius);
        return celsius;
    }

    /// <summary>Acesso ZERO: só consultas de propriedade — é o que dispensa o administrador.</summary>
    private static SafeFileHandle AbrirDisco(int numero) =>
        NativeMethods.CreateFileW($@"\\.\PhysicalDrive{numero}", 0, NativeMethods.FILE_SHARE_READ_WRITE,
            nint.Zero, NativeMethods.OPEN_EXISTING, 0, nint.Zero);

    private static double? LerDisco(SafeFileHandle disco, bool nvme)
    {
        if (nvme && LerSmartNvme(disco) is { } smart)
        {
            return smart;
        }

        // Descritor genérico (propriedade 16, StorageDeviceTemperatureProperty)
        byte[] saida = new byte[512];
        return Consultar(disco, propriedade: 16, saida, out int lidos)
            ? Temperaturas.DeDescritorDeTemperatura(saida, lidos)
            : null;
    }

    /// <summary>
    /// Registro SMART do NVMe (página de log 2) por consulta específica de protocolo. O pedido é o
    /// STORAGE_PROPERTY_QUERY (8 bytes) + STORAGE_PROTOCOL_SPECIFIC_DATA (40) + espaço para os 512
    /// bytes do registro; a resposta volta no MESMO formato, com o registro logo depois dos 48 bytes.
    /// </summary>
    private static double? LerSmartNvme(SafeFileHandle disco)
    {
        const int Cabecalho = 8, Especifico = 40, Registro = 512;
        byte[] buffer = new byte[Cabecalho + Especifico + Registro];
        BitConverter.TryWriteBytes(buffer.AsSpan(0), 50);                    // StorageDeviceProtocolSpecificProperty
        BitConverter.TryWriteBytes(buffer.AsSpan(4), 0);                     // PropertyStandardQuery
        BitConverter.TryWriteBytes(buffer.AsSpan(Cabecalho + 0), 3);         // ProtocolTypeNvme
        BitConverter.TryWriteBytes(buffer.AsSpan(Cabecalho + 4), 2);         // NVMeDataTypeLogPage
        BitConverter.TryWriteBytes(buffer.AsSpan(Cabecalho + 8), 2);         // página 2 = SMART / saúde
        BitConverter.TryWriteBytes(buffer.AsSpan(Cabecalho + 16), Especifico); // onde começam os dados
        BitConverter.TryWriteBytes(buffer.AsSpan(Cabecalho + 20), Registro);   // quantos bytes de dados
        if (!NativeMethods.DeviceIoControl(disco, NativeMethods.IOCTL_STORAGE_QUERY_PROPERTY,
                buffer, buffer.Length, buffer, buffer.Length, out int lidos, nint.Zero))
        {
            return null;
        }

        int inicio = Cabecalho + BitConverter.ToInt32(buffer, Cabecalho + 16);
        return inicio >= Cabecalho && inicio + 3 <= lidos
            ? Temperaturas.DeSmartNvme(buffer.AsSpan(inicio, lidos - inicio))
            : null;
    }

    /// <summary>IOCTL_STORAGE_QUERY_PROPERTY simples: STORAGE_PROPERTY_QUERY com o número da propriedade e consulta padrão.</summary>
    private static bool Consultar(SafeFileHandle disco, int propriedade, byte[] saida, out int lidos)
    {
        byte[] pedido = new byte[12];
        BitConverter.TryWriteBytes(pedido, propriedade);
        return NativeMethods.DeviceIoControl(disco, NativeMethods.IOCTL_STORAGE_QUERY_PROPERTY,
            pedido, pedido.Length, saida, saida.Length, out lidos, nint.Zero);
    }

    private static string TextoAscii(byte[] dados, int inicio, int limite)
    {
        if (inicio <= 0 || inicio >= limite)
        {
            return "";
        }

        int fim = Array.IndexOf(dados, (byte)0, inicio, limite - inicio);
        return Encoding.ASCII.GetString(dados, inicio, (fim < 0 ? limite : fim) - inicio).Trim();
    }

    // ------------------------------------------------------------------ Ids repetidos (duas peças iguais)

    /// <summary>Duas peças do mesmo modelo: a 2ª ganha " #2" no Id (e a 3ª " #3"), na ordem em que o Windows as lista.</summary>
    private static string IdUnico(List<SensorTemperatura> lista, string id)
    {
        if (!lista.Exists(s => s.Id == id))
        {
            return id;
        }

        for (int n = 2; ; n++)
        {
            string candidato = $"{id} #{n}";
            if (!lista.Exists(s => s.Id == candidato))
            {
                return candidato;
            }
        }
    }

    /// <summary>"NOME #2" → ("NOME", 2); sem sufixo → (texto, 1).</summary>
    public static (string Nome, int Ordem) SepararOrdem(string texto)
    {
        int marca = texto.LastIndexOf(" #", StringComparison.Ordinal);
        return marca > 0 && int.TryParse(texto.AsSpan(marca + 2), out int ordem) && ordem >= 2
            ? (texto[..marca], ordem)
            : (texto, 1);
    }
}
