using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Se7enVoice.Services;

/// <summary>
/// Lit l'état des touches et boutons de souris partout dans Windows (même en jeu), pour le
/// push-to-talk. On interroge l'état ~60 fois par seconde au lieu d'installer un hook clavier.
/// </summary>
public static class KeyWatcher
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public static bool IsDown(int vk) => vk > 0 && (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>La touche du push-to-talk est tenue (Ctrl gauche ne compte pas quand c'est Alt Gr qui l'envoie).</summary>
    public static bool IsHeld(int vk) => IsDown(vk) && !(vk == 0xA2 && IsDown(0xA5));

    /// <summary>Première touche enfoncée (hors clic gauche, qui sert à cliquer sur le bouton), ou null.</summary>
    public static int? FirstPressed()
    {
        // Alt Gr envoie aussi un faux « Ctrl gauche » : sans ça, il serait pris pour Ctrl gauche
        if (IsDown(0xA5)) return 0xA5;
        for (int vk = 0x02; vk < 0xFF; vk++)
        {
            if (vk is 0x10 or 0x11 or 0x12) continue; // Shift/Ctrl/Alt génériques : on garde les versions gauche/droite
            if (IsDown(vk)) return vk;
        }
        return null;
    }

    public static string NameOf(int vk) => vk switch
    {
        0x02 => "Clic droit",
        0x04 => "Clic molette",
        0x05 => "Souris bouton 4",
        0x06 => "Souris bouton 5",
        0x08 => "Retour arrière",
        0x09 => "Tab",
        0x0D => "Entrée",
        0x14 => "Verr. Maj",
        0x1B => "Échap",
        0x20 => "Espace",
        0xA0 => "Maj gauche",
        0xA1 => "Maj droite",
        0xA2 => "Ctrl gauche",
        0xA3 => "Ctrl droit",
        0xA4 => "Alt gauche",
        0xA5 => "Alt Gr",
        0xC0 => "ù",
        0xDE => "²",
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
        >= 0x60 and <= 0x69 => "Pavé " + (vk - 0x60),
        _ => KeyInterop.KeyFromVirtualKey(vk).ToString(),
    };
}
