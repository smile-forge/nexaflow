using System.Text;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// CSV / TSV fixtures spanning the variations the shape detector must handle: different
/// separators (comma, tab, semicolon, comma+space), quoting, headerless data, a single
/// numeric column, mixed column types (integer / decimal / boolean / date / datetime / string),
/// and one deliberately long file for the windowed streaming readers.
///
/// Shapes are inspired by real-world samples but authored from scratch so the expected
/// detection result is unambiguous and pinned by <c>SampleFileDetectionTests</c>.
/// </summary>
internal sealed class TabularSamples : ISampleSet
{
    public string SubDirectory => "tabular";

    public IReadOnlyList<SampleFile> Files { get; } =
    [
        SampleFile.Text("comma_header.csv",       CommaHeader),
        SampleFile.Text("tab_no_header.tsv",      TabNoHeader),
        SampleFile.Text("semicolon_dates.csv",    SemicolonDates),
        SampleFile.Text("single_column.csv",      SingleColumn),
        SampleFile.Text("comma_space_header.csv", CommaSpaceHeader),
        SampleFile.Text("quoted_fields.csv",      QuotedFields),
        SampleFile.Text("iso_dates.csv",          IsoDates),
        SampleFile.Text("comma_long.csv",         BuildLong()),
    ];

    /// <summary>Comma-separated, header, columns: Integer / String / Decimal / Boolean.</summary>
    private const string CommaHeader =
        """
        id,name,price,in_stock
        1,Widget,9.99,true
        2,Gadget,19.50,false
        3,Gizmo,4.00,yes
        4,Sprocket,12.34,no
        5,Cog,7.77,true
        6,Lever,3.50,false
        7,Pulley,8.10,true
        8,Spring,1.25,no

        """;

    /// <summary>Tab-separated, two string columns, no header (symbol → doc-anchor map).</summary>
    private const string TabNoHeader =
        "Pathfinding.ABPath.calculatePartial\tclass_abpath.html#a0db2411\n" +
        "Pathfinding.ABPath.endNode\tclass_abpath.html#af18b638\n" +
        "Pathfinding.ABPath.endPoint\tclass_abpath.html#ab08561d\n" +
        "Pathfinding.ABPath.originalEndPoint\tclass_abpath.html#a5e07633\n" +
        "Pathfinding.ABPath.heuristicScale\tclass_abpath.html#a5e07634\n" +
        "Pathfinding.Seeker.StartPath\tclass_seeker.html#a77c0aa1\n" +
        "Pathfinding.Seeker.CancelCurrentPathRequest\tclass_seeker.html#a77c0aa2\n" +
        "Pathfinding.GraphNode.Walkable\tclass_graphnode.html#a77c0aa3\n";

    /// <summary>Semicolon-separated, header, an integer id, a dd.MM.yyyy HH:mm:ss DateTime column,
    /// a decimal price, plus a trailing empty column. No commas anywhere so comma never scores.</summary>
    private const string SemicolonDates =
        """
        AboID;SubID;Customer;State;StartDateTime;Price;EndTime
        129154831;131574227;111411849;active;21.05.2004 15:32:18;2.60;
        129154831;133296156;111448699;active;28.05.2004 12:32:31;2.60;
        129154831;133324194;111440246;active;28.05.2004 14:39:04;3.10;
        129154831;133324195;111440247;inactive;29.05.2004 09:10:00;2.60;
        129154831;133324196;111440248;active;30.05.2004 18:45:12;5.00;
        129154831;133324197;111440249;active;31.05.2004 07:05:44;2.60;

        """;

    /// <summary>Single numeric column, no separator — mixes integers and decimals.</summary>
    private const string SingleColumn =
        """
        101
        93.16
        85.64
        78.44
        71.56
        65.00
        58.76
        52.84
        47.24

        """;

    /// <summary>Comma+space separated, header, many integer columns plus one string (player name).</summary>
    private const string CommaSpaceHeader =
        """
        Turn, Player, Cities, Pop, Science, Culture, Gold, Faith, Production, Food, Units, Tiles
        196, Greece, 2, 9, 8, 7, 7, 0, 20, 24, 0, 20
        196, France, 2, 9, 10, 9, 8, 0, 23, 21, 0, 20
        196, Egypt, 2, 9, 9, 8, 8, 1, 24, 20, 0, 20
        197, Greece, 2, 10, 8, 7, 7, 0, 21, 25, 1, 21
        197, France, 3, 11, 11, 9, 9, 0, 24, 22, 1, 22
        197, Egypt, 2, 10, 9, 8, 8, 1, 25, 21, 0, 21

        """;

    /// <summary>Comma-separated, header, double-quoted fields containing commas and escaped quotes.</summary>
    private const string QuotedFields =
        """
        sku,description,price
        "A-1","Bolt, hex, 10mm",0.45
        "B-2","Washer, flat",0.10
        "C-3","Nut, ""lock"" type",0.25
        "D-4","Screw, pan head",0.60
        "E-5","Bracket, L-shaped",1.20
        "F-6","Spacer, nylon",0.05

        """;

    /// <summary>Comma-separated, header, ISO date column (yyyy-MM-dd) plus two decimal columns.</summary>
    private const string IsoDates =
        """
        Date,Value,Average
        2000-01-01,59.2,63.14458997
        2000-02-01,58.9,59.57128548
        2000-03-01,58.4,61.79357897
        2000-04-01,60.1,60.02114558
        2000-05-01,61.7,62.33841200
        2000-06-01,62.0,61.10298471

        """;

    /// <summary>
    /// A long comma file (header + 5000 rows) so the windowed streaming readers
    /// (<c>RowWindowReader</c>) scan well past a single display window. Generated deterministically
    /// so regeneration is churn-free.
    /// </summary>
    private static string BuildLong()
    {
        var sb = new StringBuilder(5000 * 24);
        sb.Append("row,value,category\n");
        string[] categories = ["alpha", "beta", "gamma", "delta"];
        for (int i = 0; i < 5000; i++)
        {
            // Deterministic pseudo-value — no Random, so the file is identical every run.
            int value = (i * 7919) % 100000;
            sb.Append(i).Append(',').Append(value).Append(',').Append(categories[i % categories.Length]).Append('\n');
        }
        return sb.ToString();
    }
}
