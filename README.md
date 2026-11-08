<div align="center">

<img src="installer/build/appicon.png" width="112" alt="">

# Nessafy

Spotify in black and lace. A [Spicetify](https://spicetify.app) theme with a one-click installer.

**[Download NessafyInstaller.exe](https://github.com/deargodx/nessafy/releases/latest/download/NessafyInstaller.exe)**

</div>

## What you get

- **The Nessafy look:** black and lace, ivory serif type, see-through panels and a photo behind the main view. The colours Spotify pulls from cover art are toned down, so the lace stays the backdrop.
- **One click:** the installer sets up Spicetify for you if you don't have it, and adds the theme.
- **Stays on:** Spotify updates undo Spicetify. Nessafy notices and puts itself back, and picks up new versions of the theme from this repo.

## Install

1. You need Windows 10 or 11. Spicetify can only theme Spotify from [spotify.com/download](https://www.spotify.com/download), so if you don't have that, the installer gets it for you. If you have Spotify from the Microsoft Store, it asks first, then swaps it for the spotify.com version. Your playlists and library stay, but you'll log in again and re-download songs saved for offline listening.
2. Download **[NessafyInstaller.exe](https://github.com/deargodx/nessafy/releases/latest/download/NessafyInstaller.exe)** and run it. It isn't code-signed, so Windows may say it "protected your PC". Choose **More info**, then **Run anyway**.
3. Choose **Install**. Spotify restarts with Nessafy.

If you already use Spicetify, Nessafy becomes your theme and your extensions and custom apps stay. Uninstalling puts your old theme back.

### After Spotify updates

With **Keep Nessafy on after Spotify updates** ticked (the default), a scheduled task called **Nessafy** checks every few minutes. When Spotify has updated, it puts Nessafy back. If Spotify only just opened, it restarts it so you see the theme right away; otherwise the theme shows the next time Spotify opens. The same task checks this repo for a newer theme at most every six hours.

Its log is at `%APPDATA%\Nessafy\nessafy.log`.

### Uninstall

Open **Settings → Apps → Installed apps**, find **Nessafy** and choose **Uninstall**. You can also run the installer and choose **Uninstall**.

Spotify goes back to the theme you had before, or to plain Spotify if you had none. Spicetify itself stays installed, in case you use it for anything else.

### Command line

```
NessafyInstaller.exe --install | --repair | --uninstall | --status
    --keep | --no-keep              put Nessafy back after Spotify updates (default: keep)
    --theme <folder>                install a local build of the theme instead of the one inside
    --spotify-dir <folder>          Spotify's folder (default %APPDATA%\Spotify)
    --data-dir <folder>             Nessafy's data folder (default %APPDATA%\Nessafy)
    --spicetify-config <folder>     a separate Spicetify config folder
    --spicetify-state <folder>      a separate Spicetify state folder (its backup of Spotify)
    --no-restart                    never close or open Spotify
    --quiet                         no windows or questions
```

## Build it yourself

You need [Node.js](https://nodejs.org) 22 or newer.

```
node scripts/build.mjs
dist\NessafyInstaller.exe --install --theme dist\Nessafy
```

The theme is `theme/theme.css` and `theme/color.ini`. `theme/assets.css` holds the fonts and images, made from `assets/` by `python -I tools/make-assets.py` (needs [Pillow](https://pypi.org/project/pillow/)). `node scripts/build.mjs --theme-only` builds just the Spicetify theme into `dist\Nessafy`.

The installer is compiled with the C# compiler that comes with Windows, so it only builds on Windows. Releases are built by `.github/workflows/release.yml` on every push to `main`.

## Credits

- **deargod**, who made Nessafy.
- **bella**, for being fucking amazing.
- **[Spicetify](https://github.com/spicetify/cli)** and its contributors, which does the hard part.
- **[Cormorant Garamond](https://github.com/CatharsisFonts/Cormorant)** by the Cormorant Project Authors, under the [SIL Open Font License 1.1](assets/fonts/OFL.txt).

Nessafy's sister project is [Nessacord](https://github.com/deargodx/nessacord), the same look for Discord.

## Disclaimer

Nessafy isn't made by, or affiliated with, Spotify. Modifying the Spotify app is against Spotify's terms of use, so use it at your own risk.

## License

Nessafy is free software under the [GNU General Public License v3.0 or later](LICENSE).
