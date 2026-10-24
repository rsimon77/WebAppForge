using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using WebAppForge.Core.Model;

namespace WebAppForge.Desktop.ViewModels;

/// <summary>A colour rule of a calendar: when the colour field matches, the event gets a CSS colour or a Bootstrap style.</summary>
public sealed class ColorRuleRow : ReportRowBase
{
    public ColorRuleRow(ValueRule model, ReportDesignerBase owner)
        : base(owner)
    {
        Model = model;
    }

    public ValueRule Model { get; }

    public ConditionOperator Operator
    {
        get => Model.Operator;
        set
        {
            if (Write(Model.Operator, value, v => Model.Operator = v, nameof(Operator)))
            {
                OnPropertyChanged(nameof(NeedsValue));
            }
        }
    }

    /// <summary>Empty and not-empty rules compare no value.</summary>
    public bool NeedsValue => Model.Operator is not (ConditionOperator.IsEmpty or ConditionOperator.IsNotEmpty);

    public string Value
    {
        get => Model.Value;
        set => Write(Model.Value, value ?? string.Empty, v => Model.Value = v, nameof(Value));
    }

    /// <summary>A Bootstrap context name (primary, success, ...) or a CSS colour such as #ff8800.</summary>
    public string Style
    {
        get => Model.Style;
        set
        {
            if (value is not null)
            {
                Write(Model.Style, value.Trim(), v => Model.Style = v, nameof(Style));
            }
        }
    }
}

/// <summary>A calendar view that is on or off in the view list.</summary>
public sealed class CalendarViewToggle : ReportRowBase
{
    private readonly CalendarReportViewModel _designer;

    public CalendarViewToggle(CalendarView view, CalendarReportViewModel designer)
        : base(designer)
    {
        View = view;
        _designer = designer;
    }

    public CalendarView View { get; }

    public bool IsIncluded
    {
        get => _designer.Report.Calendar.Views.Contains(View);
        set => _designer.SetViewIncluded(View, value);
    }

    internal void Raise() => OnPropertyChanged(nameof(IsIncluded));
}

/// <summary>Designer of a Calendar report: the date fields, colour rules, popover fields, views and behaviour.</summary>
public sealed class CalendarReportViewModel : ReportDesignerBase
{
    public CalendarReportViewModel(ReportsViewModel owner, ReportDef report)
        : base(owner, report)
    {
        ColorRules = new ModelRowList<ValueRule, ColorRuleRow>(
            report.Calendar.ColorRules,
            this,
            r => new ColorRuleRow(r, this),
            r => r.Model,
            () => new ValueRule { Style = "primary" });
        PopoverFields = new FieldNameList(report.Calendar.PopoverFields, this);
        Filters = new ReportFilterList(report.Calendar.Filters, this);
        foreach (var view in ReportChoices.Views)
        {
            ViewToggles.Add(new CalendarViewToggle(view, this));
        }

        RefreshDefaultViews();
        SetDefaultColorCommand = new RelayCommand<string>(hex => DefaultColor = hex ?? string.Empty);
        SetRuleColorCommand = new RelayCommand<string>(hex =>
        {
            if (ColorRules.Selected is { } rule && hex is not null)
            {
                rule.Style = hex;
            }
        });
    }

    private CalendarSettings Model => Report.Calendar;

    public ModelRowList<ValueRule, ColorRuleRow> ColorRules { get; }

    public FieldNameList PopoverFields { get; }

    public ReportFilterList Filters { get; }

    public ObservableCollection<CalendarViewToggle> ViewToggles { get; } = [];

    /// <summary>The views that are on, for the default view picker.</summary>
    public ObservableCollection<CalendarView> DefaultViewChoices { get; } = [];

    public IReadOnlyList<ReportChoice<int>> Days => ReportChoices.Days;

    public IReadOnlyList<string> StyleNames => ContextStyles.Names;

    public IReadOnlyList<ReportPalette.Swatch> Palette => ReportPalette.Swatches;

    public ICommand SetDefaultColorCommand { get; }

    public ICommand SetRuleColorCommand { get; }

    // ---- field pickers: each offers only the fields that fit ----

    public IReadOnlyList<string> TitleFieldChoices => ReportFieldRules.FieldsFor(Table, FieldRole.Group);

    public IReadOnlyList<string> StartFieldChoices => ReportFieldRules.FieldsFor(Table, FieldRole.Date);

    public IReadOnlyList<string> EndFieldChoices => [ReportFieldRules.NoneLabel, .. ReportFieldRules.FieldsFor(Table, FieldRole.Date)];

