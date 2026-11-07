using Avalonia.Media;
using SukiUI.Content;

namespace WorkStation.App.Controls;

/// <summary>
/// 图标几何资源表。控件一律用 SukiUI 主题下的 <c>PathIcon</c>（SukiUI 注册了
/// <c>ControlTheme x:Key="{x:Type PathIcon}"</c>，模板里的 Path.Fill 绑定 Foreground，
/// 因此放在按钮里会自动继承按钮的悬停/选中换色）；这里只负责给 Data 取值。
///
/// 优先用 SukiUI 内置的 <see cref="Icons"/>。7.0.1 只内置了 52 枚窗口/通用符号，
/// 业务图标（删除、归档、时钟、铃铛、设置、导航）没有对应项，下面用**同一套**
/// Material Design Icons 补齐 —— 风格、24×24 网格、Apache V2 授权都一致。
/// </summary>
public static class AppIcons
{
    // —— SukiUI 内置 ——
    public static readonly StreamGeometry Edit = Icons.Pencil;
    public static readonly StreamGeometry Close = Icons.Cross;
    public static readonly StreamGeometry ArrowRight = Icons.ArrowRight;
    public static readonly StreamGeometry Plus = Icons.Plus;
    public static readonly StreamGeometry Calendar = Icons.Calendar;
    public static readonly StreamGeometry Check = Icons.Check;

    // —— 同源 Material Design Icons 补齐 ——
    public static readonly StreamGeometry Trash = Parse(
        "M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z");

    public static readonly StreamGeometry Archive = Parse(
        "M3,3H21V7H3V3M4,8H20V21H4V8M9.5,11A0.5,0.5 0 0,0 9,11.5V13H15V11.5A0.5,0.5 0 0,0 14.5,11H9.5Z");

    public static readonly StreamGeometry Unarchive = Parse(
        "M4 21H20V8H4M14 15V18H10V15H7L12 10L17 15M3 3H21V7H3");

    public static readonly StreamGeometry Clock = Parse(
        "M12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22C6.47,22 2,17.5 2,12A10,10 0 0,1 12,2M12.5,7V12.25L17,14.92L16.25,16.15L11,13V7H12.5Z");

    public static readonly StreamGeometry Bell = Parse(
        "M21,19V20H3V19L5,17V11C5,7.9 7.03,5.17 10,4.29C10,4.19 10,4.1 10,4A2,2 0 0,1 12,2A2,2 0 0,1 14,4C14,4.1 14,4.19 14,4.29C16.97,5.17 19,7.9 19,11V17L21,19M14,21A2,2 0 0,1 12,23A2,2 0 0,1 10,21");

    public static readonly StreamGeometry Board = Parse(
        "M19,5V7H15V5H19M9,5V11H5V5H9M19,13V19H15V13H19M9,17V19H5V17H9M21,3H13V9H21V3M11,3H3V13H11V3M21,11H13V21H21V11M11,15H3V21H11V15Z");

    public static readonly StreamGeometry Tasks = Parse(
        "M7,5H21V7H7V5M7,13V11H21V13H7M4,4.5A1.5,1.5 0 0,1 5.5,6A1.5,1.5 0 0,1 4,7.5A1.5,1.5 0 0,1 2.5,6A1.5,1.5 0 0,1 4,4.5M4,10.5A1.5,1.5 0 0,1 5.5,12A1.5,1.5 0 0,1 4,13.5A1.5,1.5 0 0,1 2.5,12A1.5,1.5 0 0,1 4,10.5M7,19V17H21V19H7M4,16.5A1.5,1.5 0 0,1 5.5,18A1.5,1.5 0 0,1 4,19.5A1.5,1.5 0 0,1 2.5,18A1.5,1.5 0 0,1 4,16.5Z");

    // 原本用的 view-grid-outline 和看板的 dashboard 都是「四个方块」，放大后几乎分不清，
    // 项目改用同套 MDI 的 folder-outline，语义和轮廓都更好认。
    public static readonly StreamGeometry Projects = Parse(
        "M20,18H4V8H20M20,6H12L10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6Z");

