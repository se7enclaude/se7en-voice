namespace Se7enVoice.Core.Mixer;

/// <summary>
/// Range automatiquement les applis qui font du son dans les tranches de la table de mixage
/// (jeux, musique, discussion…), d'après le nom du programme et son dossier d'installation.
/// </summary>
public static class AppCategories
{
    public const string Games = "Jeux";
    public const string Music = "Musique";
    public const string Chat = "Discussion";
    public const string Browser = "Navigateur";
    public const string System = "Système";
    public const string Other = "Autres";

    public static IReadOnlyList<string> All { get; } = [Games, Music, Chat, Browser, System, Other];

    private static readonly HashSet<string> MusicApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "spotify", "deezer", "applemusic", "itunes", "vlc", "foobar2000", "musicbee", "wmplayer", "music.ui", "tidal",
        "aimp", "winamp", "amazon music", "amazonmusic", "youtube music", "ytmdesktop", "qobuz", "soundcloud", "mediamonkey",
        "groove", "dopamine", "mpc-hc64", "mpc-hc", "mpv", "potplayermini64", "potplayer",
    };

    private static readonly HashSet<string> ChatApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord", "discordptb", "discordcanary", "teams", "ms-teams", "zoom", "ts3client_win64", "ts3client_win32", "teamspeak",
        "skype", "slack", "mumble", "whatsapp", "telegram", "signal", "element", "guilded", "steamwebhelper_voice",
    };

    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "arc", "librewolf", "waterfox", "iexplore", "msedgewebview2",
    };

    private static readonly HashSet<string> KnownGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "valorant-win64-shipping", "fortniteclient-win64-shipping", "cs2", "csgo", "rocketleague", "league of legends", "gta5",
        "gtav", "javaw", "minecraft.windows", "robloxplayerbeta", "r5apex", "r5apex_dx12", "overwatch", "eldenring", "fifa23",
        "fc24", "fc25", "fc26", "cod", "modernwarfare", "blackops6", "dota2", "pubg", "tslgame", "eafc", "rainbowsix", "rainbowsix_vulkan",
        "destiny2", "warframe.x64", "genshinimpact", "hsr", "starrail", "zenlesszonezero", "palworld-win64-shipping", "helldivers2",
        "deadbydaylight-win64-shipping", "rust", "theforest", "sonsoftheforest", "witcher3", "cyberpunk2077", "baldursgate3", "bg3",
        "bg3_dx11", "marvelrivals-win64-shipping", "thefinals", "apex", "wow", "hearthstone", "lethal company", "amongus",
    };

    private static readonly string[] GameFolders =
    [
        @"\steamapps\common\", @"\epic games\", @"\riot games\", @"\ubisoft game launcher\games\", @"\ea games\",
        @"\battle.net\", @"\gog galaxy\games\", @"\xboxgames\", @"\games\",
    ];

    /// <summary>Catégorie d'une appli d'après le nom de son .exe (sans extension) et son chemin complet (facultatif).</summary>
    public static string Classify(string processName, string? exePath = null, bool isSystemSounds = false)
    {
        if (isSystemSounds) return System;
        var name = (processName ?? "").Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (MusicApps.Contains(name)) return Music;
        if (ChatApps.Contains(name)) return Chat;
        if (Browsers.Contains(name)) return Browser;
        if (KnownGames.Contains(name) || name.EndsWith("-win64-shipping", StringComparison.OrdinalIgnoreCase)) return Games;
        if (!string.IsNullOrEmpty(exePath))
        {
            var path = exePath.Replace('/', '\\').ToLowerInvariant();
            foreach (var folder in GameFolders)
                if (path.Contains(folder)) return Games;
        }
        if (name.Equals("audiodg", StringComparison.OrdinalIgnoreCase) || name.Equals("explorer", StringComparison.OrdinalIgnoreCase)
            || name.Equals("shellexperiencehost", StringComparison.OrdinalIgnoreCase)) return System;
        return Other;
    }

    /// <summary>Glyphe Segoe Fluent Icons de la tranche.</summary>
    public static string IconOf(string category) => category switch
    {
        Games => "",
        Music => "",
        Chat => "",
        Browser => "",
        System => "",
        _ => "",
    };
}
