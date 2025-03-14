﻿
// The MIT License(MIT)
//
// Copyright(c) 2021 Alberto Rodriguez Orozco & LiveCharts Contributors
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Events;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;
using LiveChartsCore.Motion;
using LiveChartsCore.SkiaSharpView.Drawing;
using LiveChartsCore.SkiaSharpView.Drawing.Geometries;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.SKCharts;
using LiveChartsCore.VisualElements;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using SkiaSharp.Views.Maui;

namespace LiveChartsCore.SkiaSharpView.Maui;

/// <inheritdoc cref="ICartesianChartView{TDrawingContext}"/>
[XamlCompilation(XamlCompilationOptions.Compile)]
public partial class CartesianChart : ContentView, ICartesianChartView<SkiaSharpDrawingContext>
{
    #region fields

    private Chart<SkiaSharpDrawingContext>? _core;
    private readonly CollectionDeepObserver<ISeries> _seriesObserver;
    private readonly CollectionDeepObserver<ICartesianAxis> _xObserver;
    private readonly CollectionDeepObserver<ICartesianAxis> _yObserver;
    private readonly CollectionDeepObserver<Section<SkiaSharpDrawingContext>> _sectionsObserver;
    private readonly CollectionDeepObserver<ChartElement<SkiaSharpDrawingContext>> _visualsObserver;
    private double _lastScale = 0;
    private DateTime _panLocketUntil;
    private double _lastPanX = 0;
    private double _lastPanY = 0;
    private IChartLegend<SkiaSharpDrawingContext>? _legend = new SKDefaultLegend();
    private IChartTooltip<SkiaSharpDrawingContext>? _tooltip = new SKDefaultTooltip();

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="CartesianChart"/> class.
    /// </summary>
    /// <exception cref="Exception">Default colors are not valid</exception>
    public CartesianChart()
    {
        InitializeComponent();

        if (!LiveCharts.IsConfigured) LiveCharts.Configure(LiveChartsSkiaSharp.DefaultPlatformBuilder);

        InitializeCore();
        SizeChanged += OnSizeChanged;

        _seriesObserver = new CollectionDeepObserver<ISeries>(OnDeepCollectionChanged, OnDeepCollectionPropertyChanged, true);
        _xObserver = new CollectionDeepObserver<ICartesianAxis>(OnDeepCollectionChanged, OnDeepCollectionPropertyChanged, true);
        _yObserver = new CollectionDeepObserver<ICartesianAxis>(OnDeepCollectionChanged, OnDeepCollectionPropertyChanged, true);
        _sectionsObserver = new CollectionDeepObserver<Section<SkiaSharpDrawingContext>>(
            OnDeepCollectionChanged, OnDeepCollectionPropertyChanged, true);
        _visualsObserver = new CollectionDeepObserver<ChartElement<SkiaSharpDrawingContext>>(
            OnDeepCollectionChanged, OnDeepCollectionPropertyChanged, true);

        XAxes = new List<ICartesianAxis>()
            {
                LiveCharts.DefaultSettings.GetProvider<SkiaSharpDrawingContext>().GetDefaultCartesianAxis()
            };
        YAxes = new List<ICartesianAxis>()
            {
                LiveCharts.DefaultSettings.GetProvider<SkiaSharpDrawingContext>().GetDefaultCartesianAxis()
            };
        Series = new ObservableCollection<ISeries>();
        VisualElements = new ObservableCollection<ChartElement<SkiaSharpDrawingContext>>();

        canvas.SkCanvasView.EnableTouchEvents = true;
        canvas.SkCanvasView.Touch += OnSkCanvasTouched;

        if (_core is null) throw new Exception("Core not found!");
        _core.Measuring += OnCoreMeasuring;
        _core.UpdateStarted += OnCoreUpdateStarted;
        _core.UpdateFinished += OnCoreUpdateFinished;
    }

    #region bindable properties 

    /// <summary>
    /// The sync context property.
    /// </summary>
    public static readonly BindableProperty SyncContextProperty =
        BindableProperty.Create(
            nameof(SyncContext), typeof(object), typeof(CartesianChart), new ObservableCollection<ISeries>(), BindingMode.Default, null,
            (BindableObject o, object oldValue, object newValue) =>
            {
                var chart = (CartesianChart)o;
                chart.CoreCanvas.Sync = newValue;
                if (chart._core is null) return;
                chart._core.Update();
            });

    /// <summary>
    /// The title property.
    /// </summary>
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(
            nameof(Title), typeof(VisualElement<SkiaSharpDrawingContext>), typeof(CartesianChart), null, BindingMode.Default, null);

