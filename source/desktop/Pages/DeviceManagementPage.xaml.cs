using Anpeng.Desktop.Controls;
using Anpeng.Desktop.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Anpeng.Desktop.Pages;

/// <summary>
/// 设备管理（APP-SPEC 5.5）：棚舍增删改、节点列表（MQ-2 / PT100 / 电磁阀 / ESP-01 的在线状态、
/// 固件版本、信号强度）、传感器校准记录、喷头巡检记录。
/// </summary>
public sealed partial class DeviceManagementPage : Page
{
    private string? _selectedBarnId;
    private bool _built;

    public DeviceManagementPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (!_built)
            {
                _built = true;
                RuntimeState.Monitor.Changed += Rebuild;
            }
            Rebuild();
        };
        Unloaded += (_, _) =>
        {
            if (_built)
            {
                RuntimeState.Monitor.Changed -= Rebuild;
                _built = false;
            }
        };
    }

    private void Rebuild()
    {
        var monitor = RuntimeState.Monitor;
        var data = monitor.Data;
        var barns = data.Barns;

        Host.Children.Clear();

        var head = UiKit.VStack(4);
        head.Children.Add(UiKit.PageTitle("设备管理"));
        head.Children.Add(UiKit.Caption(
            $"共 {barns.Count} 座草料棚、{data.Nodes.Count} 个采集 / 执行节点；硬件链路：MQ-2 + PT100(MAX31865) → STC89C52 → 蜂鸣器 / 5V 继电器 → DC12V 常闭电磁阀 → 开式喷头；ESP-01 上报节点心跳"));
        Host.Children.Add(head);

        // ── 汇总 ──
        var stat = UiKit.EqualColumns(4, 12);
        var s0 = UiKit.Stat("草料棚", $"{barns.Count} 座", "可增删改，改动落盘 data.json");
        var s1 = UiKit.Stat("在线节点", $"{data.Nodes.Count(n => n.Online)} / {data.Nodes.Count}",
            "心跳正常");
        var s2 = UiKit.Stat("校准记录", $"{data.Calibrations.Count} 条", "烟雾 / 温度通道，含修正量",
            UiKit.Level(AlarmLevel.Notice));
        var s3 = UiKit.Stat("喷头巡检", $"{data.Inspections.Count} 条",
            $"其中堵塞 {data.Inspections.Count(i => i.State == NozzleState.Blocked)} 只",
            UiKit.Level(AlarmLevel.Suspect));
        Grid.SetColumn(s0, 0);
        Grid.SetColumn(s1, 1);
        Grid.SetColumn(s2, 2);
        Grid.SetColumn(s3, 3);
        stat.Children.Add(s0);
        stat.Children.Add(s1);
        stat.Children.Add(s2);
        stat.Children.Add(s3);
        Host.Children.Add(stat);

        // ── 棚舍列表（增删改） ──
        Host.Children.Add(BuildBarnAdmin());

        // ── 节点列表 ──
        var barn = barns.FirstOrDefault(b => b.Id == _selectedBarnId) ?? barns.FirstOrDefault();
        var nodes = barn is null ? new List<DeviceNode>() : data.Nodes.Where(n => n.BarnId == barn.Id).ToList();

        var nodeRows = nodes.Select(n => new[]
        {
            n.Id,
            n.KindName,
            n.Model,
            n.MountPosition,
            n.FirmwareVersion,
            n.StatusName,
            n.SignalText,
            n.LastHeartbeat.ToString("MM-dd HH:mm:ss"),
        }).ToList();
        Host.Children.Add(UiKit.TitledCard(
            barn is null ? "节点列表" : $"节点列表（{barn.Name}）",
            UiKit.Table(
                new[] { "节点编号", "类型", "型号", "安装位置", "固件版本", "在线状态", "信号强度", "最近心跳" },
                nodeRows),
            "MQ-2 烟雾/可燃气体、PT100 铂热电阻（MAX31865 三线制）、DC12V 常闭式电磁阀、ESP-01 网关"));

        // ── 校准记录 ──
        var calRows = data.Calibrations.OrderByDescending(c => c.Time)
            .Select(c => new[] { c.TimeText, c.BarnName, c.Device, c.ReadingText, c.OffsetText, c.Method, c.Operator, c.Result })
            .ToList();
        Host.Children.Add(UiKit.TitledCard($"传感器校准记录（{data.Calibrations.Count} 条）",
            UiKit.Table(
                new[] { "日期", "棚舍", "通道", "本机 → 标准值", "修正量", "方法", "校准人", "结论" }, calRows),
            "修正量写入节点固件，校准周期随月度巡检执行"));

        // ── 喷头巡检记录 ──
        var inspRows = data.Inspections.OrderByDescending(i => i.Time)
            .Select(i => new[] { i.TimeText, i.BarnName, i.NozzleCode, i.StateName, i.WaterPressure, i.Inspector, i.Note })
            .ToList();
        Host.Children.Add(UiKit.TitledCard($"喷头巡检记录（{data.Inspections.Count} 条）",
            UiKit.Table(
                new[] { "日期", "棚舍", "喷头编号", "喷头状态", "静态水压", "巡检人", "备注" }, inspRows),
            "喷头状态：正常 / 堵 / 未知；发现堵塞须在下次喷淋前更换或拆洗"));
    }

    private Border BuildBarnAdmin()
    {
        var data = RuntimeState.Monitor.Data;
        var stack = UiKit.VStack(12);

        foreach (var barn in data.Barns)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = UiKit.VStack(2);
            var nameRow = UiKit.HStack(8);
            nameRow.Children.Add(UiKit.StatusDot(barn.CurrentLevel, 12));
            nameRow.Children.Add(UiKit.Label(barn.Name, 16, FontWeights.SemiBold, UiKit.BrandText));
            nameRow.Children.Add(UiKit.Pill(barn.Enabled ? "已启用" : "已停用",
                barn.Enabled ? AlarmLevel.Normal : AlarmLevel.Offline));
            nameRow.Children.Add(UiKit.Pill(barn.LevelName, barn.CurrentLevel, solid: barn.CurrentLevel == AlarmLevel.Alarm));
            left.Children.Add(nameRow);
            left.Children.Add(UiKit.Caption(
                $"{barn.Id} · {barn.Location} · {barn.Area:0} ㎡ · {barn.Structure} · 存量 {barn.StockTon:0.#} 吨"));
            Grid.SetColumn(left, 0);

            var mid = UiKit.VStack(2);
            mid.Children.Add(UiKit.Caption($"棚主 {barn.Owner} {barn.OwnerPhone}"));
            mid.Children.Add(UiKit.Caption($"喷淋覆盖：{barn.SprinklerCoverage}"));
            mid.Children.Add(UiKit.Caption($"管网水压 {barn.WaterPressure:0.00} MPa · 最近巡检 {barn.LastInspection:yyyy-MM-dd}"));
            Grid.SetColumn(mid, 1);

            var ops = UiKit.HStack(8);
            ops.VerticalAlignment = VerticalAlignment.Center;
            ops.Children.Add(UiKit.PlainButton("编辑", async (_, _) => await EditBarnAsync(barn)));
            ops.Children.Add(UiKit.PlainButton("新增节点", async (_, _) => await AddNodeAsync(barn)));
            ops.Children.Add(UiKit.PlainButton(barn.Enabled ? "停用" : "启用", (_, _) =>
            {
                barn.Enabled = !barn.Enabled;
                RuntimeState.Repo.SaveData();
                Rebuild();
            }));
            ops.Children.Add(UiKit.PlainButton("删除", async (_, _) => await DeleteBarnAsync(barn)));
            Grid.SetColumn(ops, 2);

            g.Children.Add(left);
            g.Children.Add(mid);
            g.Children.Add(ops);
            var card = UiKit.Card(g, 14);
            if (barn.Id == _selectedBarnId)
            {
                card.BorderBrush = UiKit.Brand;
                card.BorderThickness = new Thickness(2);
            }
            card.Tapped += (_, _) =>
            {
                _selectedBarnId = barn.Id;
                Rebuild();
            };
            stack.Children.Add(card);
        }

        var add = UiKit.HStack(12);
        add.Children.Add(UiKit.PrimaryButton("新增草料棚", async (_, _) => await AddBarnAsync()));
        add.Children.Add(UiKit.Caption("点击任一棚卡片可切换下方节点列表；改动即时写入 %LOCALAPPDATA%\\安棚\\data.json"));
        stack.Children.Add(add);

        return UiKit.TitledCard("草料棚台账（增 / 删 / 改）", stack, "棚舍是采样、报警、喷淋记录的归属主体");
    }

    private async Task AddBarnAsync()
    {
        var data = RuntimeState.Monitor.Data;
        var index = data.Barns.Count + 1;
        var id = $"B{index:00}";
        while (data.Barns.Any(b => b.Id == id)) id = $"B{++index:00}";

        var nameBox = new TextBox { Header = "棚舍名称", Text = $"新增草料棚{index}" };
        var locBox = new TextBox { Header = "所在位置", Text = "待填写位置" };
        var ownerBox = new TextBox { Header = "棚主", Text = "待填写棚主" };
        var areaBox = new NumberBox { Header = "建筑面积（㎡）", Value = 180, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(nameBox);
        panel.Children.Add(locBox);
        panel.Children.Add(ownerBox);
        panel.Children.Add(areaBox);
        panel.Children.Add(UiKit.Caption($"将使用棚舍编号 {id}，并自动生成 4 个节点（MQ-2 / PT100 / 电磁阀 / ESP-01）"));

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "新增草料棚",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var barn = new Barn
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(nameBox.Text) ? $"新增草料棚{index}" : nameBox.Text.Trim(),
            Location = locBox.Text.Trim(),
            Owner = ownerBox.Text.Trim(),
            Area = double.IsNaN(areaBox.Value) ? 180 : areaBox.Value,
            Structure = "彩钢顶 + 土坯墙（待现场核定）",
            StockTon = 42,
            OwnerPhone = "待填写",
            LastInspection = DateTime.Today,
            SprinklerCoverage = "待现场核定喷头覆盖",
            Enabled = true,
            WaterPressure = 0.32,
        };
        data.Barns.Add(barn);

        var kinds = new[]
        {
            (NodeKind.SmokeGas, "MQ-2 烟雾/可燃气体传感器", "草料垛中部上方 1.2 m"),
            (NodeKind.Temperature, "PT100 铂热电阻 + MAX31865", "草料垛内部 0.6 m 深处"),
            (NodeKind.Valve, "DC12V 常闭式电磁阀", "支管入口"),
            (NodeKind.Gateway, "ESP-01 WiFi 网关", "棚内配电箱旁"),
        };
        var i = 1;
        foreach (var (kind, model, mount) in kinds)
        {
            data.Nodes.Add(new DeviceNode
            {
                Id = $"{id}-N{i}",
                BarnId = id,
                BarnName = barn.Name,
                Kind = kind,
                Model = model,
                MountPosition = mount,
                FirmwareVersion = "1.0.0",
                SignalDbm = -62,
                Online = true,
                LastHeartbeat = DateTime.Now,
            });
            i++;
        }

        RuntimeState.Repo.SaveData();
        _selectedBarnId = id;
        Rebuild();
    }

    private async Task EditBarnAsync(Barn barn)
    {
        var nameBox = new TextBox { Header = "棚舍名称", Text = barn.Name };
        var locBox = new TextBox { Header = "所在位置", Text = barn.Location };
        var ownerBox = new TextBox { Header = "棚主", Text = barn.Owner };
        var phoneBox = new TextBox { Header = "联系电话", Text = barn.OwnerPhone };
        var structBox = new TextBox { Header = "结构 / 耐火说明", Text = barn.Structure };
        var areaBox = new NumberBox { Header = "建筑面积（㎡）", Value = barn.Area, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var stockBox = new NumberBox { Header = "堆存量（吨）", Value = barn.StockTon, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var coverBox = new TextBox { Header = "喷淋覆盖说明", Text = barn.SprinklerCoverage, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(nameBox);
        panel.Children.Add(locBox);
        panel.Children.Add(ownerBox);
        panel.Children.Add(phoneBox);
        panel.Children.Add(structBox);
        panel.Children.Add(areaBox);
        panel.Children.Add(stockBox);
        panel.Children.Add(coverBox);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"编辑 {barn.Name}",
            Content = new ScrollViewer { Content = panel, MaxHeight = 420 },
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        barn.Name = nameBox.Text.Trim();
        barn.Location = locBox.Text.Trim();
        barn.Owner = ownerBox.Text.Trim();
        barn.OwnerPhone = phoneBox.Text.Trim();
        barn.Structure = structBox.Text.Trim();
        if (!double.IsNaN(areaBox.Value)) barn.Area = areaBox.Value;
        if (!double.IsNaN(stockBox.Value)) barn.StockTon = stockBox.Value;
        barn.SprinklerCoverage = coverBox.Text.Trim();

        foreach (var node in RuntimeState.Monitor.Data.Nodes.Where(n => n.BarnId == barn.Id))
        {
            node.BarnName = barn.Name;
        }
        foreach (var alarm in RuntimeState.Monitor.Data.Alarms.Where(a => a.BarnId == barn.Id))
        {
            alarm.BarnName = barn.Name;
        }
        foreach (var s in RuntimeState.Monitor.Data.Sprinklers.Where(s => s.BarnId == barn.Id))
        {
            s.BarnName = barn.Name;
        }

        RuntimeState.Repo.SaveData();
        Rebuild();
    }

    private async Task DeleteBarnAsync(Barn barn)
    {
        var data = RuntimeState.Monitor.Data;
        if (data.Barns.Count <= 1)
        {
            var warn = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "无法删除",
                Content = UiKit.Label("至少需要保留 1 座草料棚，否则监控台没有受控对象。", 14, FontWeights.Normal, UiKit.Ink),
                CloseButtonText = "知道了",
            };
            await warn.ShowAsync();
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"确认删除 {barn.Name}？",
            Content = UiKit.Label(
                $"将同时移除该棚的 {data.Nodes.Count(n => n.BarnId == barn.Id)} 个节点、" +
                $"{data.Alarms.Count(a => a.BarnId == barn.Id)} 条报警记录、" +
                $"{data.Sprinklers.Count(s => s.BarnId == barn.Id)} 条喷淋记录与 " +
                $"{data.Readings.Count(r => r.BarnId == barn.Id)} 条采样。此操作不可撤销。",
                14, FontWeights.Normal, UiKit.Ink),
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        data.Barns.Remove(barn);
        data.Nodes.RemoveAll(n => n.BarnId == barn.Id);
        data.Alarms.RemoveAll(a => a.BarnId == barn.Id);
        data.Sprinklers.RemoveAll(s => s.BarnId == barn.Id);
        data.Readings.RemoveAll(r => r.BarnId == barn.Id);
        data.Calibrations.RemoveAll(c => c.BarnId == barn.Id);
        data.Inspections.RemoveAll(i => i.BarnId == barn.Id);
        if (_selectedBarnId == barn.Id) _selectedBarnId = data.Barns.FirstOrDefault()?.Id;
        RuntimeState.Repo.SaveData();
        Rebuild();
    }

    private async Task AddNodeAsync(Barn barn)
    {
        var data = RuntimeState.Monitor.Data;
        var existing = data.Nodes.Where(n => n.BarnId == barn.Id).Select(n => n.Kind).ToList();
        var missing = new[]
        {
            (NodeKind.SmokeGas, "MQ-2 烟雾/可燃气体传感器", "草料垛中部上方 1.2 m"),
            (NodeKind.Temperature, "PT100 铂热电阻 + MAX31865", "草料垛内部 0.6 m 深处"),
            (NodeKind.Valve, "DC12V 常闭式电磁阀", "支管入口"),
            (NodeKind.Gateway, "ESP-01 WiFi 网关", "棚内配电箱旁"),
        }.Where(k => !existing.Contains(k.Item1)).ToList();

        if (missing.Count == 0)
        {
            var warn = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "节点已齐备",
                Content = UiKit.Label(
                    $"{barn.Name} 已具备 MQ-2、PT100、电磁阀、ESP-01 四类节点，无需补齐。",
                    14, FontWeights.Normal, UiKit.Ink),
                CloseButtonText = "知道了",
            };
            await warn.ShowAsync();
            return;
        }

        var kindBox = new ComboBox { Header = "节点类型", MinWidth = 320 };
        foreach (var m in missing) kindBox.Items.Add(m.Item2);
        kindBox.SelectedIndex = 0;
        var modelBox = new TextBox { Header = "型号 / 说明", Text = missing[0].Item2 };
        var mountBox = new TextBox { Header = "安装位置", Text = missing[0].Item3 };
        kindBox.SelectionChanged += (_, _) =>
        {
            var idx = kindBox.SelectedIndex;
            if (idx < 0 || idx >= missing.Count) return;
            modelBox.Text = missing[idx].Item2;
            mountBox.Text = missing[idx].Item3;
        };

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(kindBox);
        panel.Children.Add(modelBox);
        panel.Children.Add(mountBox);
        panel.Children.Add(UiKit.Caption("新增节点默认固件版本 1.0.0、信号强度 -62 dBm、心跳在线，保存后可继续在本页维护"));

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"为 {barn.Name} 补齐节点",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var pick = missing[Math.Max(0, kindBox.SelectedIndex)];
        var n = data.Nodes.Count(x => x.BarnId == barn.Id) + 1;
        data.Nodes.Add(new DeviceNode
        {
            Id = $"{barn.Id}-N{n}",
            BarnId = barn.Id,
            BarnName = barn.Name,
            Kind = pick.Item1,
            Model = string.IsNullOrWhiteSpace(modelBox.Text) ? pick.Item2 : modelBox.Text.Trim(),
            MountPosition = string.IsNullOrWhiteSpace(mountBox.Text) ? pick.Item3 : mountBox.Text.Trim(),
            FirmwareVersion = "1.0.0",
            SignalDbm = -62,
            Online = true,
            LastHeartbeat = DateTime.Now,
        });

        RuntimeState.Repo.SaveData();
        Rebuild();
    }
}
