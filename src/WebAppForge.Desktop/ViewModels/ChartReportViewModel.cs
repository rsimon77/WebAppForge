using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using WebAppForge.Core.Model;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>One series of a chart: its aggregate, caption, colour, axis and the kind override of combination charts.</summary>
public sealed class ChartSeriesRow : ReportRowBase
{
    public ChartSeriesRow(ChartSeries model, ReportDesignerBase owner)
        : base(owner)
    {
        Model = model;
        Value = new AggregateRow(model.Value, owner);
    }

    public ChartSeries Model { get; }

    /// <summary>The aggregate (function, field, format) the series plots.</summary>
    public AggregateRow Value { get; }

    public string Caption
    {
        get => Model.Caption;
        set
        {
            if (Write(Model.Caption, value ?? string.Empty, v => Model.Caption = v, nameof(Caption)))
            {
                OnPropertyChanged(nameof(Text));
            }
        }
    }

    /// <summary>A CSS colour (#rrggbb or a name). Empty takes the colour from the palette.</summary>
    public string Color
    {
        get => Model.Color;
        set => Write(Model.Color, (value ?? string.Empty).Trim(), v => Model.Color = v, nameof(Color));
    }

    public ChartAxis Axis
    {
        get => Model.Axis;
        set => Write(Model.Axis, value, v => Model.Axis = v, nameof(Axis));
    }

    /// <summary>"(chart kind)" or Column, Line or Area (a combination chart).</summary>
    public string KindOverride
    {
        get => Model.Kind is { } kind ? kind.ToString() : ReportChoices.ChartKindLabel;
        set
        {
            if (value is null)
            {
                return;
            }

            ChartKind? kind = value == ReportChoices.ChartKindLabel ? null : ReportFieldRules.CombinationKinds.Cast<ChartKind?>().FirstOrDefault(k => k.ToString() == value);
            if (value != ReportChoices.ChartKindLabel && kind is null)
            {
                return;
            }

            Write(Model.Kind, kind, v => Model.Kind = v, nameof(KindOverride));
        }
    }

    public string Text => string.IsNullOrWhiteSpace(Model.Caption) ? Value.Heading : Model.Caption;

    internal void RaiseText() => OnPropertyChanged(nameof(Text));
}

/// <summary>Designer of a Chart report: kind, category, series, series-by field, titles, legend, labels, height, top N, trend line and filters.</summary>
public sealed class ChartReportViewModel : ReportDesignerBase
{
    public const string PieSeriesWarningText = "Pie, doughnut and polar area charts draw a single series. Only the first series is used; the others are ignored.";

    public ChartReportViewModel(ReportsViewModel owner, ReportDef report)
        : base(owner, report)
    {
        var chart = report.Chart;
        Category = new ReportFieldRow(chart.Category, this);
        Series = new ModelRowList<ChartSeries, ChartSeriesRow>(
            chart.Series,
            this,
            s => new ChartSeriesRow(s, this),
            r => r.Model,
            () => new ChartSeries { Color = ReportPalette.ColorOf(chart.Series.Count) });
        Series.Edited += (_, _) => OnPropertyChanged(nameof(PieSeriesWarning));
        SeriesField = chart.SeriesField is { } by ? new ReportFieldRow(by, this, optional: false) : null;
        Filters = new ReportFilterList(chart.Filters, this);
        SetColorCommand = new RelayCommand<string>(SetSeriesColor);
    }

    private ChartSettings Model => Report.Chart;

    public IReadOnlyList<ChartKindChoice> KindChoices => ChartKindChoice.All;

    public ReportFieldRow Category { get; }

    public ModelRowList<ChartSeries, ChartSeriesRow> Series { get; }

    public ReportFilterList Filters { get; }

    /// <summary>The "series by" field, or null while the chart uses its listed series.</summary>
    public ReportFieldRow? SeriesField { get; private set; }

    public IReadOnlyList<ReportPalette.Swatch> Palette => ReportPalette.Swatches;

    public ICommand SetColorCommand { get; }