    /// <summary>
    /// The series property.
    /// </summary>
    public static readonly BindableProperty SeriesProperty =
        BindableProperty.Create(
            nameof(Series), typeof(IEnumerable<ISeries>), typeof(CartesianChart), new ObservableCollection<ISeries>(), BindingMode.Default, null,
            (BindableObject o, object oldValue, object newValue) =>
            {
                var chart = (CartesianChart)o;
                var seriesObserver = chart._seriesObserver;
                seriesObserver?.Dispose((IEnumerable<ISeries>)oldValue);
                seriesObserver?.Initialize((IEnumerable<ISeries>)newValue);
                if (chart._core is null) return;
                chart._core.Update();
            });

    /// <summary>
    /// The x axes property
    /// </summary>
    public static readonly BindableProperty XAxesProperty =
        BindableProperty.Create(
            nameof(XAxes), typeof(IEnumerable<ICartesianAxis>), typeof(CartesianChart), new List<ICartesianAxis>() { new Axis() },
            BindingMode.Default, null, (BindableObject o, object oldValue, object newValue) =>
            {
                var chart = (CartesianChart)o;
                var observer = chart._xObserver;
                observer?.Dispose((IEnumerable<ICartesianAxis>)oldValue);
                observer?.Initialize((IEnumerable<ICartesianAxis>)newValue);
                if (chart._core is null) return;
                chart._core.Update();
            });

    /// <summary>
    /// The y axes property.
    /// </summary>
    public static readonly BindableProperty YAxesProperty =
        BindableProperty.Create(
            nameof(YAxes), typeof(IEnumerable<ICartesianAxis>), typeof(CartesianChart), new List<ICartesianAxis>() { new Axis() },
            BindingMode.Default, null, (BindableObject o, object oldValue, object newValue) =>
            {
                var chart = (CartesianChart)o;
                var observer = chart._yObserver;
                observer?.Dispose((IEnumerable<ICartesianAxis>)oldValue);
                observer?.Initialize((IEnumerable<ICartesianAxis>)newValue);
                if (chart._core is null) return;
                chart._core.Update();
            });

    /// <summary>
    /// The sections property.
    /// </summary>
    public static readonly BindableProperty SectionsProperty =
        BindableProperty.Create(
            nameof(Sections), typeof(IEnumerable<Section<SkiaSharpDrawingContext>>), typeof(CartesianChart), new List<Section<SkiaSharpDrawingContext>>(),
            BindingMode.Default, null, (BindableObject o, object oldValue, object newValue) =>
            {
                var chart = (CartesianChart)o;
                var observer = chart._sectionsObserver;
                observer?.Dispose((IEnumerable<Section<SkiaSharpDrawingContext>>)oldValue);
                observer?.Initialize((IEnumerable<Section<SkiaSharpDrawingContext>>)newValue);
                if (chart._core is null) return;
                chart._core.Update();
            });

    /// <summary>
    /// The visual elements property.
    /// </summary>
    public static readonly BindableProperty VisualElementsProperty =
        BindableProperty.Create(
            nameof(VisualElements), typeof(IEnumerable<ChartElement<SkiaSharpDrawingContext>>), typeof(CartesianChart), new List<ChartElement<SkiaSharpDrawingContext>>(),
            BindingMode.Default, null, (BindableObject o, object oldValue, object newValue) =>
            {
                var chart = (CartesianChart)o;
                var observer = chart._visualsObserver;
                observer?.Dispose((IEnumerable<ChartElement<SkiaSharpDrawingContext>>)oldValue);
                observer?.Initialize((IEnumerable<ChartElement<SkiaSharpDrawingContext>>)newValue);
                if (chart._core is null) return;
                chart._core.Update();
            });

    /// <summary>
    /// The draw margin frame property.
    /// </summary>
    public static readonly BindableProperty DrawMarginFrameProperty =
        BindableProperty.Create(
            nameof(DrawMarginFrame), typeof(DrawMarginFrame<SkiaSharpDrawingContext>), typeof(CartesianChart), null,
            BindingMode.Default, null, OnBindablePropertyChanged);

    /// <summary>
    /// The draw margin property.
    /// </summary>
    public static readonly BindableProperty DrawMarginProperty =
        BindableProperty.Create(
            nameof(DrawMargin), typeof(Margin), typeof(CartesianChart), null, BindingMode.Default, null, OnBindablePropertyChanged);

