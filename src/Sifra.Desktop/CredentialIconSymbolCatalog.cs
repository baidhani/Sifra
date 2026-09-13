using Wpf.Ui.Controls;

namespace Sifra.Desktop;

/// <summary>
/// Candidate Fluent System Icon names per category, matching the reference
/// "Select Symbol" picker's tab layout — but using generic Wpf.Ui icons
/// instead of brand logos (Visa/PayPal/Facebook/etc.), since this app has
/// no licensed brand assets and Fluent icons are already used everywhere
/// else in the UI for consistency.
///
/// Not every candidate name below is guaranteed to exist in this version
/// of Wpf.Ui's SymbolRegular enum — GetSymbols filters via TryParse, so an
/// invalid guess is silently dropped rather than crashing the picker.
/// </summary>
public static class CredentialIconSymbolCatalog
{
    public static readonly IReadOnlyList<string> CategoryNames =
    [
        "Personal", "Technology", "Transport", "Finances", "Internet", "Misc",
    ];

    private static readonly Dictionary<string, string[]> Candidates = new()
    {
        ["Personal"] =
        [
            "Person24", "PersonAdd24", "PersonHeart24", "Heart24", "Eye24", "Glasses24",
            "Run24", "ShieldCheckmark24", "Handshake24", "Home24", "ContactCard24",
            "Umbrella24", "Animal24", "HeartPulse24", "Pill24", "PeopleTeam24",
            "Shirt24", "Tooth24", "DrinkCoffee24", "Gift24",
        ],
        ["Technology"] =
        [
            "Phone24", "PhoneDesktop24", "Laptop24", "Desktop24", "Tablet24",
            "Camera24", "Print24", "Wifi124", "Router24", "GameController24",
            "Cd24", "Code24", "Tv24", "Keyboard24", "Mouse24", "Sim24",
            "Video24", "DeveloperBoard24", "UsbStick24", "Bluetooth24",
        ],
        ["Transport"] =
        [
            "Airplane24", "Bicycle24", "Bus24", "VehicleCar24", "Car24",
            "VehicleShip24", "GasPump24", "VehicleSubway24", "VehicleTruck24",
            "Garage24", "LocationArrow24",
        ],
        ["Finances"] =
        [
            "Wallet24", "Money24", "Building24", "BuildingBank24", "PaymentCard24",
            "Receipt24", "ReceiptMoney24", "ArrowTrendingLines24", "Calculator24",
            "DocumentPercent24", "ChartMultiple24", "Savings24", "Coin24Regular",
            "PiggyBank24",
        ],
        ["Internet"] =
        [
            "Globe24", "Mail24", "Chat24", "Cloud24", "People24", "CloudArrowDown24",
            "Rss24", "MailInbox24", "Share24", "Link24", "CloudSync24",
            "PersonMail24", "Earth24", "CommentMultiple24", "Megaphone24", "VideoChat24",
        ],
        ["Misc"] =
        [
            "Alarm24", "Bag24", "Box24", "LockClosed24", "DoorArrowRight24",
            "Folder24", "Key24", "MusicNote124", "DataTrending24", "Notepad24",
            "Image24", "Settings24", "Cart24", "Briefcase24", "Ribbon24", "Star24",
        ],
    };

    /// <summary>Only the candidates that actually resolve to a real SymbolRegular value.</summary>
    public static IReadOnlyList<string> GetSymbols(string category) =>
        Candidates.TryGetValue(category, out var names)
            ? names.Where(n => Enum.TryParse<SymbolRegular>(n, out _)).ToList()
            : Array.Empty<string>();
}
