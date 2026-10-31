using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// v0.9.0 (Onda 3, item "seletor visual de cor"): quadrado saturação × brilho + barra de matiz.
/// Montado em código (sem XAML) para ser um bloco só, reaproveitável.
///
/// Uso: atribua <see cref="Cor"/> e ouça <see cref="CorAlterada"/> (disparado só por ação do usuário —
/// mouse ou teclado —, nunca quando o código atribui <see cref="Cor"/>, para não criar laço com o campo hex).
///
/// Teclado (acessibilidade): no quadrado, ←/→ mudam a saturação e ↑/↓ o brilho; na barra, ←/→ mudam o
/// matiz. Shift dá passos maiores.
/// </summary>
public sealed class SeletorCor : UserControl
{
    private const double AlturaQuadrado = 132;
    private const double AlturaBarra = 14;
    private const double DiametroMarcador = 14;

    // Estado interno em HSV: guardar o matiz separado evita que ele "pule" para 0 quando o usuário
    // arrasta até o branco/preto (onde o RGB não guarda matiz nenhum)
    private double _h, _s = 1, _v = 1;

    private readonly Border _quadrado;
    private readonly SolidColorBrush _fundoMatiz = new(Colors.Red);
    private readonly Canvas _camadaMarcador;
    private readonly Grid _marcador;
    private readonly Border _barra;
    private readonly Canvas _camadaCursorMatiz;
    private readonly Border _cursorMatiz;

    /// <summary>A cor mudou por mouse ou teclado (já em RGB opaco).</summary>
    public event Action<Color>? CorAlterada;

