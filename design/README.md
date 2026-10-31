# Catálogo de Modelos de Design (Stitch → C# WPF .NET 10)

> **Projeto Stitch:** `projects/3690916573458587930` (*Velocímetro de Internet — Windows 11 (C# WPF)*)  
> **Data:** 2026-10-31  
> **Alvo de implementação:** C# 13 · .NET 10 (LTS) · WPF (`TaskbarWidget.xaml`, `DetalhesFlyout.xaml`, `ConfiguracoesWindow.xaml`)

---

## Estrutura desta pasta

| Pasta / Arquivo | Modelo | Screen ID no Stitch |
|---|---|---|
| [`modelo-1-fluent-mica/`](./modelo-1-fluent-mica/) | **Modelo 1 — Win11 Fluent Mica Nativo (Estilo Clima Oficial)** | `8d556bf2e6aa43b4872c6558b30c409d` |
| ├── [`modelo-1-fluent-mica.png`](./modelo-1-fluent-mica/modelo-1-fluent-mica.png) | Captura em alta resolução (Desktop + Barra + Flyout + Configurações) | — |
| └── [`modelo-1-fluent-mica.html`](./modelo-1-fluent-mica/modelo-1-fluent-mica.html) | Protótipo HTML/CSS gerado pelo Stitch (tokens, cores e medidas exatas) | — |
| [`modelo-2-telemetria-hud/`](./modelo-2-telemetria-hud/) | **Modelo 2 — Telemetria Técnica & HUD de Precisão (Estilo Task Manager / Dev)** | `cea5437a33464b76b80b976302e7518d` |
| ├── [`modelo-2-telemetria-hud.png`](./modelo-2-telemetria-hud/modelo-2-telemetria-hud.png) | Captura em alta resolução (4 variantes de widget + HUD técnico + auditoria `GetIfTable2`) | — |
| └── [`modelo-2-telemetria-hud.html`](./modelo-2-telemetria-hud/modelo-2-telemetria-hud.html) | Protótipo HTML/CSS gerado pelo Stitch | — |
| [`modelo-3-acrylic-pill/`](./modelo-3-acrylic-pill/) | **Modelo 3 — Minimalismo Acrylic Pill & Semântica Visual** | `455fe7cd35fd40a2a58925034da8bbbd` |
| ├── [`modelo-3-acrylic-pill.png`](./modelo-3-acrylic-pill/modelo-3-acrylic-pill.png) | Captura em alta resolução (Micro-pílulas na barra + Quick Flyout + Estúdio de Cores/Colisão) | — |
| └── [`modelo-3-acrylic-pill.html`](./modelo-3-acrylic-pill/modelo-3-acrylic-pill.html) | Protótipo HTML/CSS gerado pelo Stitch | — |

---

## Comparativo Rápido para Escolha

| Critério | Modelo 1: Fluent Mica Nativo | Modelo 2: Telemetria & HUD | Modelo 3: Acrylic Pill |
|---|---|---|---|
| **Fidelidade ao "Estilo Clima"** | ⭐⭐⭐⭐⭐ (Idêntico a um componente nativo da Microsoft) | ⭐⭐⭐⭐ (Mais técnico, lembra o Gerenciador de Tarefas) | ⭐⭐⭐⭐ (Mais moderno/destacado por pílulas de cor) |
| **Fonte do Widget na Barra** | `Segoe UI Variable Text` com `NumeralAlignment="Tabular"` | `Cascadia Mono` / `Consolas` (monoespaçada estrita) | `Segoe UI Variable Display` Semibold em cápsulas |
| **Indicador extra na Barra** | Nenhum (apenas seta + número + hover `4px` translúcido) | Micro-barra vertical de `3px` de carga + sparkline sutil | Cápsula translúcida (`18%` opacidade) envolvendo cada linha |
| **Complexidade em WPF (XAML)** | **Mínima** (`Grid` 2 linhas + `Border` hover + 2 `TextBlock`) | **Baixa/Média** (adiciona `ProgressBar` vertical ou `Polyline`) | **Baixa** (2 `Border` com `CornerRadius="4"` e fundo com alfa) |
| **Custo de CPU/GPU por segundo** | ~0% (só invalida texto dos 2 `TextBlock`) | Muito baixo (atualiza altura de 2 `Rectangle` + texto) | ~0% (só invalida texto dentro das 2 pílulas) |

---

## Como Traduzir Cada Modelo Diretamente para C# / WPF (.NET 10)

### 1. Dicionário de Cores por Tema (`Temas/PaletaPadrao.xaml`)
Comum aos 3 modelos, respeitando os contrastes WCAG 2.1 calculados em [`01-ANALISE.md`](../01-ANALISE.md):

```xml
<!-- Paleta Escura (SystemUsesLightTheme = 0, Barra #1C1C1C) -->
<Color x:Key="UploadColorDark">#FF4CC2FF</Color>       <!-- Contraste ~9,0:1 (AAA) -->
<Color x:Key="DownloadColorDark">#FFFF6B6B</Color>     <!-- Contraste ~6,5:1 (AA+) -->
<Color x:Key="HoverBrushDark">#0FFFFFFF</Color>        <!-- Branco 6% igual ao botão Clima -->
<Color x:Key="PillBgUploadDark">#2E4CC2FF</Color>      <!-- 18% opacidade (Modelo 3) -->
<Color x:Key="PillBgDownloadDark">#2EFF6B6B</Color>    <!-- 18% opacidade (Modelo 3) -->

<!-- Paleta Clara (SystemUsesLightTheme = 1, Barra #F3F3F3) -->
<Color x:Key="UploadColorLight">#FF005FB8</Color>      <!-- Contraste ~6,0:1 (AA+) -->
<Color x:Key="DownloadColorLight">#FFC42B1C</Color>    <!-- Contraste ~5,5:1 (AA) -->
<Color x:Key="HoverBrushLight">#0D000000</Color>       <!-- Preto 5% igual ao botão Clima -->
```

### 2. `TaskbarWidget.xaml` — Modelo 1 (Fluent Mica Nativo)
```xml
<!-- Bloco de 40px de altura útil dentro dos 48px lógicos da barra do Win 11 -->
<Border x:Name="HoverContainer"
        Width="96" Height="40"
        CornerRadius="4"
        Background="Transparent"
        Padding="6,2">
    <Border.Style>
        <Style TargetType="Border">
            <Style.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                    <!-- Realce idêntico ao botão Widgets/Clima do Explorer -->
                    <Setter Property="Background" Value="{DynamicResource HoverBrush}" />
                </Trigger>
            </Style.Triggers>
        </Style>
    </Border.Style>

    <!-- Modo Normal: 2 linhas empilhadas | Modo Compacto (colisão): 1 linha horizontal -->
    <Grid x:Name="StackNormal">
        <Grid.RowDefinitions>
            <RowDefinition Height="*" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <!-- Linha 0: Upload (Azul) -->
        <TextBlock Grid.Row="0"
                   Text="{Binding UploadFormatado}"
                   Foreground="{DynamicResource UploadBrush}"
                   FontFamily="Segoe UI Variable Text"
                   FontSize="11.5"
                   FontWeight="SemiBold"
                   Typography.NumeralAlignment="Tabular"
                   VerticalAlignment="Center"
                   HorizontalAlignment="Right" />

        <!-- Linha 1: Download (Vermelho) -->
        <TextBlock Grid.Row="1"
                   Text="{Binding DownloadFormatado}"
                   Foreground="{DynamicResource DownloadBrush}"
                   FontFamily="Segoe UI Variable Text"
                   FontSize="11.5"
                   FontWeight="SemiBold"
                   Typography.NumeralAlignment="Tabular"
                   VerticalAlignment="Center"
                   HorizontalAlignment="Right" />
    </Grid>
</Border>
```

### 3. `TaskbarWidget.xaml` — Modelo 2 (Telemetria & HUD com Micro-Barras)
```xml
<!-- Adiciona indicador vertical de 3px à direita de cada linha proporcional à banda contratada -->
<Border Width="102" Height="40" CornerRadius="4" Padding="6,2" Background="{DynamicResource HudBgBrush}">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="*" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="5" />
        </Grid.ColumnDefinitions>

        <TextBlock Grid.Row="0" Grid.Column="0"
                   Text="{Binding UploadFormatado}"
                   Foreground="{DynamicResource UploadBrush}"
                   FontFamily="Cascadia Mono" FontSize="11" FontWeight="SemiBold"
                   HorizontalAlignment="Right" VerticalAlignment="Center" />
        <Border Grid.Row="0" Grid.Column="1" Width="3" Height="14" CornerRadius="1.5" Background="#22FFFFFF" VerticalAlignment="Center" HorizontalAlignment="Right">
            <Border Width="3" Height="{Binding UploadBarHeight}" CornerRadius="1.5" Background="{DynamicResource UploadBrush}" VerticalAlignment="Bottom" />
        </Border>

        <TextBlock Grid.Row="1" Grid.Column="0"
                   Text="{Binding DownloadFormatado}"
                   Foreground="{DynamicResource DownloadBrush}"
                   FontFamily="Cascadia Mono" FontSize="11" FontWeight="SemiBold"
                   HorizontalAlignment="Right" VerticalAlignment="Center" />
        <Border Grid.Row="1" Grid.Column="1" Width="3" Height="14" CornerRadius="1.5" Background="#22FFFFFF" VerticalAlignment="Center" HorizontalAlignment="Right">
            <Border Width="3" Height="{Binding DownloadBarHeight}" CornerRadius="1.5" Background="{DynamicResource DownloadBrush}" VerticalAlignment="Bottom" />
        </Border>
    </Grid>
</Border>
```

### 4. `TaskbarWidget.xaml` — Modelo 3 (Minimalismo Acrylic Pill)
```xml
<!-- Cada linha fica dentro de uma micro-pílula translúcida com absorção cromática -->
<StackPanel Width="98" VerticalAlignment="Center">
    <Border CornerRadius="4" Background="{DynamicResource PillBgUpload}" Padding="5,1" Margin="0,0,0,2">
        <TextBlock Text="{Binding UploadFormatado}"
                   Foreground="{DynamicResource UploadBrush}"
                   FontFamily="Segoe UI Variable Text" FontSize="11" FontWeight="SemiBold"
                   Typography.NumeralAlignment="Tabular" HorizontalAlignment="Right" />
    </Border>
    <Border CornerRadius="4" Background="{DynamicResource PillBgDownload}" Padding="5,1">
        <TextBlock Text="{Binding DownloadFormatado}"
                   Foreground="{DynamicResource DownloadBrush}"
                   FontFamily="Segoe UI Variable Text" FontSize="11" FontWeight="SemiBold"
                   Typography.NumeralAlignment="Tabular" HorizontalAlignment="Right" />
    </Border>
</StackPanel>
```