    /// <summary>
    /// The zoom mode property.
    /// </summary>
    public static readonly BindableProperty ZoomModeProperty =
        BindableProperty.Create(
            nameof(ZoomMode), typeof(ZoomAndPanMode), typeof(CartesianChart),
            LiveCharts.DefaultSettings.ZoomMode, BindingMode.Default, null);

    /// <summary>
    /// The zooming speed property.
    /// </summary>
    public static readonly BindableProperty ZoomingSpeedProperty =
        BindableProperty.Create(
            nameof(ZoomingSpeed), typeof(double), typeof(CartesianChart),
            LiveCharts.DefaultSettings.ZoomSpeed, BindingMode.Default, null);

    /// <summary>
    /// The animations speed property.
    /// </summary>
    public static readonly BindableProperty AnimationsSpeedProperty =
       BindableProperty.Create(
           nameof(AnimationsSpeed), typeof(TimeSpan), typeof(CartesianChart), LiveCharts.DefaultSettings.AnimationsSpeed);

    /// <summary>
    /// The easing function property.
    /// </summary>
    public static readonly BindableProperty EasingFunctionProperty =
        BindableProperty.Create(
            nameof(EasingFunction), typeof(Func<float, float>), typeof(CartesianChart),
            LiveCharts.DefaultSettings.EasingFunction);

    /// <summary>
    /// The legend position property.
    /// </summary>
    public static readonly BindableProperty LegendPositionProperty =
        BindableProperty.Create(
            nameof(LegendPosition), typeof(LegendPosition), typeof(CartesianChart),
            LiveCharts.DefaultSettings.LegendPosition, propertyChanged: OnBindablePropertyChanged);

    /// <summary>
    /// The legend background property.
    /// </summary>
    public static readonly BindableProperty LegendBackgroundPaintProperty =
        BindableProperty.Create(
            nameof(LegendBackgroundPaint), typeof(IPaint<SkiaSharpDrawingContext>), typeof(CartesianChart),
            LiveCharts.DefaultSettings.LegendBackgroundPaint, propertyChanged: OnBindablePropertyChanged);

    /// <summary>
    /// The legend text paint property.
    /// </summary>
    public static readonly BindableProperty LegendTextPaintProperty =
        BindableProperty.Create(
            nameof(LegendTextPaint), typeof(IPaint<SkiaSharpDrawingContext>), typeof(CartesianChart),
            LiveCharts.DefaultSettings.LegendTextPaint, propertyChanged: OnBindablePropertyChanged);

    /// <summary>
    /// The legend text size property.
    /// </summary>
    public static readonly BindableProperty LegendTextSizeProperty =
        BindableProperty.Create(
            nameof(LegendTextSize), typeof(double?), typeof(CartesianChart),
            LiveCharts.DefaultSettings.LegendTextSize, propertyChanged: OnBindablePropertyChanged);

    /// <summary>
    /// The tool tip position property.
    /// </summary>
    public static readonly BindableProperty TooltipPositionProperty =
       BindableProperty.Create(
           nameof(TooltipPosition), typeof(TooltipPosition), typeof(CartesianChart),
           LiveCharts.DefaultSettings.TooltipPosition, propertyChanged: OnBindablePropertyChanged);

    /// <summary>
    /// The tool tip finding strategy property.
    /// </summary>
    public static readonly BindableProperty TooltipFindingStrategyProperty =
        BindableProperty.Create(
            nameof(TooltipFindingStrategy), typeof(TooltipFindingStrategy), typeof(CartesianChart),
            LiveCharts.DefaultSettings.TooltipFindingStrategy);

    /// <summary>
    /// The tooltip background property.
    /// </summary>
    public static readonly BindableProperty TooltipBackgroundPaintProperty =
        BindableProperty.Create(
            nameof(TooltipBackgroundPaint), typeof(IPaint<SkiaSharpDrawingContext>), typeof(CartesianChart),
            LiveCharts.DefaultSettings.TooltipBackgroundPaint, propertyChanged: OnBindablePropertyChanged);

    /// <summary>
    /// The tooltip text paint property.
    /// </summary>
    public static readonly BindableProperty TooltipTextPaintProperty =
        BindableProperty.Create(
            nameof(TooltipTextPaint), typeof(IPaint<SkiaSharpDrawingContext>), typeof(CartesianChart),
            LiveCharts.DefaultSettings.TooltipTextPaint, propertyChanged: OnBindablePropertyChanged);

