using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Markdown.Plot;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Plot;

namespace Nexaflow.Tests.Visuals.Markdown.Plot;

/// <summary>
/// Writes the correlation-plot figures for the user documentation, through the real builder so the picture
/// in the docs is the picture the app draws. Opt-in: set <c>NEXAFLOW_WRITE_FIGURES</c> to the output folder.
/// </summary>
[TestClass]
[TestCategory("UI")]
[NoCoverage("writes a documentation figure")]
public class PlotFigureWriter
{
    /// <summary>What the help page shows first: two columns of numbers and a title.</summary>
    private const string Plain = """
        title: Weight against fuel economy
        xTitle: Weight (lb)
        yTitle: Miles per gallon

        weight  mpg
        2620    21.0
        2875    21.0
        2320    22.8
        3215    21.4
        3440    18.7
        3460    18.1
        3570    14.3
        3190    24.4
        3150    22.8
        3440    19.2
        3440    17.8
        4070    16.4
        3730    17.3
        3780    15.2
        5250    10.4
        5424    10.4
        5345    14.7
        2200    32.4
        1615    30.4
        1835    33.9
        2465    21.5
        3520    15.5
        3435    15.2
        3840    13.3
        3845    19.2
        1935    27.3
        2140    26.0
        1513    30.4
        3170    15.8
        2770    19.7
        3570    15.0
        2780    21.4
        """;

    /// <summary>A column of names feeding colour, which is what makes a key.</summary>
    private const string Grouped = """
        title: Fuel economy by cylinders
        colour: cyl
        xTitle: Weight (lb)
        yTitle: Miles per gallon

        weight  mpg   cyl
        2620    21.0  six
        2320    22.8  four
        3440    18.7  eight
        3570    14.3  eight
        3190    24.4  four
        2200    32.4  four
        1615    30.4  four
        5250    10.4  eight
        3170    15.8  eight
        2770    19.7  six
        3460    18.1  six
        1835    33.9  four
        """;

    /// <summary>A third column feeding size, which is the whole of what a bubble plot is.</summary>
    private const string Bubbles = """
        title: Wealth, longevity and population
        xTitle: GDP per head
        yTitle: Life expectancy
        sizeRange: 5 34
        colour: region

        gdp    life  pop   region
        1280   52.9  1032  Africa
        38225  81.7  742   Europe
        9771   76.5  4545  Asia
        14103  75.1  648   Americas
        54225  82.5  41    Oceania
        2104   64.9  1380  Asia
        """;

    /// <summary>A log x-axis, gridlines, and a column feeding shape as well as colour.</summary>
    private const string Scaled = """
        title: Wealth and longevity
        xScale: log
        xTitle: GDP per head
        yTitle: Life expectancy
        colour: region
        shape: region
        grid: both

        gdp    life  region
        1280   52.9  Africa
        2104   64.9  Asia
        38225  81.7  Europe
        9771   76.5  Asia
        14103  75.1  Americas
        54225  82.5  Oceania
        780    59.3  Africa
        3550   71.0  Asia
        22400  78.9  Europe
        6200   74.2  Americas
        430    54.1  Africa
        47800  83.1  Oceania
        """;

    /// <summary>A correlation matrix: the shape anybody writes one in, read down its side as well as across.</summary>
    private const string Correlations = """
        title: How the measures move together
        gradient: rdbu
        midpoint: 0
        fillLimits: -1 1
        labels: true

                mpg    disp    hp     drat   wt     qsec
        mpg     1.00  -0.85  -0.78   0.68  -0.87   0.42
        disp   -0.85   1.00   0.79  -0.71   0.89  -0.43
        hp     -0.78   0.79   1.00  -0.45   0.66  -0.71
        drat    0.68  -0.71  -0.45   1.00  -0.71   0.09
        wt     -0.87   0.89   0.66  -0.71   1.00  -0.17
        qsec    0.42  -0.43  -0.71   0.09  -0.17   1.00
        """;

    /// <summary>A heat map of a long table: a row per cell, its value the colour.</summary>
    private const string Counts = """
        title: Sightings by month
        gradient: viridis

        month  year   count
        Jan    2023   12
        Feb    2023   28
        Mar    2023   45
        Jan    2024   19
        Feb    2024   36
        Mar    2024   61
        Jan    2025   24
        Feb    2025   41
        Mar    2025   77
        """;

    /// <summary>Enough points that drawing them one by one says less than counting them.</summary>
    private static string Crowded(string geom, int bins)
    {
        var lines = new System.Text.StringBuilder();
        lines.AppendLine($"geom: {geom}");
        lines.AppendLine($"bins: {bins}");
        lines.AppendLine("gradient: viridis");
        lines.AppendLine("xTitle: x");
        lines.AppendLine("yTitle: y");
        lines.AppendLine();
        lines.AppendLine("x  y");

        // Two drifts of points, so the counting has something to find.
        var throws = new Random(7);

        for (var which = 0; which < 4000; which++)
        {
            var middle = which % 2 == 0 ? -1.0 : 1.6;

            var x = middle + Normal(throws);
            var y = (middle * 0.7) + Normal(throws) + (x * 0.35);

            lines.AppendLine($"{x:0.####}  {y:0.####}");
        }

        return lines.ToString();
    }

