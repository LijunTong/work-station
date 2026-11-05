using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WorkStation.App.ViewModels;

/// <summary>
/// 项目下拉里的一项。
///
/// 刻意用「引用相等」的 class，而不是 record：下拉框是按引用记选中项的，
/// 值相等的两个实例在它眼里等于「没换过」，于是不会重新渲染选中态——
/// 表现就是用户在下拉里选了项目、折叠框里却仍是空白。
/// </summary>
public sealed partial class ProjectOption : ObservableObject
{
    public ProjectOption(string? id, string name, bool isArchived = false)
    {
        Id = id;
        _name = name;
        _isArchived = isArchived;
    }

    public string? Id { get; }

    /// <summary>可写：项目改名时原地更新，避免换实例把选中态弄丢。</summary>
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isArchived;

    /// <summary>筛选用的「全部项目」。</summary>
    public static ProjectOption All { get; } = new(null, "全部项目");

    /// <summary>任务表单里的「不归属任何项目」。</summary>
    public static ProjectOption Standalone { get; } = new(null, "独立任务");

    public override string ToString() => Name;
}

/// <summary>项目下拉列表的原地同步。</summary>
internal static class ProjectOptionList
{
    /// <summary>
    /// 让 <paramref name="target"/> 的内容与 <paramref name="desired"/> 一致，但不重建集合。
    ///
    /// 不能写成 Clear() + 逐项 Add()：清空会让下拉框先把选中项置空，
    /// 之后即使补回「值相同」的新实例，折叠框也不会重新认领这一项，
    /// 于是出现了「下拉里明明选了项目，框里却是空白」。这里改为按 Id 匹配、
    /// 就地改名 + 移动，选中项始终是列表里的同一个实例。
    /// </summary>
    public static void Sync(ObservableCollection<ProjectOption> target, IReadOnlyList<ProjectOption> desired)
    {
        // 1. 删掉不再需要的项（从后往前，避免索引漂移）
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (desired.Any(d => Same(d, target[i]))) continue;
            target.RemoveAt(i);
        }

        // 2. 按目标顺序就位：缺的插入、位置不对的移动、名字变了就地更新
        for (var i = 0; i < desired.Count; i++)
        {
            var want = desired[i];
            var at = IndexOf(target, want);

            if (at < 0)
            {
                target.Insert(i, want);
                continue;
            }

            var existing = target[at];
            existing.Name = want.Name;
            existing.IsArchived = want.IsArchived;

            if (at != i) target.Move(at, i);
        }
    }

    /// <summary>
    /// 同一个选项的判定：引用相同，或者 Id 相同。
    /// Id 为 null 的两项（「全部项目」/「独立任务」）只能靠引用区分。
    /// </summary>
    private static bool Same(ProjectOption a, ProjectOption b) =>
        ReferenceEquals(a, b) || (a.Id is not null && a.Id == b.Id);

    private static int IndexOf(IReadOnlyList<ProjectOption> list, ProjectOption want)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (Same(list[i], want)) return i;
        }

        return -1;
    }
}