    // 仪表盘：MDI 的 gauge 仪表盘轮廓（圆弧 + 指针），和看板的方块网格区分得开
    public static readonly StreamGeometry Dashboard = Parse(
        "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,4A8,8 0 0,1 19.93,10H16A4,4 0 0,0 12,8V4M12,20A8,8 0 0,1 4.07,14H8A4,4 0 0,0 12,16V20M12,12L15.5,8.5L14.5,7.5L12,10V12H12Z");

    // 逾期 / 阻塞提示条上的警示三角
    public static readonly StreamGeometry Alert = Parse(
        "M13,14H11V9H13M13,18H11V16H13M1,21H23L12,2L1,21Z");

    // 趋势箭头：右上折线，和「本月/本周」这种时间概念区分开
    public static readonly StreamGeometry Trend = Parse(
        "M16,6H22V12H20V9.41L11.83,17.59L10.41,16.17L18.59,8H16V6M4,5H9V7H6V18H17V15H19V20H4V5Z");

    // —— 标题栏用的「描边版」图标 ——
    //
    // 标题栏的最小化/最大化/关闭是 SukiUI 自己的细线条图标（约 1.1px 线宽），
    // 而上面那些 MDI 图标是**实心填充**，且默认继承按钮主色 —— 放在一起就是
    // 三个纯蓝实心块挨着三个细黑线，大小和重量都对不上。
    //
    // 这里另存一套只用 Stroke 绘制的几何：线宽由使用处统一给（1.3px），
    // 颜色跟 SukiText 走，和窗口按钮同一档视觉重量。
    // 均画在 24×24 的网格里，用 Stretch=Uniform 缩放到同一尺寸。

    /// <summary>齿轮：8 齿轮廓 + 中心孔，只描边。</summary>
    public static readonly StreamGeometry SettingsLine = Parse(
        "M17.79,10.87L20.46,11.19L20.46,12.81L17.79,13.13A5.9,5.9 0 0 1 16.89,15.3" +
        "L18.56,17.41L17.41,18.56L15.3,16.89A5.9,5.9 0 0 1 13.13,17.79" +
        "L12.81,20.46L11.19,20.46L10.87,17.79A5.9,5.9 0 0 1 8.7,16.89" +
        "L6.59,18.56L5.44,17.41L7.11,15.3A5.9,5.9 0 0 1 6.21,13.13" +
        "L3.54,12.81L3.54,11.19L6.21,10.87A5.9,5.9 0 0 1 7.11,8.7" +
        "L5.44,6.59L6.59,5.44L8.7,7.11A5.9,5.9 0 0 1 10.87,6.21" +
        "L11.19,3.54L12.81,3.54L13.13,6.21A5.9,5.9 0 0 1 15.3,7.11" +
        "L17.41,5.44L18.56,6.59L16.89,8.7A5.9,5.9 0 0 1 17.79,10.87Z" +
        "M14.7,12A2.7,2.7 0 1 1 9.3,12A2.7,2.7 0 1 1 14.7,12");

    /// <summary>月牙：和实心版同一条轮廓，这里只描边。</summary>
    public static readonly StreamGeometry MoonLine = Parse(
        "M21,12.79A9,9 0 1,1 11.21,3A7,7 0 0,0 21,12.79Z");

    /// <summary>太阳：圆 + 8 道短射线，线宽与齿轮、月牙一致。</summary>
    public static readonly StreamGeometry SunLine = Parse(
        "M15.9,12A3.9,3.9 0 1 1 8.1,12A3.9,3.9 0 1 1 15.9,12" +
        "M18.4,12L20.7,12M16.53,16.53L18.15,18.15M12,18.4L12,20.7M7.47,16.53L5.85,18.15" +
        "M5.6,12L3.3,12M7.47,7.47L5.85,5.85M12,5.6L12,3.3M16.53,7.47L18.15,5.85");

    private static StreamGeometry Parse(string path) => StreamGeometry.Parse(path);
}
