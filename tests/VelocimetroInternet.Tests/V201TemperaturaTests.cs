using VelocimetroInternet.Apresentacao;
using VelocimetroInternet.Config;
using VelocimetroInternet.Medicao;
using VelocimetroInternet.Sistema;

namespace VelocimetroInternet.Tests;

/// <summary>
/// v2.0.1 — coluna de temperatura (desligada por padrão) entre CPU/RAM e a internet. Cobre os padrões
/// e a guarda da configuração, a escolha do sensor de cada linha (automática, nenhuma, explícita,
/// peça ausente), as conversões (zona térmica em Kelvin, SMART do NVMe, descritor genérico), texto,
/// alerta, a largura na barra e a leitura real desta máquina (que não pode estourar em nenhum PC).
/// </summary>
public sealed class V201TemperaturaTests
{
    private static readonly SensorTemperatura Cpu = new("cpu", "", 79);
    private static readonly SensorTemperatura Gpu = new("gpu:Placa A", "Placa A", 57);
    private static readonly SensorTemperatura Ssd = new("ssd:Disco X", "Disco X", 34);

    // ------------------------------------------------------------------ configuração

    [Fact]
    public void Padroes_TemperaturaDesligada_LinhasAutomaticas()
    {
        var c = new Configuracoes();
        Assert.False(c.MostrarTemperatura); // pedido do dono: não começa habilitada
        Assert.Equal("auto", c.TemperaturaLinha1);
        Assert.Equal("auto", c.TemperaturaLinha2);
    }

    [Fact]
    public void SettingsAntigo_SemOsCampos_ContinuaSemAColuna()
    {
        // Quem atualiza tem um settings.json sem os campos novos: a leitura preenche os padrões
        var c = System.Text.Json.JsonSerializer.Deserialize<Configuracoes>("{\"MostrarCpuRam\":true}")!;
        c.Normalizar();
        Assert.False(c.MostrarTemperatura);
        Assert.Equal("auto", c.TemperaturaLinha1);
    }

    [Theory]
    [InlineData("auto", "auto")]
    [InlineData("nenhuma", "nenhuma")]
    [InlineData("cpu", "cpu")]
    [InlineData("gpu:NVIDIA GeForce RTX 4070", "gpu:NVIDIA GeForce RTX 4070")]
    [InlineData("ssd:Modelo #2", "ssd:Modelo #2")]
    [InlineData("hd:WDC_WD10", "hd:WDC_WD10")]
    [InlineData("ram", "auto")]      // RAM não tem sensor acessível: código desconhecido
    [InlineData("", "auto")]
    [InlineData(null, "auto")]
    public void Normalizar_CodigoDeLinhaDesconhecidoVoltaAoAutomatico(string? informado, string esperado)
    {
        var c = new Configuracoes { TemperaturaLinha1 = informado!, TemperaturaLinha2 = informado! };
        c.Normalizar();
        Assert.Equal(esperado, c.TemperaturaLinha1);
        Assert.Equal(esperado, c.TemperaturaLinha2);
    }

    [Fact]
    public void Clonar_LevaAsEscolhasDeTemperatura()
    {
        var c = new Configuracoes { MostrarTemperatura = true, TemperaturaLinha1 = "cpu", TemperaturaLinha2 = "nenhuma" };
        Configuracoes copia = c.Clonar();
        Assert.True(copia.MostrarTemperatura);
        Assert.Equal("cpu", copia.TemperaturaLinha1);
        Assert.Equal("nenhuma", copia.TemperaturaLinha2);
    }

    // ------------------------------------------------------------------ escolha do sensor de cada linha

    [Fact]
    public void Resolver_Automatico_PegaOsDoisPrimeirosComLeitura()
    {
        LinhasTemperatura l = Temperaturas.Resolver("auto", "auto", [Cpu, Gpu, Ssd]);
        Assert.Equal("cpu", l.Cima!.Value.Id);
        Assert.Equal("gpu:Placa A", l.Baixo!.Value.Id);
    }

    [Fact]
    public void Resolver_Automatico_PulaSensorSemLeituraEOQueAOutraLinhaUsa()
    {
        var gpuMuda = new SensorTemperatura("gpu:Sem leitura", "Sem leitura", null);
        // Linha de baixo fixa na CPU: a automática de cima não repete a CPU nem pega a GPU sem leitura
        LinhasTemperatura l = Temperaturas.Resolver("auto", "cpu", [Cpu, gpuMuda, Ssd]);
        Assert.Equal("ssd:Disco X", l.Cima!.Value.Id);
        Assert.Equal("cpu", l.Baixo!.Value.Id);
    }

    [Fact]
    public void Resolver_Nenhuma_DevolveNull_EAutomaticaSemSensores_DevolveSensorVazio()
    {
        LinhasTemperatura l = Temperaturas.Resolver("nenhuma", "auto", []);
        Assert.Null(l.Cima);
        Assert.Equal(TipoSensorTemperatura.Nenhum, l.Baixo!.Value.Tipo);
        Assert.Null(l.Baixo.Value.Celsius);
    }

