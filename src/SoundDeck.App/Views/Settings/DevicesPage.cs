using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SoundDeck.App.ViewModels;
using SoundDeck.Core.Commands;
using SoundDeck.Core.Settings;

namespace SoundDeck.App.Views.Settings;

public sealed partial class DevicesPage : SettingsPageBase
{
    protected override string PageTitle => "デバイス";

    protected override bool RebuildOnDeviceChange => true;

    protected override void Build()
    {
        AddNote("表示名を変えたり、使わないデバイスをフライアウトから隠したりできます。既定のデバイスは隠していても表示されます。");

        foreach (var (title, flow) in new[] { ("出力デバイス", DeviceFlow.Render), ("入力デバイス", DeviceFlow.Capture) })
        {
            AddSection(title);
            var devices = Audio.GetAllDevices(flow);
            if (devices.Count == 0)
            {
                AddNote("つながっているデバイスはありません。");
            }

            foreach (var device in devices)
            {
                AddDeviceCard(device);
            }
        }
    }

    private void AddDeviceCard(DeviceViewModel device)
    {
        var custom = Current.Devices.GetValueOrDefault(device.Id);

        var nameBox = new TextBox
        {
            Text = custom?.DisplayName ?? "",
            PlaceholderText = device.Device.Description,
            Width = 220,
        };
        nameBox.LostFocus += (_, _) =>
        {
            var value = nameBox.Text.Trim();
            var entry = GetOrCreate(device.Id);
            if (entry.DisplayName != (value.Length == 0 ? null : value))
            {
                entry.DisplayName = value.Length == 0 ? null : value;
                Save();
            }
        };
        ToolTipService.SetToolTip(nameBox, "表示名（空欄で元の名前）");

        var visible = Toggle(custom?.Hidden != true, on =>
        {
            GetOrCreate(device.Id).Hidden = !on;
            Save();
        });
        visible.OnContent = "表示";
        visible.OffContent = "隠す";

        var setDefault = TextButton("既定にする", () => Audio.SetDefaultDevice(device.Id));
        setDefault.IsEnabled = !device.IsDefault;

        var status = device.IsDefault ? "既定のデバイス" : "";
        if (device.IsCommunicationsDefault)
        {
            status += status.Length > 0 ? "・既定の通信デバイス" : "既定の通信デバイス";
        }

        AddCard(device.Device.FriendlyName, status, device.DeviceGlyph, Row(nameBox, visible, setDefault));
    }

    private static DeviceCustomization GetOrCreate(string id)
    {
        if (!Current.Devices.TryGetValue(id, out var custom))
        {
            custom = new DeviceCustomization();
            Current.Devices[id] = custom;
        }

        return custom;
    }
}
