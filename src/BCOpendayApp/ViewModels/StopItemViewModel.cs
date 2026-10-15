using System.Windows.Input;
using BCOpendayApp.Models;

namespace BCOpendayApp.ViewModels;

/// <summary>One stop as shown in a list or card (adds "visited" text and the explore command).</summary>
public sealed class StopItemViewModel
{
    public StopItemViewModel(Hotspot stop, bool isVisited, ICommand exploreCommand)
    {
        Stop = stop;
        StatusText = isVisited ? "✓ Visited" : "Ready to explore";
        ExploreCommand = exploreCommand;
    }

    /// <summary>Passed back as the CommandParameter so one shared command serves every card.</summary>
    public Hotspot Stop { get; }

    public string Name => Stop.Name;

    public string Image => Stop.Image;

    public string Summary => Stop.Summary;

    public string StatusText { get; }

    public string ExploreText => $"Explore {Stop.Name}";

    public ICommand ExploreCommand { get; }
}