    /// <summary>A throw from a bell curve, by the Box-Muller rule.</summary>
    private static double Normal(Random throws) =>
        Math.Sqrt(-2 * Math.Log(1 - throws.NextDouble())) * Math.Cos(2 * Math.PI * throws.NextDouble());

    /// <summary>The same crowd of points, drawn as how thickly they lie.</summary>
    private static string Clouded(string contour, bool points) =>
        Crowded("density2d", 26)
            .Replace("geom: density2d", $"geom: density2d\ncontour: {contour}\npoints: {points.ToString().ToLowerInvariant()}")
            .Replace("bins: 26\n", string.Empty);

    /// <summary>The settings that used to parse and do nothing: subtitle, caption, key title, label, alpha.</summary>
    private const string Furnished = """
        title: Wealth and longevity
        subtitle: Eight economies, most recent year
        caption: Source: made up for the figure
        legendTitle: Region
        colour: region
        label: place
        alpha: life
        xTitle: GDP per head
        yTitle: Life expectancy

        gdp    life  region    place
        1280   52.9  Africa    Chad
        38225  81.7  Europe    France
        9771   76.5  Asia      China
        14103  75.1  Americas  Brazil
        54225  82.5  Oceania   Australia
        2104   64.9  Asia      India
        780    59.3  Africa    Niger
        22400  78.9  Europe    Poland
        """;

    /// <summary>Rows landing on the same value, with and without being shaken apart.</summary>
    private static string Stacked(double jitter) => $"""
        title: Ratings by month
        jitter: {jitter}
        xTitle: Month
        yTitle: Rating

        month  rating
        Jan    3
        Jan    3
        Jan    4
        Jan    4
        Jan    5
        Feb    4
        Feb    4
        Feb    4
        Feb    5
        Feb    5
        Mar    2
        Mar    3
        Mar    3
        Mar    3
        Mar    4
        """;

    /// <summary>A square panel and swapped axes.</summary>
    private const string Shaped = """
        title: Fuel economy by weight
        aspect: 1
        flip: true
        fit: lm
        group: cyl
        colour: cyl
        xTitle: Weight (lb)
        yTitle: Miles per gallon

        weight  mpg   cyl
        2620    21.0  six
        2320    22.8  four
        3440    18.7  eight
        3570    14.3  eight
        3190    24.4  four
        2200    32.4  four
        1615    30.4  four
        5250    10.4  eight
        3170    15.8  eight
        2770    19.7  six
        3460    18.1  six
        1835    33.9  four
        """;

    /// <summary>A correlation matrix worked out from the observations, rather than written as one.</summary>
    private const string Computed = """
        title: How the measures move together
        geom: corr
        labels: true
        aspect: 1

        mpg   cyl  disp   hp   drat  wt
        21.0  6    160.0  110  3.90  2.620
        21.0  6    160.0  110  3.90  2.875
        22.8  4    108.0  93   3.85  2.320
        21.4  6    258.0  110  3.08  3.215
        18.7  8    360.0  175  3.15  3.440
        18.1  6    225.0  105  2.76  3.460
        14.3  8    360.0  245  3.21  3.570
        24.4  4    146.7  62   3.69  3.190
        22.8  4    140.8  95   3.92  3.150
        19.2  6    167.6  123  3.92  3.440
        17.8  6    167.6  123  3.92  3.440
        16.4  8    275.8  180  3.07  4.070
        17.3  8    275.8  180  3.07  3.730
        15.2  8    275.8  180  3.07  3.780
        10.4  8    472.0  205  2.93  5.250
        10.4  8    460.0  215  3.00  5.424
        14.7  8    440.0  230  3.23  5.345
        32.4  4    78.7   66   4.08  2.200
        30.4  4    75.7   52   4.93  1.615
        33.9  4    71.1   65   4.22  1.835
        21.5  4    120.1  97   3.70  2.465
        15.5  8    318.0  150  2.76  3.520
        15.2  8    304.0  150  3.15  3.435
        13.3  8    350.0  245  3.73  3.840
        19.2  8    400.0  175  3.08  3.845
        27.3  4    79.0   66   4.08  1.935
        26.0  4    120.3  91   4.43  2.140
        30.4  4    95.1   113  3.77  1.513
        15.8  8    351.0  264  4.22  3.170
        19.7  6    145.0  175  3.62  2.770
        15.0  8    301.0  335  3.54  3.570
        21.4  4    121.0  109  4.11  2.780
        """;

