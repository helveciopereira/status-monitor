using System.Windows;
using System.Windows.Media;

namespace VelocimetroInternet.Apresentacao;

/// <summary>
/// v1.7.0 (Onda 13): o mini gráfico dos últimos 60 s desenhado ATRÁS do número, dentro da pílula.
///
/// Desenho "por subtração": a pílula já tem um véu translúcido na cor do número. O gráfico pinta a
/// cor da BARRA (translúcida, <see cref="Paletas.AlfaMascaraGrafico"/>) na região ACIMA da curva,
/// apagando ali a maior parte do véu. O que continua colorido — do chão da pílula até a curva — é o
/// gráfico de área. Assim o fundo atrás do número nunca fica mais parecido com o número do que já
/// era: o contraste (WCAG) calibrado pelas paletas vale inteiro, em todas elas e nos dois temas.
/// (Pintar o gráfico na cor do número por cima do véu foi medido e descartado: ver Paletas.)
///
/// Custo: um StreamGeometry com ~60 pontos por atualização (1×/s no padrão), só quando a opção
/// está ligada e o layout de duas linhas está na tela. Sem animação nem timer próprio: redesenha
/// quando o App manda a próxima amostra.
/// </summary>
public sealed class MiniGrafico : FrameworkElement
{
    private IReadOnlyList<(double X, double Y)> _pontos = [];
    private Brush? _mascara;

    public MiniGrafico()
    {
        // O gráfico é fundo: não recebe clique (o widget inteiro é que abre o painel)
        IsHitTestVisible = false;
    }

    /// <summary>Raio dos cantos da pílula, para recortar o desenho na mesma curva (4 − borda 0,75).</summary>
    public double RaioCantos { get; set; } = 3.25;

    /// <summary>Cor da "borracha" já com a opacidade (Paletas.Resolver → MascaraGrafico).</summary>
    public void DefinirCor(Color cor)
    {
        if (cor.A == 0)
        {
            _mascara = null; // cor ainda não aplicada: não desenha nada
        }
        else
        {
            var pincel = new SolidColorBrush(cor);
            pincel.Freeze();
            _mascara = pincel;
        }

        InvalidateVisual();
    }

    /// <summary>Novos pontos (frações 0–1 de <see cref="SerieCurta.Pontos"/>); redesenha.</summary>
    public void DefinirPontos(IReadOnlyList<(double X, double Y)> pontos)
    {
        _pontos = pontos;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        double h = ActualHeight;
        // Menos de 2 pontos (acabou de ligar, ou pausou): a pílula fica como sempre foi
        if (_mascara is null || _pontos.Count < 2 || w < 2 || h < 2)
        {
            return;
        }

        // Recorte com o mesmo arredondamento da pílula: a borracha não "vaza" nos cantos
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), RaioCantos, RaioCantos));

        // 1 px de chão sempre colorido: com a internet parada a pílula continua reconhecível
        double util = h - 1;
        Point Ponto((double X, double Y) p) => new(p.X * w, util * (1 - p.Y));

        // Região ACIMA da curva: canto de cima à esquerda → desce até o chão antes do 1º ponto
        // (sem dados ali, nada fica colorido) → percorre a curva → sobe no "agora" → fecha pelo topo
        var acima = new StreamGeometry();
        using (StreamGeometryContext g = acima.Open())
        {
            g.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
            g.LineTo(new Point(0, h), isStroked: false, isSmoothJoin: false);
            g.LineTo(new Point(_pontos[0].X * w, h), isStroked: false, isSmoothJoin: false);
            foreach ((double X, double Y) p in _pontos)
            {
                g.LineTo(Ponto(p), isStroked: false, isSmoothJoin: true);
            }

            g.LineTo(new Point(_pontos[^1].X * w, 0), isStroked: false, isSmoothJoin: false);
        }

        acima.Freeze();
        dc.DrawGeometry(_mascara, null, acima);
        dc.Pop();
    }
}