    public IReadOnlyList<string> AllDayFieldChoices => [ReportFieldRules.NoneLabel, .. ReportFieldRules.FieldsFor(Table, FieldRole.Boolean)];

    public IReadOnlyList<string> ColorFieldChoices => [ReportFieldRules.NoneLabel, .. ReportFieldRules.FieldsFor(Table, FieldRole.Group)];

    public string TitleField
    {
        get => Model.TitleField;
        set
        {
            if (value is not null)
            {
                Put(Model.TitleField, value, v => Model.TitleField = v);
            }
        }
    }

    public string StartField
    {
        get => Model.StartField;
        set
        {
            if (value is not null)
            {
                Put(Model.StartField, value, v => Model.StartField = v);
            }
        }
    }

    public string EndField
    {
        get => ReportFieldRules.Display(Model.EndField, ReportFieldRules.NoneLabel);
        set
        {
            if (value is not null)
            {
                Put(Model.EndField, ReportFieldRules.Stored(value, ReportFieldRules.NoneLabel), v => Model.EndField = v);
            }
        }
    }

    public string AllDayField
    {
        get => ReportFieldRules.Display(Model.AllDayField, ReportFieldRules.NoneLabel);
        set
        {
            if (value is not null)
            {
                Put(Model.AllDayField, ReportFieldRules.Stored(value, ReportFieldRules.NoneLabel), v => Model.AllDayField = v);
            }
        }
    }

    public string ColorField
    {
        get => ReportFieldRules.Display(Model.ColorField, ReportFieldRules.NoneLabel);
        set
        {
            if (value is not null)
            {
                Put(Model.ColorField, ReportFieldRules.Stored(value, ReportFieldRules.NoneLabel), v => Model.ColorField = v);
            }
        }
    }

    /// <summary>Default event colour: a CSS colour or empty (the theme colour).</summary>
    public string DefaultColor
    {
        get => Model.DefaultColor;
        set => Put(Model.DefaultColor, (value ?? string.Empty).Trim(), v => Model.DefaultColor = v);
    }

    public CalendarView DefaultView
    {
        get => Model.DefaultView;
        set => Put(Model.DefaultView, value, v => Model.DefaultView = v);
    }

    public int FirstDay
    {
        get => Model.FirstDay;
        set
        {
            if (value is >= 0 and <= 6)
            {
                Put(Model.FirstDay, value, v => Model.FirstDay = v);
                OnPropertyChanged(nameof(FirstDayChoice));
            }
        }
    }

    public ReportChoice<int> FirstDayChoice
    {
        get => ReportChoices.Days[Model.FirstDay];
        set
        {
            if (value is not null)
            {
                FirstDay = value.Value;
            }
        }
    }

    public bool ShowWeekends
    {
        get => Model.ShowWeekends;
        set => Put(Model.ShowWeekends, value, v => Model.ShowWeekends = v);
    }

    public bool AllowAdd
    {
        get => Model.AllowAdd;
        set => Put(Model.AllowAdd, value, v => Model.AllowAdd = v);
    }

    public bool AllowOpen
    {
        get => Model.AllowOpen;
        set => Put(Model.AllowOpen, value, v => Model.AllowOpen = v);
    }

    /// <summary>
    /// Turns a view on or off. The list keeps the order of <see cref="CalendarView"/>; the last view cannot be turned off, and
    /// turning off the default view moves the default to the first view that is left.
    /// </summary>
    public void SetViewIncluded(CalendarView view, bool included)
    {
        var views = Model.Views;
        if (included == views.Contains(view))
        {
            return;
        }

        if (!included && views.Count == 1)
        {
            Notice("A calendar needs at least one view.");
            ViewToggles.First(t => t.View == view).Raise();
            return;
        }

        Notice(string.Empty);
        if (included)
        {
            views.Add(view);
            views.Sort();
        }
        else
        {
            views.Remove(view);
            if (Model.DefaultView == view)
            {
                Model.DefaultView = views[0];
            }
        }

        RefreshDefaultViews();
        ViewToggles.First(t => t.View == view).Raise();
        OnPropertyChanged(nameof(DefaultView));
        Changed();
    }

    private void RefreshDefaultViews()
    {
        ReportRowBase.SetItems(DefaultViewChoices, Model.Views.OrderBy(v => v));
        if (!Model.Views.Contains(Model.DefaultView) && Model.Views.Count > 0)
        {
            Model.DefaultView = Model.Views[0];
        }
    }
}
