# se7en Voice

Changeur de voix Windows simple (style Voicemeeter, en beaucoup plus facile), au design **Slipstream**.

Vrai micro → effets en temps réel → VB-CABLE, que Discord, les jeux et Windows utilisent comme micro.

## Structure
- `src/Se7enVoice.Core` — DSP (pitch, « voix plus grave », écho, réverb, robot, radio, EQ, compresseur, limiteur), presets, réglages. Sans dépendance Windows, testé.
- `src/Se7enVoice` — app WPF : moteur WASAPI (NAudio), interface Slipstream, raccourcis globaux, soundboard, icône de notification.
- `tests/Se7enVoice.Tests` — tests xUnit du DSP et des presets.

## Commandes
```bash
dotnet test tests/Se7enVoice.Tests
```
```bash
dotnet publish src/Se7enVoice -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist/publish
```

Le guide pour les utilisateurs est dans `dist/publish/LISEZMOI.txt`. Les réglages sont stockés dans `%AppData%\Se7enVoice`.

Polices : Saira Condensed, Josefin Sans, Bangers (SIL Open Font License, `Assets/Fonts/OFL.txt`).
