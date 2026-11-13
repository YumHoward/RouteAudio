using System.Collections.ObjectModel;
using System.Windows;
using RouteAudio.Audio;
using RouteAudio.Core.Abstractions;
using RouteAudio.Core.Config;
using RouteAudio.Core.Models;
using RouteAudio.Core.Rules;
using MessageBox = System.Windows.MessageBox;

namespace RouteAudio.App.Views;

public partial class RulesWindow : Window
{
    private readonly AudioHost _host;
    private readonly IAuditLog _log;
    private bool _loading;

    public RulesWindow(AudioHost host, IAuditLog log)
    {
        InitializeComponent();
        DataContext = this;
        _host = host;
        _log = log;

        LoadProfiles();
        LoadRules();
        LoadApps();
    }

    public ObservableCollection<Profile> Profiles { get; } = [];

    public ObservableCollection<RuleRow> RuleRows { get; } = [];

    public ObservableCollection<AppRuntimeView> Apps { get; } = [];

    private Profile ActiveProfile => _host.Routing.ActiveProfile;

    private void LoadProfiles()
    {
        _loading = true;
        Profiles.Clear();
        foreach (var profile in _host.Routing.Config.Profiles) Profiles.Add(profile);
        ProfileBox.ItemsSource = Profiles;
        ProfileBox.SelectedItem = Profiles.FirstOrDefault(p => p.Id == _host.Routing.Config.ActiveProfileId);
        _loading = false;
    }

    private void LoadRules()
    {
        RuleRows.Clear();
        var devices = _host.Routing.Devices;
        foreach (var rule in ActiveProfile.Rules)
        {
            RuleRows.Add(new RuleRow(rule, id => devices.FirstOrDefault(d => d.Id == id)?.DisplayName));
        }

        RuleCountText.Text = $"共 {RuleRows.Count} 条规则";
    }

    private void LoadApps()
    {
        Apps.Clear();
        foreach (var app in _host.Routing.GetRunningApps()) Apps.Add(app);
        AppBox.ItemsSource = Apps;
        if (Apps.Count > 0) AppBox.SelectedIndex = 0;
    }

    private async void ProfileBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading || ProfileBox.SelectedItem is not Profile profile) return;
        if (profile.Id == _host.Routing.Config.ActiveProfileId) return;

        await _host.Routing.SwitchProfileAsync(profile.Id);
        LoadRules();
        LoadApps();
    }

    private async void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog("新建配置档", "配置档名称", "游戏模式") { Owner = this };
        if (dialog.ShowDialog() != true) return;

        await _host.Routing.CreateProfileAsync(dialog.Value);
        LoadProfiles();
        LoadRules();
        LoadApps();
    }

    private async void CopyProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not Profile profile) return;
        var dialog = new InputDialog("复制配置档", "新配置档名称", profile.Name + " 副本") { Owner = this };
        if (dialog.ShowDialog() != true) return;

        await _host.Routing.DuplicateProfileAsync(profile, dialog.Value);
        LoadProfiles();
        LoadRules();
        LoadApps();
    }

    private async void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not Profile profile) return;
        var dialog = new InputDialog("重命名配置档", "配置档名称", profile.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        await _host.Routing.RenameProfileAsync(profile, dialog.Value);
        LoadProfiles();
        LoadRules();
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not Profile profile) return;
        if (_host.Routing.Config.Profiles.Count <= 1)
        {
            MessageBox.Show(this, "至少需要保留一个配置档。", "配置档", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(this, $"确定删除配置档「{profile.Name}」？", "配置档",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        await _host.Routing.DeleteProfileAsync(profile);
        LoadProfiles();
        LoadRules();
        LoadApps();
    }

    private async void AddRule_Click(object sender, RoutedEventArgs e) => await EditRuleAsync(null);

    private async void EditRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesList.SelectedItem is not RuleRow row) return;
        await EditRuleAsync(row);
    }

    private async void RulesList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RulesList.SelectedItem is not RuleRow row) return;
        await EditRuleAsync(row);
    }

    private async Task EditRuleAsync(RuleRow? row)
    {
        var editor = new RuleEditorWindow(_host.Routing.Devices, row?.Rule) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result is null) return;

        if (row is null)
        {
            ActiveProfile.Rules.Add(editor.Result);
        }
        else
        {
            var index = ActiveProfile.Rules.IndexOf(row.Rule);
            if (index >= 0) ActiveProfile.Rules[index] = editor.Result;
        }

        await _host.Routing.ApplyConfigChangesAsync();
        LoadRules();
    }

    private async void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesList.SelectedItem is not RuleRow row) return;

        var result = MessageBox.Show(this, $"确定删除规则「{row.Name}」？", "规则",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        ActiveProfile.Rules.Remove(row.Rule);
        await _host.Routing.ApplyConfigChangesAsync();
        LoadRules();
    }

    private async void RuleEnabled_Click(object sender, RoutedEventArgs e)
    {
        await _host.Routing.ApplyConfigChangesAsync();
        _log.Info("[rules] 启用状态变更");
    }

    private void TestRule_Click(object sender, RoutedEventArgs e)
    {
        if (AppBox.SelectedItem is not AppRuntimeView app)
        {
            TestResultText.Text = "请先选择一个正在运行的应用。";
            return;
        }

        var match = RuleEvaluator.Evaluate(ActiveProfile.Rules, new AppMatchContext(app.App));
        if (match is null)
        {
            TestResultText.Text = $"「{app.App.DisplayName}」未命中任何规则（将保持系统默认输出）。";
            return;
        }

        var devices = _host.Routing.Devices;
        TestResultText.Text =
            $"「{app.App.DisplayName}」命中规则「{match.Rule.Name}」（优先级 {match.Rule.Priority}，" +
            $"匹配条件 {match.MatchedConditions} 个）→ {RuleText.DescribeActions(match.Actions, id => devices.FirstOrDefault(d => d.Id == id)?.DisplayName)}";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    public sealed class RuleRow(Rule rule, Func<string, string?> resolveDeviceName)
    {
        public Rule Rule { get; } = rule;

        public string Name => Rule.Name;

        public int Priority => Rule.Priority;

        public bool Enabled
        {
            get => Rule.Enabled;
            set => Rule.Enabled = value;
        }

        public string MatchText { get; } = RuleText.DescribeMatch(rule.Match);

        public string ActionText { get; } = RuleText.DescribeActions(rule.Actions, resolveDeviceName);
    }
}
