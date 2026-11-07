using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using WorkStation.App.Platform;
using WorkStation.App.Services;
using WorkStation.App.ViewModels;

namespace WorkStation.App.Views;

public partial class MainWindow : SukiWindow
{
    private const double MenuClickSlop = 6;

    /// <summary>
    /// 整窗背景。
    ///
    /// 为什么不用 SukiUI 自带的 Gradient / GradientSoft：它们都走
    /// <c>blendOverlay(iBase, ...)</c>，浅色主题下 iBase 是纯白，
    /// 白色参与 overlay 结果恒为白 —— 换哪个风格背景都是一片白，
    /// 玻璃面板透不到任何颜色。
    ///
    /// 为什么必须画在 shader 而不是布局里：SukiUI 把标题栏和内容区一起塞进
    /// SukiMainHost，标题栏看到的是 shader、内容区看到的是 Grid 自己铺的底。
    /// 之前背景铺在内容 Grid 里，标题栏就露出一条 shader 的白，中间横着一道接缝。
    /// 画成 shader 之后整窗只有一条渐变，标题栏和内容区天然连续。
    ///
    /// 颜色只取主题的 iBase / iPrimary，不引入第二套配色；
    /// 明暗主题、换主色都会自动跟着变。
    /// </summary>
    /// <remarks>
    /// internal 是为了让测试能拿它去编译一次 —— 这个 shader 走的是 composition
    /// 自定义视觉，无头截图里根本不渲染，跑测试看不到效果；
    /// 但 SukiEffect.FromString 是预编译的，语法错误会抛异常，这一层必须守住。
    /// </remarks>
    internal const string BackdropShader = """
        vec4 main(vec2 fragCoord)
        {
            vec2 uv = fragCoord / iResolution.xy;

            // 左上偏浓、右下淡出的斜向系数。
            // 之前用「两端着色、中间归零」，中间一大片等于没上色，
            // 整窗看着还是白的 —— 玻璃没有可透的东西。
            //
            // 窗口最外侧不要再做任何「淡到某个颜色」的处理：之前把边缘淡成固定的白
            // （深色主题是 #323232），换主色之后那条白边和主题色对不上，就成了
            // 窗口外圈一条颜色不一样的边。现在边缘和内部用同一条公式，
            // 颜色是连续的，缝里露出的也是主题底色（SukiStrongBackground）。
            float d = clamp(uv.x * 0.85 + uv.y * 0.55, 0.0, 1.0);
            float w = 1.0 - smoothstep(0.02, 1.0, d);

            // 深色底本来就深，同样强度会糊成一片，所以压低
            float strength = (iDark > 0.5) ? 0.34 : 0.46;

            vec3 col = mix(iBase, iPrimary, w * strength);

            return vec4(col, iAlpha);
        }
        """;

    // SukiUI Normal 紧凑度下 Pane.CompactWidth = 48；内容区列宽必须同步，
    // 否则菜单收起了，右侧还留着 236px 的空当。
    private const double ExpandedPaneWidth = 236;
    private const double CompactPaneWidth = 48;

    private readonly SukiToastManager _toastManager = new();
    private readonly SukiDialogManager _dialogManager = new();

    /// <summary>页面切走的轻量淡入：只碰透明度，不碰命中测试，动画期间页面照样能点。</summary>
    private static readonly Animation PageEnterAnimation = new()
    {
        Duration = TimeSpan.FromMilliseconds(160),
        FillMode = FillMode.Forward,
        Children =
        {
            new KeyFrame
            {
                Cue = new Cue(0d),
                Setters = { new Setter(Visual.OpacityProperty, 0d) }
            },
            new KeyFrame
            {
                Cue = new Cue(1d),
                Setters = { new Setter(Visual.OpacityProperty, 1d) }
            }
        }
    };

    private MainWindowViewModel? _viewModel;
    private bool _forceClose;