    public ChartKind Kind
    {
        get => Model.Kind;
        set
        {
            if (Put(Model.Kind, value, v => Model.Kind = v))
            {
                OnPropertyChanged(nameof(KindChoice));
                OnPropertyChanged(nameof(PieSeriesWarning));
                OnPropertyChanged(nameof(HasPieSeriesWarning));
                OnPropertyChanged(nameof(CanCombine));
            }
        }
    }

    public ChartKindChoice KindChoice
    {
        get => ChartKindChoice.Of(Model.Kind);
        set
        {
            if (value is not null)
            {
                Kind = value.Kind;
            }
        }
    }

    /// <summary>A kind override only makes sense where the chart can mix bars and lines.</summary>
    public bool CanCombine => Model.Kind is ChartKind.Column or ChartKind.Bar or ChartKind.Line or ChartKind.Area or ChartKind.StackedColumn;

    public IReadOnlyList<string> SeriesKindChoices => ReportChoices.SeriesKindChoices;

    /// <summary>Warns when a single-series kind has more than one series.</summary>
    public string PieSeriesWarning
        => ReportFieldRules.IsSingleSeriesKind(Model.Kind) && Model.Series.Count > 1 ? PieSeriesWarningText : string.Empty;

    public bool HasPieSeriesWarning => PieSeriesWarning.Length > 0;

    /// <summary>One series per distinct value of a field (the first listed series is the value).</summary>
    public bool UseSeriesField
    {
        get => Model.SeriesField is not null;
        set
        {
            if (value == (Model.SeriesField is not null))
            {
                return;
            }

            if (value)
            {
                var first = ReportFieldRules.FieldsFor(Table, FieldRole.Group).FirstOrDefault(n => n != Model.Category.Field) ?? string.Empty;
                Model.SeriesField = new ReportField { Field = first };
                SeriesField = new ReportFieldRow(Model.SeriesField, this);
            }
            else
            {
                Model.SeriesField = null;
                SeriesField = null;
            }

            OnPropertyChanged(nameof(UseSeriesField));
            OnPropertyChanged(nameof(SeriesField));
            Changed();
        }
    }

    public string Title
    {
        get => Model.Title;
        set => Put(Model.Title, value ?? string.Empty, v => Model.Title = v);
    }

    public LegendPosition Legend
    {
        get => Model.Legend;
        set => Put(Model.Legend, value, v => Model.Legend = v);
    }

    public bool ShowDataLabels
    {
        get => Model.ShowDataLabels;
        set => Put(Model.ShowDataLabels, value, v => Model.ShowDataLabels = v);
    }

    public string HeightText
    {
        get => Shown(nameof(HeightText), Model.Height.ToString(CultureInfo.InvariantCulture));
        set => AcceptWhole(nameof(HeightText), value, "Height", 100, 2000, n => Model.Height = n);
    }

    public string TopNText
    {
        get => Shown(nameof(TopNText), Model.TopN.ToString(CultureInfo.InvariantCulture));
        set => AcceptWhole(nameof(TopNText), value, "Top N", 0, 1000, n => Model.TopN = n);
    }

    public bool TrendLine
    {
        get => Model.TrendLine;
        set => Put(Model.TrendLine, value, v => Model.TrendLine = v);
    }

    public string XAxisTitle
    {
        get => Model.XAxisTitle;
        set => Put(Model.XAxisTitle, value ?? string.Empty, v => Model.XAxisTitle = v);
    }

    public string YAxisTitle
    {
        get => Model.YAxisTitle;
        set => Put(Model.YAxisTitle, value ?? string.Empty, v => Model.YAxisTitle = v);
    }

    /// <summary>Sets the colour of the selected series (a palette swatch was clicked).</summary>
    public void SetSeriesColor(string? hex)
    {
        if (Series.Selected is { } row && hex is not null)
        {
            row.Color = hex;
        }
    }

    protected override void Changed()
    {
        foreach (var row in Series.Rows)
        {
            row.RaiseText();
        }

        OnPropertyChanged(nameof(PieSeriesWarning));
        OnPropertyChanged(nameof(HasPieSeriesWarning));
        base.Changed();
    }
}