    /// <summary>
    /// The tooltip text size property.
    /// </summary>
    public static readonly BindableProperty TooltipTextSizeProperty =
        BindableProperty.Create(
            nameof(TooltipTextSize), typeof(double?), typeof(CartesianChart),
            LiveCharts.DefaultSettings.TooltipTextSize, propertyChanged: OnBindablePropertyChanged);

    /// <summary>
    /// The data pointer down command property
    /// </summary>
    public static readonly BindableProperty DataPointerDownCommandProperty =
        BindableProperty.Create(
            nameof(DataPointerDownCommand), typeof(ICommand), typeof(CartesianChart),
            null, propertyChanged: OnBindablePropertyChanged);

    /// <summary>
    /// The chart point pointer down command property
    /// </summary>
    public static readonly BindableProperty ChartPointPointerDownCommandProperty =
        BindableProperty.Create(
            nameof(ChartPointPointerDownCommand), typeof(ICommand), typeof(CartesianChart),
            null, propertyChanged: OnBindablePropertyChanged);

    /// <summary>
    /// The visual elements pointer down command property
    /// </summary>
    public static readonly BindableProperty VisualElementsPointerDownCommandProperty =
        BindableProperty.Create(
            nameof(VisualElementsPointerDownCommand), typeof(ICommand), typeof(CartesianChart),
            null, propertyChanged: OnBindablePropertyChanged);

    #endregion

    #region events

    /// <inheritdoc cref="IChartView{TDrawingContext}.Measuring" />
    public event ChartEventHandler<SkiaSharpDrawingContext>? Measuring;

    /// <inheritdoc cref="IChartView{TDrawingContext}.UpdateStarted" />
    public event ChartEventHandler<SkiaSharpDrawingContext>? UpdateStarted;

    /// <inheritdoc cref="IChartView{TDrawingContext}.UpdateFinished" />
    public event ChartEventHandler<SkiaSharpDrawingContext>? UpdateFinished;

    /// <inheritdoc cref="IChartView.DataPointerDown" />
    public event ChartPointsHandler? DataPointerDown;

    /// <inheritdoc cref="IChartView.ChartPointPointerDown" />
    public event ChartPointHandler? ChartPointPointerDown;

    /// <inheritdoc cref="IChartView{TDrawingContext}.VisualElementsPointerDown"/>
    public event VisualElementHandler<SkiaSharpDrawingContext>? VisualElementsPointerDown;

    /// <summary>
    /// Called when the chart is touched.
    /// </summary>
    public event EventHandler<SKTouchEventArgs>? Touched;

    #endregion

    #region properties

    /// <inheritdoc cref="IChartView.DesignerMode" />
    bool IChartView.DesignerMode => DesignMode.IsDesignModeEnabled;

    /// <inheritdoc cref="IChartView.CoreChart" />
    public IChart CoreChart => _core ?? throw new Exception("Core not set yet.");

    LvcColor IChartView.BackColor
    {
        get => Background is not SolidColorBrush b
            ? new LvcColor()
            : LvcColor.FromArgb(
                (byte)(b.Color.Alpha * 255), (byte)(b.Color.Red * 255),
                (byte)(b.Color.Green * 255), (byte)(b.Color.Blue * 255));
        set => Background = new SolidColorBrush(Color.FromRgba(value.R / 255, value.G / 255, value.B / 255, value.A / 255));
    }

    CartesianChart<SkiaSharpDrawingContext> ICartesianChartView<SkiaSharpDrawingContext>.Core => _core is null ? throw new Exception("core not found") : (CartesianChart<SkiaSharpDrawingContext>)_core;

    LvcSize IChartView.ControlSize => new()
    {
        Width = (float)(canvas.Width * DeviceDisplay.MainDisplayInfo.Density),
        Height = (float)(canvas.Height * DeviceDisplay.MainDisplayInfo.Density)
    };

    /// <inheritdoc cref="IChartView{TDrawingContext}.CoreCanvas" />
    public MotionCanvas<SkiaSharpDrawingContext> CoreCanvas => canvas.CanvasCore;

    /// <inheritdoc cref="IChartView.SyncContext" />
    public object SyncContext
    {
        get => GetValue(SyncContextProperty);
        set => SetValue(SyncContextProperty, value);
    }

    /// <inheritdoc cref="IChartView.DrawMargin" />
    public Margin? DrawMargin
    {
        get => (Margin)GetValue(DrawMarginProperty);
        set => SetValue(DrawMarginProperty, value);
    }