    [Fact]
    public void Resolver_SensorEscolhidoAusente_MantemORotuloSemLeitura()
    {
        // HD externo desligado: a linha continua "HD —" em vez de trocar de sensor sozinha
        LinhasTemperatura l = Temperaturas.Resolver("hd:Externo", "nenhuma", [Cpu]);
        Assert.Equal(TipoSensorTemperatura.Hd, l.Cima!.Value.Tipo);
        Assert.Null(l.Cima.Value.Celsius);
        Assert.Null(l.Baixo);
    }

    [Theory]
    [InlineData(false, "auto", "auto", false)]       // opção desligada
    [InlineData(true, "auto", "auto", true)]
    [InlineData(true, "nenhuma", "cpu", true)]
    [InlineData(true, "nenhuma", "nenhuma", false)]  // ligada, mas as duas linhas vazias: sem coluna (sem vão)
    public void ColunaVisivel_SoComAOpcaoLigadaEAlgumaLinha(bool mostrar, string l1, string l2, bool esperado) =>
        Assert.Equal(esperado, Temperaturas.ColunaVisivel(mostrar, l1, l2));

    // ------------------------------------------------------------------ rótulo, texto e alerta

    [Theory]
    [InlineData("cpu", "CPU")]
    [InlineData("gpu:X", "GPU")]
    [InlineData("ssd:X", "SSD")]
    [InlineData("hd:X", "HD")]
    [InlineData("", "TEMP")]
    public void Rotulo_VemDoPrefixoDoId(string id, string esperado) =>
        Assert.Equal(esperado, Temperaturas.Rotulo(Temperaturas.TipoDe(id)));

    [Theory]
    [InlineData(54.4, "54°C")]
    [InlineData(54.5, "55°C")]
    [InlineData(100.0, "100°C")]
    public void Formatar_GrausInteiros(double celsius, string esperado) =>
        Assert.Equal(esperado, Temperaturas.Formatar(celsius));

    [Fact]
    public void Formatar_SemLeitura_Travessao() => Assert.Equal("—", Temperaturas.Formatar(null));

    [Theory]
    [InlineData(TipoSensorTemperatura.Cpu, 89.4, false)]
    [InlineData(TipoSensorTemperatura.Cpu, 89.5, true)]   // mostrado como "90°C" → vermelho, como o CPU/RAM
    [InlineData(TipoSensorTemperatura.Gpu, 85.0, true)]
    [InlineData(TipoSensorTemperatura.Ssd, 69.0, false)]
    [InlineData(TipoSensorTemperatura.Ssd, 70.0, true)]
    [InlineData(TipoSensorTemperatura.Hd, 55.0, true)]
    [InlineData(TipoSensorTemperatura.Nenhum, 200.0, false)]
    public void EmAlerta_LimitePorPeca_ComOValorArredondado(TipoSensorTemperatura tipo, double celsius, bool esperado) =>
        Assert.Equal(esperado, Temperaturas.EmAlerta(tipo, celsius));

    [Fact]
    public void EmAlerta_SemLeitura_NaoEAlerta() => Assert.False(Temperaturas.EmAlerta(TipoSensorTemperatura.Cpu, null));

    // ------------------------------------------------------------------ conversões

    [Fact]
    public void ZonaMaisQuente_IgnoraZonaDeEnfeiteELixo()
    {
        // Medido nesta máquina: TZ01 parada em 293,2 K (20 °C) e THRM em 352,2 K (79 °C)
        Assert.Equal(79.05, Temperaturas.ZonaMaisQuente([293.2, 352.2])!.Value, 2);
        Assert.Null(Temperaturas.ZonaMaisQuente([]));
        Assert.Null(Temperaturas.ZonaMaisQuente([0, 273.15, 9999, double.NaN])); // 0 K, 0 °C e absurdos não são leitura
    }

    [Fact]
    public void SmartNvme_TemperaturaCompostaEmKelvinNosBytes1e2()
    {
        byte[] registro = new byte[512];
        registro[1] = 0x33; // 0x0133 = 307 K = 34 °C
        registro[2] = 0x01;
        Assert.Equal(34, Temperaturas.DeSmartNvme(registro));
        Assert.Null(Temperaturas.DeSmartNvme(new byte[512])); // zerado = 0 K: sem leitura
        Assert.Null(Temperaturas.DeSmartNvme(new byte[2]));   // registro curto
    }

