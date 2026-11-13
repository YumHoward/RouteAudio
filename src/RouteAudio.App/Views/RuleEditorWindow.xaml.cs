using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using RouteAudio.Core.Models;
using RouteAudio.Core.Rules;
using MessageBox = System.Windows.MessageBox;

namespace RouteAudio.App.Views;

public partial class RuleEditorWindow : Window
{
    public static string[] ConditionKinds { get; } = ["进程名", "路径", "签名者", "产品名", "AUMID", "父进程"];

    private readonly IReadOnlyList<DeviceDescriptor> _devices;
    private readonly Rule? _existing;

    public RuleEditorWindow(IReadOnlyList<DeviceDescriptor> devices, Rule? existing = null)
    {
        InitializeComponent();
        DataContext = this;

        _devices = devices;
        _existing = existing;

        TargetKindBox.ItemsSource = new[] { "指定设备", "按设备角色", "跟随系统默认" };
        TargetKindBox.SelectedIndex = 0;
        RoleBox.ItemsSource = new[] { "主设备（控制台）", "多媒体设备", "通信设备" };
        RoleBox.SelectedIndex = 0;
        CombineBox.ItemsSource = new[] { "全部满足（AND）", "任一满足（OR）" };
        CombineBox.SelectedIndex = 0;
        DeviceBox.ItemsSource = devices;
        if (devices.Count > 0) DeviceBox.SelectedIndex = 0;
        UpdateHint();

        if (existing is null)
        {
            NameBox.Text = "新规则";
            Conditions.Add(new ConditionRow { Kind = "进程名", Pattern = "" });
        }
        else
        {
            LoadExisting(existing);
        }
    }

    public ObservableCollection<ConditionRow> Conditions { get; } = [];

    public Rule? Result { get; private set; }

    private void LoadExisting(Rule rule)
    {
        NameBox.Text = rule.Name;
        PriorityBox.Text = rule.Priority.ToString();
        EnabledBox.IsChecked = rule.Enabled;

        var leaves = rule.Match switch
        {
            AllNode all => all.Children,
            AnyNode any => any.Children,
            _ => [rule.Match],
        };

        CombineBox.SelectedIndex = rule.Match is AnyNode ? 1 : 0;

        foreach (var leaf in leaves)
        {
            var (kind, pattern) = ToRow(leaf);
            Conditions.Add(new ConditionRow { Kind = kind, Pattern = pattern });
        }

        if (Conditions.Count == 0) Conditions.Add(new ConditionRow());

        switch (rule.Actions.FirstOrDefault())
        {
            case RouteToDeviceAction toDevice:
                TargetKindBox.SelectedIndex = 0;
                DeviceBox.SelectedItem = _devices.FirstOrDefault(d => d.Id == toDevice.DeviceId);
                break;
            case RouteToRoleAction toRole:
                TargetKindBox.SelectedIndex = 1;
                RoleBox.SelectedIndex = (int)toRole.Role;
                break;
            case FollowDefaultAction:
                TargetKindBox.SelectedIndex = 2;
                break;
        }
    }

    private static (string Kind, string Pattern) ToRow(MatchNode node) => node switch
    {
        ProcessNameGlobNode n => ("进程名", n.Pattern),
        PathGlobNode n => ("路径", n.Pattern),
        SignerContainsNode n => ("签名者", n.Text),
        ProductContainsNode n => ("产品名", n.Text),
        AumidGlobNode n => ("AUMID", n.Pattern),
        ParentProcessGlobNode n => ("父进程", n.Pattern),
        _ => ("进程名", "*"),
    };

    private static MatchNode ToNode(ConditionRow row) => row.Kind switch
    {
        "路径" => new PathGlobNode { Pattern = row.Pattern },
        "签名者" => new SignerContainsNode { Text = row.Pattern },
        "产品名" => new ProductContainsNode { Text = row.Pattern },
        "AUMID" => new AumidGlobNode { Pattern = row.Pattern },
        "父进程" => new ParentProcessGlobNode { Pattern = row.Pattern },
        _ => new ProcessNameGlobNode { Pattern = row.Pattern },
    };

    private void AddCondition_Click(object sender, RoutedEventArgs e) =>
        Conditions.Add(new ConditionRow { Kind = "进程名", Pattern = "" });

    private void RemoveCondition_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ConditionRow row)
        {
            Conditions.Remove(row);
        }
    }

    private void TargetKindBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceBox is null || RoleBox is null) return;
        DeviceBox.Visibility = TargetKindBox.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        RoleBox.Visibility = TargetKindBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        UpdateHint();
    }

    private void UpdateHint()
    {
        HintText.Text = TargetKindBox.SelectedIndex switch
        {
            0 => "把命中的应用固定路由到指定设备（推荐）。",
            1 => "路由到系统的对应角色默认设备，设备变化时自动跟随。",
            _ => "不改变该应用的输出，只用于占位/显式排除。",
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var rows = Conditions.Where(c => !string.IsNullOrWhiteSpace(c.Pattern)).ToList();
        if (rows.Count == 0)
        {
            MessageBox.Show(this, "至少需要填写一个匹配条件。", "规则", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var nodes = rows.Select(ToNode).ToList();
        MatchNode match = nodes.Count == 1
            ? nodes[0]
            : CombineBox.SelectedIndex == 1
                ? new AnyNode { Children = nodes }
                : new AllNode { Children = nodes };

        RuleAction action;
        switch (TargetKindBox.SelectedIndex)
        {
            case 0:
                if (DeviceBox.SelectedItem is not DeviceDescriptor device)
                {
                    MessageBox.Show(this, "请选择目标设备。", "规则", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                action = new RouteToDeviceAction { DeviceId = device.Id, Role = AudioRole.Console };
                break;
            case 1:
                action = new RouteToRoleAction { Role = (AudioRole)Math.Clamp(RoleBox.SelectedIndex, 0, 2) };
                break;
            default:
                action = new FollowDefaultAction();
                break;
        }

        Result = new Rule
        {
            Id = _existing?.Id ?? Guid.NewGuid().ToString("n"),
            Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "未命名规则" : NameBox.Text.Trim(),
            Priority = int.TryParse(PriorityBox.Text.Trim(), out var priority) ? priority : 0,
            Enabled = EnabledBox.IsChecked == true,
            Match = match,
            Actions = [action],
        };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public sealed class ConditionRow
    {
        public string Kind { get; set; } = "进程名";

        public string Pattern { get; set; } = "";
    }
}