    /// <inheritdoc cref="IChartView{TDrawingContext}.Title" />
    public VisualElement<SkiaSharpDrawingContext>? Title
    {
        get => (VisualElement<SkiaSharpDrawingContext>?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <inheritdoc cref="ICartesianChartView{TDrawingContext}.Series" />
    public IEnumerable<ISeries> Series
    {
        get => (IEnumerable<ISeries>)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    /// <inheritdoc cref="ICartesianChartView{TDrawingContext}.XAxes" />
    public IEnumerable<ICartesianAxis> XAxes
    {
        get => (IEnumerable<ICartesianAxis>)GetValue(XAxesProperty);
        set => SetValue(XAxesProperty, value);
    }

    /// <inheritdoc cref="ICartesianChartView{TDrawingContext}.YAxes" />
    public IEnumerable<ICartesianAxis> YAxes
    {
        get => (IEnumerable<ICartesianAxis>)GetValue(YAxesProperty);
        set => SetValue(YAxesProperty, value);
    }

    /// <inheritdoc cref="ICartesianChartView{TDrawingContext}.Sections" />
    public IEnumerable<Section<SkiaSharpDrawingContext>> Sections
    {
        get => (IEnumerable<Section<SkiaSharpDrawingContext>>)GetValue(SectionsProperty);
        set => SetValue(SectionsProperty, value);
    }

    /// <inheritdoc cref="IChartView{TDrawingContext}.VisualElements" />
    public IEnumerable<ChartElement<SkiaSharpDrawingContext>> VisualElements
    {
        get => (IEnumerable<ChartElement<SkiaSharpDrawingContext>>)GetValue(VisualElementsProperty);
        set => SetValue(VisualElementsProperty, value);
    }

    /// <inheritdoc cref="ICartesianChartView{TDrawingContext}.DrawMarginFrame" />
    public DrawMarginFrame<SkiaSharpDrawingContext>? DrawMarginFrame
    {
        get => (DrawMarginFrame<SkiaSharpDrawingContext>)GetValue(DrawMarginFrameProperty);
        set => SetValue(DrawMarginFrameProperty, value);
    }

    /// <inheritdoc cref="IChartView.AnimationsSpeed" />
    public TimeSpan AnimationsSpeed
    {
        get => (TimeSpan)GetValue(AnimationsSpeedProperty);
        set => SetValue(AnimationsSpeedProperty, value);
    }

    /// <inheritdoc cref="IChartView.EasingFunction" />
    public Func<float, float>? EasingFunction
    {
        get => (Func<float, float>)GetValue(EasingFunctionProperty);
        set => SetValue(EasingFunctionProperty, value);
    }

    /// <inheritdoc cref="ICartesianChartView{TDrawingContext}.ZoomMode" />
    public ZoomAndPanMode ZoomMode
    {
        get => (ZoomAndPanMode)GetValue(ZoomModeProperty);
        set => SetValue(ZoomModeProperty, value);
    }

    /// <inheritdoc cref="ICartesianChartView{TDrawingContext}.ZoomingSpeed" />
    public double ZoomingSpeed
    {
        get => (double)GetValue(ZoomingSpeedProperty);
        set => SetValue(ZoomingSpeedProperty, value);
    }

    /// <inheritdoc cref="IChartView.LegendPosition" />
    public LegendPosition LegendPosition
    {
        get => (LegendPosition)GetValue(LegendPositionProperty);
        set => SetValue(LegendPositionProperty, value);
    }

    /// <inheritdoc cref="IChartView{TDrawingContext}.LegendBackgroundPaint" />
    public IPaint<SkiaSharpDrawingContext>? LegendBackgroundPaint
    {
        get => (IPaint<SkiaSharpDrawingContext>?)GetValue(LegendBackgroundPaintProperty);
        set => SetValue(LegendBackgroundPaintProperty, value);
    }

    /// <inheritdoc cref="IChartView{TDrawingContext}.LegendTextPaint" />
    public IPaint<SkiaSharpDrawingContext>? LegendTextPaint
    {
        get => (IPaint<SkiaSharpDrawingContext>?)GetValue(LegendTextPaintProperty);
        set => SetValue(LegendTextPaintProperty, value);
    }

    /// <inheritdoc cref="IChartView{TDrawingContext}.LegendTextSize" />
    public double? LegendTextSize
    {
        get => (double?)GetValue(LegendTextSizeProperty);
        set => SetValue(LegendTextSizeProperty, value);
    }

    /// <inheritdoc cref="IChartView{TDrawingContext}.Legend" />
    public IChartLegend<SkiaSharpDrawingContext>? Legend { get => _legend; set { _legend = value; OnPropertyChanged(); } }

    /// <inheritdoc cref="IChartView.TooltipPosition" />
    public TooltipPosition TooltipPosition
    {
        get => (TooltipPosition)GetValue(TooltipPositionProperty);
        set => SetValue(TooltipPositionProperty, value);
    }

    /// <inheritdoc cref="ICartesianChartView{TDrawingContext}.TooltipFindingStrategy" />
    public TooltipFindingStrategy TooltipFindingStrategy
    {
        get => (TooltipFindingStrategy)GetValue(TooltipFindingStrategyProperty);
        set => SetValue(TooltipFindingStrategyProperty, value);
    }

    /// <inheritdoc cref="IChartView{TDrawingContext}.TooltipBackgroundPaint" />
    public IPaint<SkiaSharpDrawingContext>? TooltipBackgroundPaint
    {
        get => (IPaint<SkiaSharpDrawingContext>?)GetValue(TooltipBackgroundPaintProperty);
        set => SetValue(TooltipBackgroundPaintProperty, value);
    }

    /// <inheritdoc cref="IChartView{TDrawingContext}.TooltipTextPaint" />
    public IPaint<SkiaSharpDrawingContext>? TooltipTextPaint
    {
        get => (IPaint<SkiaSharpDrawingContext>?)GetValue(TooltipTextPaintProperty);
        set => SetValue(TooltipTextPaintProperty, value);
    }

    /// <inheritdoc cref="IChartView{TDrawingContext}.TooltipTextSize" />
    public double? TooltipTextSize
    {
        get => (double?)GetValue(TooltipTextSizeProperty);
        set => SetValue(TooltipTextSizeProperty, value);
    }

    /// <inheritdoc cref="IChartView{TDrawingContext}.Tooltip" />
    public IChartTooltip<SkiaSharpDrawingContext>? Tooltip { get => _tooltip; set { _tooltip = value; OnPropertyChanged(); } }

    /// <inheritdoc cref="IChartView{TDrawingContext}.AutoUpdateEnabled" />
    public bool AutoUpdateEnabled { get; set; } = true;

    /// <inheritdoc cref="IChartView.UpdaterThrottler" />
    public TimeSpan UpdaterThrottler { get; set; } = LiveCharts.DefaultSettings.UpdateThrottlingTimeout;

    /// <summary>
    /// Gets or sets a command to execute when the pointer goes down on a data or data points.
    /// </summary>
    public ICommand? DataPointerDownCommand
    {
        get => (ICommand?)GetValue(DataPointerDownCommandProperty);
        set => SetValue(DataPointerDownCommandProperty, value);
    }

    /// <summary>
    /// Gets or sets a command to execute when the pointer goes down on a chart point.
    /// </summary>
    public ICommand? ChartPointPointerDownCommand
    {
        get => (ICommand?)GetValue(ChartPointPointerDownCommandProperty);
        set => SetValue(ChartPointPointerDownCommandProperty, value);
    }

    /// <summary>
    /// Gets or sets a command to execute when the pointer goes down on a chart point.
    /// </summary>
    public ICommand? VisualElementsPointerDownCommand
    {
        get => (ICommand?)GetValue(VisualElementsPointerDownCommandProperty);
        set => SetValue(VisualElementsPointerDownCommandProperty, value);
    }

    #endregion

    /// <inheritdoc cref="ICartesianChartView{TDrawingContext}.ScaleUIPoint(LvcPoint, int, int)" />
    [Obsolete("Use the ScalePixelsToData method instead.")]
    public double[] ScaleUIPoint(LvcPoint point, int xAxisIndex = 0, int yAxisIndex = 0)
    {
        if (_core is null) throw new Exception("core not found");
        var cartesianCore = (CartesianChart<SkiaSharpDrawingContext>)_core;
        return cartesianCore.ScaleUIPoint(point, xAxisIndex, yAxisIndex);
    }

    /// <inheritdoc cref="ICartesianChartView{TDrawingContext}.ScalePixelsToData(LvcPointD, int, int)"/>
    public LvcPointD ScalePixelsToData(LvcPointD point, int xAxisIndex = 0, int yAxisIndex = 0)
    {
        if (_core is not CartesianChart<SkiaSharpDrawingContext> cc) throw new Exception("core not found");
        var xScaler = new Scaler(cc.DrawMarginLocation, cc.DrawMarginSize, cc.XAxes[xAxisIndex]);
        var yScaler = new Scaler(cc.DrawMarginLocation, cc.DrawMarginSize, cc.YAxes[yAxisIndex]);

        return new LvcPointD { X = xScaler.ToChartValues(point.X), Y = yScaler.ToChartValues(point.Y) };
    }

    /// <inheritdoc cref="ICartesianChartView{TDrawingContext}.ScaleDataToPixels(LvcPointD, int, int)"/>
    public LvcPointD ScaleDataToPixels(LvcPointD point, int xAxisIndex = 0, int yAxisIndex = 0)
    {
        if (_core is not CartesianChart<SkiaSharpDrawingContext> cc) throw new Exception("core not found");

        var xScaler = new Scaler(cc.DrawMarginLocation, cc.DrawMarginSize, cc.XAxes[xAxisIndex]);
        var yScaler = new Scaler(cc.DrawMarginLocation, cc.DrawMarginSize, cc.YAxes[yAxisIndex]);

        return new LvcPointD { X = xScaler.ToPixels(point.X), Y = yScaler.ToPixels(point.Y) };
    }

    /// <inheritdoc cref="IChartView{TDrawingContext}.GetPointsAt(LvcPoint, TooltipFindingStrategy)"/>
    public IEnumerable<ChartPoint> GetPointsAt(LvcPoint point, TooltipFindingStrategy strategy = TooltipFindingStrategy.Automatic)
    {
        if (_core is not CartesianChart<SkiaSharpDrawingContext> cc) throw new Exception("core not found");

        if (strategy == TooltipFindingStrategy.Automatic)
            strategy = cc.Series.GetTooltipFindingStrategy();

        return cc.Series.SelectMany(series => series.FindHitPoints(cc, point, strategy));
    }

    /// <inheritdoc cref="IChartView{TDrawingContext}.GetVisualsAt(LvcPoint)"/>
    public IEnumerable<VisualElement<SkiaSharpDrawingContext>> GetVisualsAt(LvcPoint point)
    {
        return _core is not CartesianChart<SkiaSharpDrawingContext> cc
            ? throw new Exception("core not found")
            : cc.VisualElements.SelectMany(visual => ((VisualElement<SkiaSharpDrawingContext>)visual).IsHitBy(_core, point));
    }

    /// <inheritdoc cref="IChartView{TDrawingContext}.ShowTooltip(IEnumerable{ChartPoint})"/>
    public void ShowTooltip(IEnumerable<ChartPoint> points)
    {
        if (_tooltip is null || _core is null) return;
        _tooltip.Show(points, _core);
    }

    /// <inheritdoc cref="IChartView{TDrawingContext}.HideTooltip"/>
    public void HideTooltip()
    {
        if (_tooltip is null || _core is null) return;
        _core?.ClearTooltipData();
        _tooltip.Hide();
    }

    void IChartView.InvokeOnUIThread(Action action)
    {
        _ = MainThread.InvokeOnMainThreadAsync(action);
    }

    /// <summary>
    /// Initializes the core.
    /// </summary>
    /// <returns></returns>
    protected void InitializeCore()
    {
        var zoomingSection = new RectangleGeometry();
        var zoomingSectionPaint = new SolidColorPaint
        {
            IsFill = true,
            Color = new SkiaSharp.SKColor(33, 150, 243, 50),
            ZIndex = int.MaxValue
        };
        zoomingSectionPaint.AddGeometryToPaintTask(canvas.CanvasCore, zoomingSection);
        canvas.CanvasCore.AddDrawableTask(zoomingSectionPaint);

        _core = new CartesianChart<SkiaSharpDrawingContext>(
            this, LiveChartsSkiaSharp.DefaultPlatformBuilder, canvas.CanvasCore, zoomingSection);
        _core.Update();
    }

    /// <summary>
    /// Called when a bindable property changed.
    /// </summary>
    /// <param name="o">The o.</param>
    /// <param name="oldValue">The old value.</param>
    /// <param name="newValue">The new value.</param>
    /// <returns></returns>
    protected static void OnBindablePropertyChanged(BindableObject o, object oldValue, object newValue)
    {
        var chart = (CartesianChart)o;
        if (chart._core is null) return;
        chart._core.Update();
    }

    /// <inheritdoc cref="NavigableElement.OnParentSet"/>
    protected override void OnParentSet()
    {
        base.OnParentSet();

        if (Parent == null)
        {
            _core?.Unload();
            return;
        }

        _core?.Load();
    }

    private void OnDeepCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _core?.Update();
    }

    private void OnDeepCollectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _core?.Update();
    }

    private void OnSizeChanged(object? sender, EventArgs e)
    {
        _core?.Update();
    }

    private void PanGestureRecognizer_PanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        // gestures might not be working properly in android in the current version of maui (rc2)
        // https://github.com/dotnet/maui/issues/6553

        if (_core is null) return;
        if (e.StatusType is not GestureStatus.Running and not GestureStatus.Completed) return;
        if (DateTime.Now < _panLocketUntil) return;

        var c = (CartesianChart<SkiaSharpDrawingContext>)_core;

        if (e.StatusType == GestureStatus.Running)
        {
            var delta = new LvcPoint(
                (float)((e.TotalX - _lastPanX) * DeviceDisplay.MainDisplayInfo.Density),
                (float)((e.TotalY - _lastPanY) * DeviceDisplay.MainDisplayInfo.Density));

            var args = new PanGestureEventArgs(delta);
            c.InvokePanGestrue(args);

            if (args.Handled) return;

            c.Pan(delta, true);
            _lastPanX = e.TotalX;
            _lastPanY = e.TotalY;

            return;
        }

        // lets just let the core know that the pan finished,
        // so the core is able to bounce back the plot in case it exceeded the allowed limits
        // this is a dummy request of += 0.01 pixels just in the corresponding direction
        c.Pan(new LvcPoint(_lastPanX > 0 ? 0.01f : -0.01f, _lastPanY > 0 ? 0.01f : -0.01f), false);

        _lastPanX = 0;
        _lastPanY = 0;
    }

