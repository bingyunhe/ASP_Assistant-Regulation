namespace ASPAssistant.Core.ViewModels;

public class BanViewModel
{
    private readonly HashSet<string> _bannedNames = [];
    private readonly HashSet<string> _automaticNames = [];
    private readonly HashSet<string> _bannedCovenants = [];

    public IReadOnlySet<string> BannedNames => _bannedNames;
    public IReadOnlySet<string> BannedCovenants => _bannedCovenants;
    public bool IsCovenantBanned(string name) => _bannedCovenants.Contains(name);

    public void SetCovenantBans(IEnumerable<string> names)
    {
        _bannedCovenants.Clear();
        foreach (var name in names)
            if (!string.IsNullOrWhiteSpace(name)) _bannedCovenants.Add(name);
        BansChanged?.Invoke();
    }

    public event Action? BansChanged;

    public bool IsBanned(string name) => _bannedNames.Contains(name);

    /// <summary>
    /// Replaces the current ban list with the provided names.
    /// Used both on initial load and when a new match ban screen is detected
    /// (auto-clears the previous run's bans before applying the new set).
    /// </summary>
    public void SetBans(IEnumerable<string> names)
    {
        _bannedNames.Clear();
        _automaticNames.Clear();
        foreach (var name in names)
            if (!string.IsNullOrWhiteSpace(name))
            {
                _bannedNames.Add(name);
                _automaticNames.Add(name);
            }
        BansChanged?.Invoke();
    }

    public void ToggleBan(string name)
    {
        _automaticNames.Remove(name);
        if (!_bannedNames.Remove(name))
            _bannedNames.Add(name);
        BansChanged?.Invoke();
    }

    public void ClearBans()
    {
        _bannedNames.Clear();
        _automaticNames.Clear();
        _bannedCovenants.Clear();
        BansChanged?.Invoke();
    }

    public void ClearRunBans()
    {
        _bannedNames.ExceptWith(_automaticNames);
        _automaticNames.Clear();
        _bannedCovenants.Clear();
        BansChanged?.Invoke();
    }
}
