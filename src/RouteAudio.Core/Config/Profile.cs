using System.Text.Json.Serialization;
using RouteAudio.Core.Models;
using RouteAudio.Core.Rules;

namespace RouteAudio.Core.Config;

public sealed class Profile
{
    public string Id { get; set; } = "default";

    public string Name { get; set; } = "默认";

    public List<RouteEntry> Entries { get; set; } = [];

    public List<Rule> Rules { get; set; } = [];

    [JsonIgnore]
    public string DisplayText => $"{Name}";

    public RouteEntry? FindEntry(AppKey app) => Entries.FirstOrDefault(e => e.App.Id == app.Id);

    public RouteEntry GetOrCreateEntry(AppKey app)
    {
        var entry = FindEntry(app);
        if (entry is not null) return entry;
        entry = new RouteEntry { App = app };
        Entries.Add(entry);
        return entry;
    }

    public static Profile CreateDefault() => new()
    {
        Id = "default",
        Name = "默认",
        Entries = [],
        Rules =
        [
            new Rule
            {
                Id = "comm-apps",
                Name = "通信类应用走通信设备",
                Priority = 100,
                Match = new AnyNode
                {
                    Children =
                    [
                        new SignerContainsNode { Text = "Discord" },
                        new ProcessNameGlobNode { Pattern = "Discord*" },
                        new ProcessNameGlobNode { Pattern = "Teams*" },
                        new ProcessNameGlobNode { Pattern = "ms-teams*" },
                        new ProcessNameGlobNode { Pattern = "Zoom*" },
                    ],
                },
                Actions = [new RouteToRoleAction { Role = AudioRole.Communications }],
            },
        ],
    };
}