    private void PinchGestureRecognizer_PinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        // gestures might not be working properly in android in the current version of maui (rc2)
        // https://github.com/dotnet/maui/issues/6553

        if (_core is null) return;
        if (e.Status is not GestureStatus.Running and not GestureStatus.Completed) return;

        var c = (CartesianChart<SkiaSharpDrawingContext>)_core;
        var p = e.ScaleOrigin;
        var s = c.ControlSize;

        var pivot = new LvcPoint((float)(p.X * s.Width), (float)(p.Y * s.Height));

        if (e.Status == GestureStatus.Running)
        {
            c.Zoom(pivot, ZoomDirection.DefinedByScaleFactor, e.Scale, true);
            _panLocketUntil = DateTime.Now.AddMilliseconds(500);
            _lastScale = e.Scale;
            return;
        }

        // lets just let the core know that the zoom finished,
        // so the core is able to bounce back the plot in case it exceeded the allowed limits
        // this is a dummy request of += .001 percent just in the corresponding direction
        c.Zoom(pivot, ZoomDirection.DefinedByScaleFactor, _lastScale < 1 ? 0.99999 : 1.00001, false);
    }

    private void OnSkCanvasTouched(object? sender, SKTouchEventArgs e)
    {
        if (_core is null) return;

        var location = new LvcPoint(e.Location.X, e.Location.Y);
        _core.InvokePointerDown(location, false);
        _core.InvokePointerMove(location);

        Touched?.Invoke(this, e);
    }

    private void OnCoreUpdateFinished(IChartView<SkiaSharpDrawingContext> chart)
    {
        UpdateFinished?.Invoke(this);
    }

    private void OnCoreUpdateStarted(IChartView<SkiaSharpDrawingContext> chart)
    {
        UpdateStarted?.Invoke(this);
    }

    private void OnCoreMeasuring(IChartView<SkiaSharpDrawingContext> chart)
    {
        Measuring?.Invoke(this);
    }

    void IChartView.OnDataPointerDown(IEnumerable<ChartPoint> points, LvcPoint pointer)
    {
        DataPointerDown?.Invoke(this, points);
        if (DataPointerDownCommand is not null && DataPointerDownCommand.CanExecute(points)) DataPointerDownCommand.Execute(points);

        var closest = points.FindClosestTo(pointer);
        ChartPointPointerDown?.Invoke(this, closest);
        if (ChartPointPointerDownCommand is not null && ChartPointPointerDownCommand.CanExecute(closest))
            ChartPointPointerDownCommand.Execute(closest);
    }

    void IChartView<SkiaSharpDrawingContext>.OnVisualElementPointerDown(
        IEnumerable<VisualElement<SkiaSharpDrawingContext>> visualElements, LvcPoint pointer)
    {
        var args = new VisualElementsEventArgs<SkiaSharpDrawingContext>(visualElements, pointer);

        VisualElementsPointerDown?.Invoke(this, args);
        if (VisualElementsPointerDownCommand is not null && VisualElementsPointerDownCommand.CanExecute(args))
            VisualElementsPointerDownCommand.Execute(args);
    }

    void IChartView.Invalidate()
    {
        CoreCanvas.Invalidate();
    }
}
