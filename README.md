# se7en Voice

Centrale audio tout-en-un pour streamers, gamers et créateurs, au design **Centrale Audio** (verre liquide, thème sombre) :

- **Studio vocal** (façon Voicemod) : 48 voix prêtes (Dark Vador, Ghostface, Autotune, voix de fille…), favoris, recherche, A/B, enregistrement .wav, push-to-talk.
- **Effets pro** : débruitage spectral, coupe-bas, de-esser, hauteur, formants (timbre), autotune, harmoniseur, robot, chuchotement, vibrato, distorsion, 8-bit, radio, filtre, égaliseur 5 bandes, compresseur, chorus, écho, réverb, limiteur.
- **Table de mixage** (façon Voicemeeter, sans pilote) : une tranche par type d'appli (jeux, musique, discussion, navigateur, système) avec fader, muet et vu-mètre ; ducking automatique ; envoi du son d'une appli dans le micro virtuel (Windows 11).
- **Soundboard** avec raccourcis globaux.
- **Voix IA** (RVC) : vraie conversion de voix en direct sur la carte graphique (ONNX Runtime + DirectML, toute marque),
  12 voix choisies parmi les 109 du corpus VCTK (CC BY 4.0) apprises par le modèle de base de RVC (MIT), hauteur
  automatique. Modèles (≈ 485 Mo) téléchargés au premier usage depuis la release `voix-ia-1`, vérifiés par SHA-256.
  Retard ≈ 0,25 s (blocs de 100 ms, fenêtre de 1 s, raccord SOLA).
- **Assistant micro** : 3 s de silence + 7 s de parole → volume Windows du micro, volume, anti-bruit, réduction de
  bruit et coupe-bas réglés tout seuls.
- **Seven Microphone** : le micro virtuel s'installe depuis l'appli (pilote gratuit VB-CABLE téléchargé sur le site officiel de VB-Audio, signature vérifiée, puis renommé).

Vrai micro → effets en temps réel → VB-CABLE, que Discord, les jeux et Windows utilisent comme micro.

## Structure
- `src/Se7enVoice.Core` — DSP (chaîne d'effets, FFT/STFT, débruitage, formants, autotune YIN…), presets, classement des applis du mixeur, réglages. Sans dépendance Windows, testé.
- `src/Se7enVoice` — app WPF : moteur WASAPI (NAudio), table de mixage (sessions audio Windows + capture « process loopback »), interface Centrale Audio (thème sombre), raccourcis globaux, push-to-talk, enregistreur, soundboard, icône de notification.
- `tests/Se7enVoice.Tests` — tests xUnit du DSP et des presets.

## Commandes
```bash
dotnet test tests/Se7enVoice.Tests
```
```bash
dotnet publish src/Se7enVoice -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist/publish
```

Le guide pour les utilisateurs est dans `dist/publish/LISEZMOI.txt`. Les réglages sont stockés dans `%AppData%\Se7enVoice`.

Polices : Geist, Geist Mono (SIL Open Font License, `Assets/Fonts/OFL.txt`).