    [Fact]
    public void DescritorDeTemperatura_SemSensores_Null_ComSensores_AMaisQuente()
    {
        // O que o NVMe desta máquina devolve: versão 1, tamanho 20, nenhum sensor
        byte[] vazio = new byte[512];
        vazio[0] = 1;
        vazio[4] = 20;
        Assert.Null(Temperaturas.DeDescritorDeTemperatura(vazio, 20));

        byte[] dois = new byte[512];
        dois[12] = 2;        // dois sensores
        dois[24 + 2] = 41;   // 1º: 41 °C
        dois[40 + 2] = 44;   // 2º: 44 °C
        Assert.Equal(44, Temperaturas.DeDescritorDeTemperatura(dois, 56));
        Assert.Equal(41, Temperaturas.DeDescritorDeTemperatura(dois, 40)); // resposta cortada: só o 1º bloco vale
    }

    [Theory]
    [InlineData("Placa A", "Placa A", 1)]
    [InlineData("Placa A #2", "Placa A", 2)]
    [InlineData("Modelo #1", "Modelo #1", 1)]   // "#1" não é sufixo nosso (a 1ª peça não leva número)
    [InlineData("Disco #x", "Disco #x", 1)]
    public void SepararOrdem_SufixoDaSegundaPecaIgual(string texto, string nome, int ordem) =>
        Assert.Equal((nome, ordem), MedidorTemperatura.SepararOrdem(texto));

    // ------------------------------------------------------------------ largura na barra

    [Fact]
    public void Layout_SemTemperatura_NadaMuda()
    {
        Assert.Equal(TaskbarLayout.LarguraLogicaNormal(true, 1.0), TaskbarLayout.LarguraLogicaNormal(true, 1.0, mostrarTemperatura: false));
        Assert.Equal(TaskbarLayout.LarguraLogicaCompacta(true, 1.0), TaskbarLayout.LarguraLogicaCompacta(true, 1.0, mostrarTemperatura: false));
    }

    [Fact]
    public void Layout_ComTemperatura_SomaAColunaNosDoisLayoutsEEmTodosOsModos()
    {
        Assert.Equal(
            TaskbarLayout.LarguraLogicaNormal(true, 1.0) + TaskbarLayout.LarguraLogicaTemperaturaNormalPx,
            TaskbarLayout.LarguraLogicaNormal(true, 1.0, mostrarTemperatura: true));
        foreach (ModoExibicaoWidget modo in Enum.GetValues<ModoExibicaoWidget>())
        {
            Assert.Equal(
                TaskbarLayout.LarguraLogicaCompacta(false, 1.0, modo) + TaskbarLayout.LarguraLogicaTemperaturaCompactaPx,
                TaskbarLayout.LarguraLogicaCompacta(false, 1.0, modo, mostrarTemperatura: true));
        }
    }

    [Fact]
    public void Layout_ComTemperatura_FicaMaisLargoNaVagaDoClima()
    {
        var barra = new NativeMethods.RECT { Left = 0, Top = 1368, Right = 2560, Bottom = 1440 };
        PosicaoWidgetBarra sem = TaskbarLayout.CalcularVagaDoClima(barra, 1169, 1797, 1.5, mostrarSistema: true);
        PosicaoWidgetBarra com = TaskbarLayout.CalcularVagaDoClima(barra, 1169, 1797, 1.5, mostrarSistema: true, mostrarTemperatura: true);
        Assert.Equal(EstadoColisaoBarra.NormalDuasLinhas, com.Estado);
        Assert.Equal((int)Math.Round(TaskbarLayout.LarguraLogicaNormal(true, 1.0, true) * 1.5), com.LarguraFisicaPx);
        // Continua encostado no '^' da bandeja: a borda direita é a mesma, o bloco cresce para a esquerda
        Assert.Equal(sem.XTelaPx + sem.LarguraFisicaPx, com.XTelaPx + com.LarguraFisicaPx);
    }

    // ------------------------------------------------------------------ leitura real desta máquina

    [Fact]
    public void LeituraReal_NaoEstoura_ETudoQueListaEPlausivel()
    {
        using var medidor = new MedidorTemperatura();
        IReadOnlyList<SensorTemperatura> sensores = medidor.Listar();
        // Pode ser vazia (PC sem sensor acessível, máquina de CI): o que vier tem de fazer sentido
        Assert.All(sensores, s =>
        {
            Assert.NotEqual(TipoSensorTemperatura.Nenhum, s.Tipo);
            Assert.True(s.Celsius is { } c && Temperaturas.Plausivel(c), $"{s.Id}: {s.Celsius}");
        });
        Assert.Equal(sensores.Count, sensores.Select(s => s.Id).Distinct().Count()); // Ids únicos

        LinhasTemperatura linhas = medidor.LerLinhas("auto", "auto");
        if (sensores.Count >= 2)
        {
            Assert.NotEqual(linhas.Cima!.Value.Id, linhas.Baixo!.Value.Id);
        }

        medidor.Fechar();
        Assert.NotNull(medidor.LerLinhas("auto", "nenhuma").Cima); // depois de fechar, reabre sozinho
    }
}