    // 菜单导航改成「按下记录、松开确认、位移超限取消」：
    // 压缩包/滚动条/误拖都不应该顺手切页。
    private Point? _menuPressPoint;
    private CancellationTokenSource? _pageTransitionCts;

    // SukiDialogManager 同一时刻只允许一个弹框。所有编辑表单、命令面板、快捷键帮助、
    // 删除确认都走这里，由「VM 状态变 true 弹 / 变 false 收」驱动。
    private ISukiDialog? _activeDialog;
    private IEditorForm? _editorForm;

    public MainWindow()
    {
        InitializeComponent();

        Icon = LoadWindowIcon();

        // 整窗背景（含标题栏）统一由这个 shader 画，见 BackdropShader 的注释。
        BackgroundShaderCode = BackdropShader;

        // 两个浮层宿主必须拿到管理器实例才会开始工作：
        // 少了这一步，调用 Queue/TryShowDialog 不会有任何反应，也不报错
        ToastHost.Manager = _toastManager;
        DialogHost.Manager = _dialogManager;

        // 隧道阶段处理：等冒泡到窗口时，字母键往往已经被 TextBox 或列表吃掉了
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        // SukiSideMenu 是 TreeView。不能订阅 SelectionChanged 来导航：TreeView 在
        // 内容区被点击、容器生成、AlwaysSelected 补选时会自行改选中项，会把页面拉回看板。
        //
        // 也不能只听冒泡：TreeView/TreeViewItem 自己会处理 PointerPressed 并置 Handled，
        // 点在图标、文字、空白处等不同子区域时结果不一致 —— 表现就是「有时点了没反应」。
        // 走隧道阶段（handledEventsToo 一并打开），在 TreeView 吃掉事件之前就拿到它。
        //
        // 导航放在 PointerReleased 而不是 Pressed：按下只记录起点，松开时确认位移，
        // 这样拖动侧栏滚动条、按住滑出菜单都不会误切页。
        SideMenu.AddHandler(
            PointerPressedEvent,
            OnSideMenuPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        SideMenu.AddHandler(
            PointerReleasedEvent,
            OnSideMenuPointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        SideMenu.AddHandler(
            KeyDownEvent,
            OnSideMenuKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        // 折叠时补 Tooltip，展开时清掉；状态由 SukiSideMenu 自己驱动。
        // 绑定是单向的：控件初始化时默认 false，双向会把「未展开」写回 VM，
        // 这里只在用户真的点了折叠按钮时把控件状态回写。
        SideMenu.PropertyChanged += (_, e) =>
        {
            if (e.Property != SukiSideMenu.IsMenuExpandedProperty) return;

            if (_viewModel is { } vm && vm.IsSidebarExpanded != SideMenu.IsMenuExpanded)
            {
                vm.IsSidebarExpanded = SideMenu.IsMenuExpanded;
            }

            UpdateMenuToolTips();
        };
        UpdateMenuToolTips();

        // SukiUI 7.0.1 只在 ChangeColorTheme 时按「当时的明暗」计算主色阶 120/150，
        // 切换明暗主题不会重算。Suki 的 Basic / Outlined 等按钮类都用 120 阶做前景，
        // 不刷新就会在深色下沿用浅色算出的深蓝，文字发暗、几乎看不清。
        // 这里在主题切换后重新应用当前生效的主题色（不选色），让 Suki 按新明暗重算。
        //
        // 必须用 ActiveColorTheme 而不是 ThemeColor：后者是内置枚举，
        // 用户自定义了主题色之后它仍然停在某个内置值上，
        // 拿它重新套一遍会把自定义配色打回内置色。
        ActualThemeVariantChanged += (_, _) =>
        {
            if (Application.Current is not { } app) return;
            var theme = SukiUI.SukiTheme.GetInstance(app);
            if (theme.ActiveColorTheme is { } active) theme.ChangeColorTheme(active);
            else theme.ChangeColorTheme(theme.ThemeColor);
        };

        Opened += OnOpened;
        Closing += OnClosing;

        // 真正关闭（退出）时停掉调度，否则进程退出前定时器还会再跑一拍
        Closed += (_, _) => ViewModel?.StopReminderPump();
    }

    public MainWindow(MainWindowViewModel viewModel) : this()
    {
        DataContext = viewModel;

        // 视图模型只管「有没有一条提示」；怎么画交给 Suki 的提示宿主
        _viewModel = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // 折叠状态是绑定进来的：等模板把菜单项生成完，再按最终展开状态补提示。
        // 开局也要同步一次选中态：Page 是在 VM 构造里就定好的，那时视图还没订阅
        // PropertyChanged，光靠事件会漏掉首帧 —— 启动页是仪表盘时菜单上没有高亮项。
        Dispatcher.UIThread.Post(() =>
        {
            SyncSidebarWidth();
            SyncSideMenuSelection();
            UpdateMenuToolTips();
        }, DispatcherPriority.Background);

        WireDialogs(viewModel);
    }

    /// <summary>
    /// 把所有「弹出式」界面统一接到 SukiDialogHost：页面 VM 只暴露一个布尔状态，
    /// 这里负责把内容模板 / 面板视图装进 Suki 的居中弹框，并在状态归零时收掉。
    /// </summary>
    private void WireDialogs(MainWindowViewModel vm)
    {
        foreach (var page in new IEditorDialog?[] { vm.Tasks, vm.Projects, vm.Reminders })
        {
            if (page is null) continue;
            var editor = page;
            editor.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(IEditorDialog.IsFormOpen)) return;
                if (editor.IsFormOpen) ShowEditorDialog(editor);
                else DismissActiveDialog();
            };
        }

        foreach (var page in new IDeleteConfirmation?[] { vm.Tasks, vm.Projects, vm.Reminders })
        {
            if (page is not IDeleteConfirmation confirmation ||
                page is not System.ComponentModel.INotifyPropertyChanged notifier) continue;

            notifier.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IDeleteConfirmation.IsDeleteConfirmOpen) &&
                    confirmation.IsDeleteConfirmOpen)
                {
                    ShowDeleteDialog(confirmation);
                }
            };
        }

        if (vm.Palette is { } palette)
        {
            palette.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(CommandPaletteViewModel.IsOpen)) return;
                if (palette.IsOpen) ShowPaletteDialog(palette);
                else DismissActiveDialog();
            };
        }

        // 自定义主题色弹窗：设置页只管「要不要开」，弹框本体由这里统一呈现。
        if (vm.Settings is { } settings)
        {
            settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(SettingsViewModel.IsCustomThemeDialogOpen)) return;
                if (settings.IsCustomThemeDialogOpen) ShowCustomThemeDialog(settings);
                else DismissActiveDialog();
            };
        }
    }

    /// <summary>自定义主题色弹窗：两个取色器 + 取消/应用，由 Suki 对话框承载。</summary>
    private void ShowCustomThemeDialog(SettingsViewModel settings)
    {
        _editorForm = null;
        var view = new CustomThemeDialogView { DataContext = settings };

        PresentDialog(
            view,
            _ => { if (settings.IsCustomThemeDialogOpen) settings.CloseCustomThemeDialogCommand.Execute(null); },
            builder =>
            {
                builder.SetTitle("自定义主题色");
                builder.AddActionButton("取消", _ => settings.CloseCustomThemeDialogCommand.Execute(null), true, new[] { "Flat" });
                builder.AddActionButton("应用", _ => settings.ApplyCustomThemeCommand.Execute(null), true, new[] { "Basic" });
            });
    }

    private void ShowEditorDialog(IEditorDialog page)
    {
        if (!TryGetResource(page.DialogTemplateKey, null, out var resource) ||
            resource is not IDataTemplate template) return;

        // 560 才能让「日期 + 时间」两列都拿到 Suki 模板要求的宽度：
        // TimePicker 的 PART_FlyoutButton MinWidth 是 242，加日期列与内边距后 460 会被裁切。
        var content = new ContentControl
        {
            Content = page.Form,
            ContentTemplate = template,
            Width = 560
        };

        _editorForm = page.Form as IEditorForm;
        PresentDialog(content, _ =>
        {
            _editorForm = null;
            if (page.IsFormOpen) page.DismissForm();
            // 编辑弹窗关闭后刷新当前页（看板等），让改动立刻反映在停留的页面上
            _viewModel?.RefreshActivePage();
        });
    }

    private void ShowPaletteDialog(CommandPaletteViewModel palette)
    {
        _editorForm = null;
        var view = new CommandPaletteView { DataContext = palette, Width = 580 };
        PresentDialog(view, _ => { if (palette.IsOpen) palette.Close(); });
    }

    private void ShowShortcutHelpDialog(MainWindowViewModel vm)
    {
        if (!TryGetResource("ShortcutHelpTemplate", null, out var resource) ||
            resource is not IDataTemplate template) return;

        _editorForm = null;
        var content = new ContentControl { Content = vm, ContentTemplate = template, Width = 430 };
        PresentDialog(content, _ =>
        {
            if (vm.IsShortcutHelpOpen) vm.CloseShortcutHelpCommand.Execute(null);
        });
    }

    /// <summary>统一弹框入口：先收掉可能存在的上一个，再交给 Suki 的对话框宿主。</summary>
    private void PresentDialog(
        object content,
        Action<ISukiDialog>? onDismissed = null,
        Action<SukiDialogBuilder>? configure = null)
    {
        if (_activeDialog is not null) _dialogManager.TryDismissDialog(_activeDialog);

        var builder = new SukiDialogBuilder(_dialogManager);
        builder.SetContent(content);
        builder.SetCanDismissWithBackgroundClick(true);
        builder.SetOnDismissed(dialog =>
        {
            if (ReferenceEquals(_activeDialog, dialog)) _activeDialog = null;
            onDismissed?.Invoke(dialog);
        });
        configure?.Invoke(builder);

        _activeDialog = builder.Dialog;

        // Suki 的 TimePicker 模板把时/分文本写死成 Center；给足宽度后仍会分散居中。
        // 模板内是本地值、样式覆盖不了，只能在挂树后把这几个 TextBlock 拨到左对齐。
        if (content is Control control) AlignTimePickerText(control);

        builder.TryShow();
    }

    /// <summary>
    /// 只改对齐，不碰颜色/尺寸/主题：Suki TimePicker 模板里的数字格子是居中本地值，
    /// 用户要求时间文本靠左。模板应用发生在挂树之后，所以用 Loaded 优先级补一次。
    /// </summary>
    private static void AlignTimePickerText(Control content)
    {
        static void Apply(Control root)
        {
            foreach (var picker in root.GetVisualDescendants().OfType<TimePicker>())
            {
                foreach (var text in picker.GetVisualDescendants().OfType<TextBlock>())
                {
                    text.HorizontalAlignment = HorizontalAlignment.Left;
                }
            }
        }

        void OnAttached(object? _, VisualTreeAttachmentEventArgs __)
        {
            content.AttachedToVisualTree -= OnAttached;
            Dispatcher.UIThread.Post(() => Apply(content), DispatcherPriority.Loaded);
        }

        if (content.IsAttachedToVisualTree()) Apply(content);
        else content.AttachedToVisualTree += OnAttached;
    }

    private void DismissActiveDialog()
    {
        if (_activeDialog is { } dialog) _dialogManager.TryDismissDialog(dialog);
    }

    /// <summary>项目任务弹窗：左侧放大进度环 + 右侧垂直 stepper，由 ShowTasksForProject 触发。</summary>
    private void ShowProjectTasksDialog(ProjectTasksDialogViewModel vm)
    {
        _editorForm = null;
        var view = new ProjectTasksDialogView { DataContext = vm, Width = 820, Height = 560 };
        vm.CloseRequested += OnProjectTasksCloseRequested;
        PresentDialog(view, _ =>
        {
            vm.CloseRequested -= OnProjectTasksCloseRequested;
            if (_viewModel?.ProjectTasksDialog == vm) _viewModel.CloseProjectTasksDialog();
        });
    }

    private void OnProjectTasksCloseRequested() => DismissActiveDialog();

    public ISukiToastManager Toasts => _toastManager;

    public ISukiDialogManager Dialogs => _dialogManager;

    /// <summary>
    /// 用 SukiDialogBuilder 弹删除确认。两个按钮直接执行页面 VM 已有的命令，
    /// 点背景关闭时也把 VM 的待删状态清掉，避免下次点删除时状态残留。
    /// </summary>
    private void ShowDeleteDialog(IDeleteConfirmation confirmation)
    {
        _editorForm = null;
        var content = new TextBlock
        {
            Text = $"确定删除「{confirmation.DeleteTargetName}」？删除后无法恢复。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 360
        };

        PresentDialog(
            content,
            _ => { if (confirmation.IsDeleteConfirmOpen) confirmation.Cancel.Execute(null); },
            builder =>
            {
                builder.SetTitle("删除确认");
                builder.SetType(NotificationType.Warning);
                builder.AddActionButton("取消", _ => confirmation.Cancel.Execute(null), true, new[] { "Basic" });
                builder.AddActionButton("删除", _ => confirmation.Confirm.Execute(null), true, new[] { "Danger" });
            });
    }

    /// <summary>
    /// SukiWindow 重写的窗口模板里没有 VisualLayerManager，于是窗口里没有 OverlayLayer /
    /// PopupOverlayLayer。有原生 IPopupImpl 的桌面平台看不出问题；但在 headless 渲染、
    /// 截图验收以及没有原生弹出窗口实现的平台上，Popup 只能退回 OverlayLayer ——
    /// 找不到就会抛 "Unable to create IPopupImpl and no overlay layer is found"，
    /// ComboBox 下拉直接打不开。
    ///
    /// 补一个只承载层、不承载内容的 VLM 挂在模板根。注意 PopupOverlayLayer 的开关是
    /// internal：正常由 Window.OnApplyTemplate 对模板 NameScope 里的 PART_VisualLayerManager
    /// 打开，而 Suki 模板没有这个部件、NameScope 此时又已 Complete 不能补注册，
    /// 所以只能在这里补设一次。桌面平台不走这条路径，反射失败也不影响正常使用。
    /// </summary>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (e.NameScope.Find("PART_Root") is Panel root &&
            !root.Children.OfType<VisualLayerManager>().Any())
        {
            var layers = new VisualLayerManager
            {
                EnableOverlayLayer = true,
                EnableTextSelectorLayer = true,
            };

            typeof(VisualLayerManager)
                .GetProperty("EnablePopupOverlayLayer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(layers, true);

            root.Children.Add(layers);
        }
    }

    private void OnSideMenuPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null) return;

        // 按下只记录起点；真正切页在 PointerReleased 里确认
        var item = FindMenuItem(e);
        _menuPressPoint = item is { Tag: string } ? e.GetPosition(SideMenu) : null;
    }

    private void OnSideMenuPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_viewModel is null || _menuPressPoint is not { } pressedAt) return;

        _menuPressPoint = null;
        var item = FindMenuItem(e);
        if (item is not { Tag: string tag }) return;

        // 拖动超过阈值就当成滚动/误触，不切页
        var releasedAt = e.GetPosition(SideMenu);
        if (Math.Abs(releasedAt.X - pressedAt.X) > MenuClickSlop ||
            Math.Abs(releasedAt.Y - pressedAt.Y) > MenuClickSlop)
        {
            return;
        }

        // Navigate 内部对「已经在该页」直接返回，不会重复刷新
        _viewModel.NavigateCommand.Execute(tag);
    }

    private void OnSideMenuKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null) return;
        if (e.Key is not (Key.Enter or Key.Space)) return;

        var focused = FocusManager?.GetFocusedElement() as Visual;
        var item = focused as SukiSideMenuItem
                   ?? focused?.FindAncestorOfType<SukiSideMenuItem>()
                   ?? (e.Source as Visual)?.FindAncestorOfType<SukiSideMenuItem>();

        if (item is not { Tag: string tag }) return;

        _viewModel.NavigateCommand.Execute(tag);
        e.Handled = true;
    }

    /// <summary>
    /// e.Source 偶尔会是模板里不带视觉父链的交互对象，再按指针坐标做一次命中兜底。
    /// </summary>
    private SukiSideMenuItem? FindMenuItem(PointerEventArgs e) =>
        (e.Source as Visual)?.FindAncestorOfType<SukiSideMenuItem>()
        ?? SideMenu.GetVisualAt(e.GetPosition(SideMenu))?.FindAncestorOfType<SukiSideMenuItem>();

    /// <summary>
    /// 侧栏折叠时把外壳第一列同步到 SukiUI 的紧凑宽度，内容区跟着左移。
    /// 列宽不能交给 Auto：SukiSideMenu 的 Pane 宽度是动画驱动出来的，
    /// 用 Auto 测量会得到 0，菜单文字会被 Viewbox 压成 0px。
    /// </summary>
    private void SyncSidebarWidth()
    {
        if (ShellGrid.ColumnDefinitions.Count == 0) return;

        var width = _viewModel?.IsSidebarExpanded == false ? CompactPaneWidth : ExpandedPaneWidth;
        var column = ShellGrid.ColumnDefinitions[0];
        if (Math.Abs(column.Width.Value - width) > 0.1)
        {
            column.Width = new GridLength(width);
        }
    }

    /// <summary>
    /// 折叠后只看得见图标，用 Tooltip 把页面名补回来；
    /// 展开时清空，避免鼠标悬停又浮出一个和文字重复的标签。
    /// </summary>
    private void UpdateMenuToolTips()
    {
        var collapsed = !SideMenu.IsMenuExpanded;

        foreach (var item in SideMenu.Items.OfType<SukiSideMenuItem>())
        {
            if (!collapsed)
            {
                ToolTip.SetTip(item, null);
                continue;
            }

            ToolTip.SetTip(item, item.Header as string);
            ToolTip.SetShowDelay(item, 450);
        }
    }

    /// <summary>命令面板 / 数字键 / 菜单点击切页时，把 SukiSideMenu 的选中态同步过来。</summary>
    private void SyncSideMenuSelection()
    {
        if (_viewModel is null) return;

        // 设置页不在菜单项里，全部取消高亮；只改 SideMenu.SelectedItem 不会清掉
        // TreeView 容器自己的 IsSelected，会出现「两个菜单项同时高亮」或
        // 「进了设置页还高亮上一页」，看起来就像点击没生效。
        var tag = _viewModel.Page == AppPage.Settings ? null : _viewModel.Page.ToString();
        SukiSideMenuItem? selected = null;

        foreach (var item in SideMenu.Items.OfType<SukiSideMenuItem>())
        {
            var isSelected = item.Tag is string itemTag && itemTag == tag;
            item.IsSelected = isSelected;
            if (isSelected) selected = item;
        }

        if (!ReferenceEquals(SideMenu.SelectedItem, selected))
        {
            SideMenu.SelectedItem = selected;
        }
    }

    /// <summary>
    /// 切页时给内容区做一次 160ms 淡入。只动 Opacity，不动 IsHitTestVisible，
    /// 所以动画期间列表、按钮都能立刻点。无头测试/截图里跳过：动画时钟不会自然推进，
    /// 否则截图会定格在中间帧。
    /// </summary>
    private void PlayPageTransition()
    {
        if (PageHost is null) return;

        _pageTransitionCts?.Cancel();
        if (IsHeadlessHost)
        {
            PageHost.Opacity = 1;
            return;
        }

        var cancellation = new CancellationTokenSource();
        _pageTransitionCts = cancellation;
        PageHost.Opacity = 0;
        _ = RunPageTransitionAsync(cancellation);
    }

    private async Task RunPageTransitionAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await PageEnterAnimation.RunAsync(PageHost, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // 连续切页时由新的动画接手
        }
        finally
        {
            if (ReferenceEquals(_pageTransitionCts, cancellation))
            {
                PageHost.Opacity = 1;
                _pageTransitionCts = null;
            }

            cancellation.Dispose();
        }
    }

    private static bool IsHeadlessHost =>
        AppDomain.CurrentDomain.GetAssemblies().Any(a =>
            a.GetName().Name is { } name &&
            name.StartsWith("Avalonia.Headless", StringComparison.Ordinal));

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsSidebarExpanded))
        {
            SyncSidebarWidth();
            UpdateMenuToolTips();
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.Page))
        {
            SyncSideMenuSelection();
            PlayPageTransition();
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.IsShortcutHelpOpen))
        {
            if (_viewModel is { } shell)
            {
                if (shell.IsShortcutHelpOpen) ShowShortcutHelpDialog(shell);
                else DismissActiveDialog();
            }
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.ProjectTasksDialog))
        {
            if (_viewModel is { } shell && shell.ProjectTasksDialog is { } dlg) ShowProjectTasksDialog(dlg);
            else DismissActiveDialog();
            return;
        }

        if (e.PropertyName != nameof(MainWindowViewModel.IsToastVisible)) return;
        if (_viewModel is not { IsToastVisible: true } vm) return;

        // 这几个 Set* 都返回 void，只能一条条调
        var toast = new SukiToastBuilder(_toastManager);
        toast.SetTitle(vm.ToastMessage ?? string.Empty);
        toast.SetType(vm.IsToastError ? NotificationType.Error : NotificationType.Success);
        // 与视图模型里的 3 秒计时对齐，两边同时收，不会出现「提示还在但状态已清零」
        toast.SetDismissAfter(TimeSpan.FromSeconds(3), true);
        toast.Queue();
    }

    /// <summary>
    /// 托盘是否真的建起来了。没有托盘时「关窗口驻留」必须退化成直接退出 ——
    /// 否则窗口一关，用户就再也找不回这个进程了。
    /// </summary>
    public bool TrayAvailable { get; set; }

    /// <summary>托盘没建起来的原因，启动后提示一次，不让它静默失败。</summary>
    public string? TrayUnavailableReason { get; set; }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>托盘菜单「退出」走这里：绕过驻留逻辑，真正关掉窗口。</summary>
    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private static WindowIcon? LoadWindowIcon()
    {
        try
        {
            return new WindowIcon(AssetLoader.Open(new Uri("avares://WorkStation/Assets/app.png")));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (ViewModel is not { IsReady: true } viewModel) return;

        try
        {
            viewModel.AttachNotifications(new WindowNotificationSink(this));
        }
        catch (Exception)
        {
            // 无头/无通知宿主的环境：没有通知通道，调度器照常把状态推进下去
        }

        viewModel.StartReminderPump();

        // 托盘没建成时，用户看不到任何图标；不说一声就变成「关窗口直接退出」太难解释
        if (TrayUnavailableReason is { } reason)
        {
            viewModel.ShowToast($"托盘没建起来（{reason}）：关闭窗口会直接退出", true);
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose) return;

        var closeToTray = ViewModel?.CloseToTray ?? true;
        AppLog.Write($"关窗口：TrayAvailable={TrayAvailable} CloseToTray={closeToTray} ForceClose={_forceClose}");

        if (!TrayAvailable) return;

        // 驻留托盘：窗口藏起来，提醒调度继续跑
        if (!closeToTray) return;

        e.Cancel = true;
        Hide();
        AppLog.Write("关窗口：已隐藏，进程继续驻留");
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { IsReady: true } vm) return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        // Ctrl 组合键先处理：Ctrl+数字跳页、Ctrl+B 折叠侧栏
        if (ctrl)
        {
            if (e.Key == Key.B)
            {
                vm.ToggleSidebarCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (PageFromDigit(e.Key) is { } page)
            {
                vm.NavigateCommand.Execute(page.ToString());
                e.Handled = true;
                return;
            }
        }

        if (ctrl && e.Key is Key.K or Key.P)
        {
            if (vm.Palette is { IsOpen: true })
            {
                vm.Palette.Close();
            }
            else
            {
                vm.OpenPaletteCommand.Execute(null);
            }

            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.Enter && _editorForm is { } form)
        {
            form.Save.Execute(null);
            e.Handled = true;
            return;
        }

        // 命令面板现在是 SukiDialog 的弹框内容，焦点不一定落在它的控件树上。
        // 上下 / 回车 / Esc 改由窗口统一分发，其余按键照旧放行给搜索框。
        if (vm.Palette is { IsOpen: true } palette)
        {
            switch (e.Key)
            {
                case Key.Down:
                    palette.MoveSelection(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    palette.MoveSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    palette.RunSelected();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    palette.Close();
                    e.Handled = true;
                    break;
            }
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_activeDialog is not null)
            {
                if (_editorForm is { } editor) editor.Cancel.Execute(null);
                else DismissActiveDialog();
                e.Handled = true;
            }
            else if (vm.IsShortcutHelpOpen)
            {
                vm.CloseShortcutHelpCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (ctrl) return;

        // 正在输入时不要抢单个字母/数字 —— 在备注里打「n」不该跳去记录框
        if (FocusManager?.GetFocusedElement() is TextBox) return;

        switch (e.Key)
        {
            case Key.N:
                FocusQuickAdd();
                e.Handled = true;
                break;
            case Key.Oem2 when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                vm.ToggleShortcutHelpCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Oem2:
                FocusSearch();
                e.Handled = true;
                break;
            // 数字键与侧边栏菜单顺序一一对应：仪表盘 → 看板 → 任务 → 项目 → 提醒，
            // 设置在标题栏不在菜单里，所以排最后。
            case Key.D1:
            case Key.NumPad1:
                vm.ShowDashboardCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.D2:
            case Key.NumPad2:
                vm.ShowBoardCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.D3:
            case Key.NumPad3:
                vm.ShowTasksCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.D4:
            case Key.NumPad4:
                vm.ShowProjectsCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.D5:
            case Key.NumPad5:
                vm.ShowRemindersCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.D6:
            case Key.NumPad6:
                vm.ShowSettingsCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// 数字键与侧边栏顺序一致：仪表盘 → 看板 → 任务 → 项目 → 提醒 → 设置。
    /// 设置不在菜单里，所以单独排第 6。
    /// </summary>
    private static AppPage? PageFromDigit(Key key) => key switch
    {
        Key.D1 or Key.NumPad1 => AppPage.Dashboard,
        Key.D2 or Key.NumPad2 => AppPage.Board,
        Key.D3 or Key.NumPad3 => AppPage.Tasks,
        Key.D4 or Key.NumPad4 => AppPage.Projects,
        Key.D5 or Key.NumPad5 => AppPage.Reminders,
        Key.D6 or Key.NumPad6 => AppPage.Settings,
        _ => null
    };

    private void FocusQuickAdd()
    {
        QuickAddBox.Focus();
        QuickAddBox.SelectAll();
    }

    private void FocusSearch()
    {
        if (TryFocusSearch()) return;

        // 项目页与设置页没有搜索框：先跳到任务页，等模板实例化后再聚焦
        ViewModel?.ShowTasksCommand.Execute(null);
        Dispatcher.UIThread.Post(() => TryFocusSearch(), DispatcherPriority.Background);
    }

    private bool TryFocusSearch()
    {
        foreach (var box in this.GetVisualDescendants().OfType<TextBox>())
        {
            if (box.Tag as string != "SearchBox") continue;

            box.Focus();
            box.SelectAll();
            return true;
        }

        return false;
    }
}