    public SeletorCor()
    {
        // ---- quadrado: matiz puro + branco→transparente (horizontal) + transparente→preto (vertical)
        var camadas = new Grid();
        camadas.Children.Add(new Border { Background = _fundoMatiz });
        camadas.Children.Add(new Border
        {
            Background = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(1, 0))
        });
        camadas.Children.Add(new Border
        {
            Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, new Point(0, 0), new Point(0, 1))
        });

        // Marcador: anel branco com contorno preto fino — visível sobre qualquer cor
        _marcador = new Grid { Width = DiametroMarcador, Height = DiametroMarcador, IsHitTestVisible = false };
        _marcador.Children.Add(new Ellipse { Stroke = Brushes.Black, StrokeThickness = 1, Opacity = 0.6 });
        _marcador.Children.Add(new Ellipse { Stroke = Brushes.White, StrokeThickness = 2, Margin = new Thickness(1) });
        // Fica FORA do quadrado (que é recortado nos cantos): o anel aparece inteiro até nas bordas
        _camadaMarcador = new Canvas { IsHitTestVisible = false };
        _camadaMarcador.Children.Add(_marcador);

        _quadrado = new Border
        {
            Height = AlturaQuadrado,
            CornerRadius = new CornerRadius(6),
            Child = camadas,
            Focusable = true,
            Cursor = Cursors.Cross,
            ClipToBounds = true,
            Background = Brushes.Transparent // garante que o quadrado inteiro recebe o mouse
        };
        AutomationProperties.SetName(_quadrado, "Saturação e brilho (setas do teclado ajustam)");
        _quadrado.MouseLeftButtonDown += (_, e) => IniciarArrasto(_quadrado, e, MoverNoQuadrado);
        _quadrado.MouseMove += (_, e) => { if (_quadrado.IsMouseCaptured) { MoverNoQuadrado(e.GetPosition(_quadrado)); } };
        _quadrado.MouseLeftButtonUp += (_, _) => _quadrado.ReleaseMouseCapture();
        _quadrado.KeyDown += TeclaNoQuadrado;
        _quadrado.SizeChanged += (_, _) => { RecortarCantos(_quadrado, 6); PosicionarMarcadores(); };

        // ---- barra de matiz: o círculo cromático aberto em linha (vermelho → … → vermelho)
        var arcoIris = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (int i = 0; i <= 6; i++)
        {
            arcoIris.GradientStops.Add(new GradientStop(CorHsv.MatizPuro(i * 60), i / 6.0));
        }

        _cursorMatiz = new Border
        {
            Width = 6,
            Height = AlturaBarra + 4,
            CornerRadius = new CornerRadius(3),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.7 }
        };
        _camadaCursorMatiz = new Canvas { IsHitTestVisible = false };
        _camadaCursorMatiz.Children.Add(_cursorMatiz);

        _barra = new Border
        {
            Height = AlturaBarra,
            CornerRadius = new CornerRadius(AlturaBarra / 2),
            Background = arcoIris,
            Focusable = true,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 12, 0, 2)
        };
        AutomationProperties.SetName(_barra, "Matiz (setas do teclado ajustam)");
        _barra.MouseLeftButtonDown += (_, e) => IniciarArrasto(_barra, e, MoverNaBarra);
        _barra.MouseMove += (_, e) => { if (_barra.IsMouseCaptured) { MoverNaBarra(e.GetPosition(_barra)); } };
        _barra.MouseLeftButtonUp += (_, _) => _barra.ReleaseMouseCapture();
        _barra.KeyDown += TeclaNaBarra;
        _barra.SizeChanged += (_, _) => PosicionarMarcadores();

        // O cursor da barra fica numa camada por cima, com a mesma área (Grid de 1 célula)
        var areaBarra = new Grid();
        areaBarra.Children.Add(_barra);
        areaBarra.Children.Add(_camadaCursorMatiz);
        _camadaCursorMatiz.Margin = _barra.Margin;

        var areaQuadrado = new Grid();
        areaQuadrado.Children.Add(_quadrado);
        areaQuadrado.Children.Add(_camadaMarcador);

        var pilha = new StackPanel();
        pilha.Children.Add(areaQuadrado);
        pilha.Children.Add(areaBarra);
        Content = pilha;

        AtualizarVisual();
    }

    /// <summary>Matiz guardado (graus). Continua valendo em cinza/branco/preto, onde o RGB não tem matiz.</summary>
    public double Matiz => _h;

    /// <summary>Cor atual (RGB opaco). Atribuir NÃO dispara <see cref="CorAlterada"/>.</summary>
    public Color Cor
    {
        get => new CorHsv(_h, _s, _v).ParaCor();
        set
        {
            if (value == Cor)
            {
                return; // mesma cor: mantém o matiz guardado (importa no branco/preto/cinza)
            }

            CorHsv hsv = CorHsv.De(value);
            // Cinza não tem matiz: conserva o anterior para a barra não saltar para o vermelho
            if (hsv.S > 0 && hsv.V > 0)
            {
                _h = hsv.H;
            }

            _s = hsv.S;
            _v = hsv.V;
            AtualizarVisual();
        }
    }

    // ------------------------------------------------------------------ mouse

    private static void IniciarArrasto(UIElement alvo, MouseButtonEventArgs e, Action<Point> mover)
    {
        alvo.Focus();
        alvo.CaptureMouse();
        mover(e.GetPosition(alvo));
        e.Handled = true;
    }

    private void MoverNoQuadrado(Point p)
    {
        double largura = Math.Max(1, _quadrado.ActualWidth);
        double altura = Math.Max(1, _quadrado.ActualHeight);
        _s = Math.Clamp(p.X / largura, 0, 1);
        _v = 1 - Math.Clamp(p.Y / altura, 0, 1);
        Notificar();
    }

    private void MoverNaBarra(Point p)
    {
        double largura = Math.Max(1, _barra.ActualWidth);
        // 359,9 em vez de 360: o fim da barra é vermelho, mas o matiz fica dentro de [0, 360)
        _h = Math.Clamp(p.X / largura, 0, 1) * 359.9;
        Notificar();
    }

    // ------------------------------------------------------------------ teclado

    private void TeclaNoQuadrado(object sender, KeyEventArgs e)
    {
        double passo = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 0.02;
        switch (e.Key)
        {
            case Key.Left: _s = Math.Clamp(_s - passo, 0, 1); break;
            case Key.Right: _s = Math.Clamp(_s + passo, 0, 1); break;
            case Key.Up: _v = Math.Clamp(_v + passo, 0, 1); break;
            case Key.Down: _v = Math.Clamp(_v - passo, 0, 1); break;
            default: return;
        }

        e.Handled = true;
        Notificar();
    }

    private void TeclaNaBarra(object sender, KeyEventArgs e)
    {
        double passo = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 30 : 5;
        switch (e.Key)
        {
            case Key.Left: _h = (_h - passo + 360) % 360; break;
            case Key.Right: _h = (_h + passo) % 360; break;
            default: return;
        }

        e.Handled = true;
        Notificar();
    }

    // ------------------------------------------------------------------ desenho

    private void Notificar()
    {
        AtualizarVisual();
        CorAlterada?.Invoke(Cor);
    }

    private void AtualizarVisual()
    {
        _fundoMatiz.Color = CorHsv.MatizPuro(_h);
        PosicionarMarcadores();
    }

    private void PosicionarMarcadores()
    {
        Canvas.SetLeft(_marcador, (_s * _quadrado.ActualWidth) - (DiametroMarcador / 2));
        Canvas.SetTop(_marcador, ((1 - _v) * _quadrado.ActualHeight) - (DiametroMarcador / 2));
        // Limitado à barra: nos extremos (vermelho, 0°/360°) o cursor não fica meio para fora
        double xCursor = (_h / 360 * _barra.ActualWidth) - (_cursorMatiz.Width / 2);
        Canvas.SetLeft(_cursorMatiz, Math.Clamp(xCursor, 0, Math.Max(0, _barra.ActualWidth - _cursorMatiz.Width)));
        Canvas.SetTop(_cursorMatiz, -2);
    }

    /// <summary>ClipToBounds corta em retângulo; o recorte arredondado acompanha o CornerRadius.</summary>
    private static void RecortarCantos(FrameworkElement el, double raio) =>
        el.Clip = new RectangleGeometry(new Rect(0, 0, el.ActualWidth, el.ActualHeight), raio, raio);
}