    /// <summary>A panel per group, all on the same scales, each with its own fitted line.</summary>
    private const string Split = """
        title: Fuel economy by weight
        facet: cyl
        colour: cyl
        fit: lm
        xTitle: Weight (lb)
        yTitle: Miles per gallon

        weight  mpg   cyl
        2620    21.0  six
        2875    21.0  six
        2320    22.8  four
        3215    21.4  six
        3440    18.7  eight
        3460    18.1  six
        3570    14.3  eight
        3190    24.4  four
        3150    22.8  four
        3440    19.2  six
        4070    16.4  eight
        3730    17.3  eight
        3780    15.2  eight
        5250    10.4  eight
        5424    10.4  eight
        2200    32.4  four
        1615    30.4  four
        1835    33.9  four
        2465    21.5  four
        3520    15.5  eight
        3840    13.3  eight
        1935    27.3  four
        2140    26.0  four
        1513    30.4  four
        3170    15.8  eight
        2770    19.7  six
        3570    15.0  eight
        2780    21.4  four
        """;

    [TestMethod]
    public void WritePlotFigures()
    {
        var folder = Environment.GetEnvironmentVariable("NEXAFLOW_WRITE_FIGURES");
        if (string.IsNullOrEmpty(folder)) Assert.Inconclusive("set NEXAFLOW_WRITE_FIGURES to write the figure");

        Directory.CreateDirectory(folder);

        UiThread.Run(() =>
        {
            Write(Path.Combine(folder, "scatter.png"), Plain, PlotFence.Scatter, StyleFormat.Light,
                              Brushes.White, 640);

                        Write(Path.Combine(folder, "settings-furnished.png"), Furnished, PlotFence.Scatter,
                              StyleFormat.Light, Brushes.White, 640);

                        Write(Path.Combine(folder, "settings-jitter-none.png"), Stacked(0), PlotFence.Scatter,
                              StyleFormat.Light, Brushes.White, 420);

                        Write(Path.Combine(folder, "settings-jitter.png"), Stacked(0.6), PlotFence.Scatter,
                              StyleFormat.Light, Brushes.White, 420);

                        Write(Path.Combine(folder, "settings-shaped.png"), Shaped, PlotFence.Scatter,
                              StyleFormat.Light, Brushes.White, 520);

                        Write(Path.Combine(folder, "scatter-fit.png"),
                              "fit: lm\nse: true\nstats: r r2 n p\n" + Plain,
                              PlotFence.Scatter, StyleFormat.Light, Brushes.White, 640);

                        Write(Path.Combine(folder, "scatter-loess.png"),
                              "fit: loess\nse: true\nstats: r n\nmethod: spearman\n" + Plain,
                              PlotFence.Scatter, StyleFormat.Light, Brushes.White, 640);

            Write(Path.Combine(folder, "scatter-groups.png"), Grouped, PlotFence.Scatter, StyleFormat.Light,
                  Brushes.White, 640);

            Write(Path.Combine(folder, "bubble.png"), Bubbles, PlotFence.Bubble, StyleFormat.Light,
                              Brushes.White, 640);

                        Write(Path.Combine(folder, "scatter-log.png"), Scaled, PlotFence.Scatter, StyleFormat.Light,
                                          Brushes.White, 640);

                                    Write(Path.Combine(folder, "heatmap-correlations.png"), Correlations, PlotFence.Heatmap,
                                          StyleFormat.Light, Brushes.White, 620);

                                    Write(Path.Combine(folder, "heatmap-counts.png"), Counts, PlotFence.Heatmap,
                                                      StyleFormat.Light, Brushes.White, 620);

                                                Write(Path.Combine(folder, "heatmap-hex.png"), Crowded("hex", 26), PlotFence.Heatmap,
                                                      StyleFormat.Light, Brushes.White, 620);

                                                Write(Path.Combine(folder, "heatmap-bin2d.png"), Crowded("bin2d", 24), PlotFence.Heatmap,
                                                                  StyleFormat.Light, Brushes.White, 620);

                                                            Write(Path.Combine(folder, "density-bands.png"), Clouded("bands", points: false),
                                                                  PlotFence.Density2d, StyleFormat.Light, Brushes.White, 620);

                                                            Write(Path.Combine(folder, "density-lines.png"), Clouded("lines", points: true),
                                                                  PlotFence.Density2d, StyleFormat.Light, Brushes.White, 620);

                                                            Write(Path.Combine(folder, "heatmap-computed.png"), Computed, PlotFence.Heatmap,
                                                                  StyleFormat.Light, Brushes.White, 620);

                                                            Write(Path.Combine(folder, "scatter-facets.png"), Split, PlotFence.Scatter,
                                                                  StyleFormat.Light, Brushes.White, 660);
        });
    }

    private static void Write(string path, string source, PlotFence fence, StyleFormat palette,
                              Brush ground, double width)
    {
        var host = new Border
        {
            Background = ground,
            Padding = new Thickness(16),
            Child = PlotBuilder.Element(source, fence, DiagramRenderOptions.For(palette)),
        };

        host.Measure(new Size(width, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight),
                                            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var file = File.Create(path);
        encoder.Save(file);
    }
}
