# Local Display Server

A self-hosted digital-signage server for Windows.

Local Display Server turns videos and still images into continuously available HLS streams that can be consumed by smart TVs, web browsers, Roku-style clients, media players, embedded devices, or custom applications on a local network.

The project started as an internal signage system and has been generalized so it can be reused for restaurants, retail menus, waiting rooms, event displays, internal dashboards, promotional screens, museums, schools, entertainment venues, offices, or other local signage workflows.

## What it does

- manages an arbitrary number of display endpoints;
- assigns each display a stable numeric ID and editable name;
- accepts video and image uploads through a web interface;
- normalizes uploaded media to a predictable H.264 MP4 profile;
- converts still images to 10-second still MP4 files;
- continuously loops media on the server with FFmpeg;
- publishes live HLS playlists at stable URLs;
- restarts failed FFmpeg processes automatically;
- exposes health and configuration APIs;
- includes a lightweight TV web player under `/vidaa/`;
- runs as a native Windows Service;
- includes a Windows desktop manager;
- includes a WiX-based MSI installer;
- builds and smoke-tests the installer with GitHub Actions.

## Architecture

```text
Browser / admin PC
       |
       | upload image/video
       v
Local Display Server
       |
       | FFmpeg normalization
       v
normalized MP4
       |
       | FFmpeg -stream_loop -1
       v
live HLS playlist
       |
       +--> browser / smart TV
       +--> VIDAA-style web player
       +--> Roku/custom client
       +--> VLC / HLS-compatible player
```

Each display has a stable stream URL:

```text
/hls/tv1/index.m3u8
/hls/tv2/index.m3u8
/hls/tv3/index.m3u8
...
```

Changing a display name or replacing its media does not change its stream URL.

## Requirements

For the Windows build:

- Windows 10/11 or Windows Server, x64;
- administrator rights for MSI installation;
- network access for clients;
- GitHub Actions or a local .NET 10 SDK if you want to build from source.

The generated server and manager applications are self-contained. A target machine does not need a separate .NET installation.

## Quick start

### Option 1 - GitHub Actions installer

The workflow `Build Windows Installer` creates:

```text
Local-Display-Server-Setup.msi
Local-Display-Server-Setup.sha256.txt
ffmpeg.version.txt
```

The MSI:

- installs the application under `C:\Program Files\Local Display Server\`;
- installs FFmpeg under `Tools\ffmpeg.exe`;
- installs the Windows service `LocalDisplayServer`;
- configures automatic service startup;
- configures service recovery after crashes;
- opens TCP port 8090 for the local subnet;
- installs Display Server Manager in the Start menu.

The installer is intentionally unsigned. Windows SmartScreen may warn about unsigned builds.

### Option 2 - run from source

Install .NET 10 and make FFmpeg available, then:

```powershell
dotnet run --project DisplayServer.Server.csproj
```

Open:

```text
http://localhost:8090/
```

When running from source, generated Data/Media/Hls files remain inside the repository by default.

## Configuration

Main configuration is in `appsettings.json`.

### Branding

```json
"Branding": {
  "Name": "Local Display Server",
  "Tagline": "Self-hosted media signage",
  "ServiceDisplayName": "Local Display Server"
}
```

`Name` and `Tagline` are exposed by `/api/system` and used by the web UI and TV web player.

### Listening address / port

```json
"Server": {
  "Urls": "http://0.0.0.0:8090"
}
```

You can also set:

```text
DISPLAY_SERVER_URLS=http://0.0.0.0:9000
```

If you change the default MSI port, update the Windows firewall rule accordingly.

### Data root

When installed as a Windows Service, the default data directory is:

```text
C:\ProgramData\Local Display Server\
```

Override it with:

```json
"DisplayServer": {
  "DataRoot": "D:\SignageData"
}
```

or:

```text
DISPLAY_SERVER_DATA_ROOT=D:\SignageData
```

Relative configured paths are resolved from the installed program directory.

### Streaming

```json
"Streaming": {
  "MediaFolder": "Media",
  "HlsFolder": "Hls",
  "DataFolder": "Data",
  "FfmpegPath": "Tools\\ffmpeg.exe",
  "SegmentSeconds": 4,
  "PlaylistSize": 8,
  "Tvs": {}
}
```

New installations start with no displays. Add them from the web interface.

## Media handling

Supported video extensions include:

```text
.mp4 .mov .m4v .mkv .webm .avi .mpeg .mpg
.wmv .flv .mts .m2ts .3gp
```

Supported image extensions include:

```text
.jpg .jpeg .png .webp .bmp .gif .tif .tiff
.avif .heic .heif .jfif
```

Actual decoding support depends on the FFmpeg build used.

Videos are normalized to a consistent H.264/AAC profile with a maximum 1920x1080 frame size and constant 30 fps.

Images use the first frame only and are converted to a 10-second still H.264 MP4.

The original base filename is preserved while the final extension becomes `.mp4`.

## Web administration

Open:

```text
http://<SERVER_IP>:8090/
```

From the web interface you can:

- create displays;
- rename displays;
- see stream state;
- upload/replace media;
- copy HLS URLs.

Uploads can be performed from any computer that can reach the server.

## Built-in TV web player

Open:

```text
http://<SERVER_IP>:8090/vidaa/
```

Although the directory is named `vidaa` for compatibility with the original target platform, the page is a standard HTML5 player and may work in other smart-TV browsers that support HLS video playback.

It stores the selected display ID in browser localStorage.

## Client API

Generic display list:

```http
GET /api/clients/tvs
```

Compatibility alias:

```http
GET /api/roku/tvs
```

Response:

```json
{
  "televisions": [
    {
      "id": "1",
      "name": "Lobby",
      "streamUrl": "/hls/tv1/index.m3u8"
    }
  ]
}
```

Full administration list:

```http
GET /api/tvs
```

Create a display:

```http
POST /api/tvs
Content-Type: application/json

{ "name": "Lobby" }
```

Rename a display:

```http
PUT /api/tvs/1/name
Content-Type: application/json

{ "name": "Main menu" }
```

Upload media:

```http
POST /api/tvs/1/video
Content-Type: multipart/form-data
```

Health:

```http
GET /health
```

System information:

```http
GET /api/system
```

Display diagnostics:

```http
GET /tv/1
```

## Windows Service reliability

The Windows MSI installs `LocalDisplayServer` as an automatic service.

WiX configures Windows Service Control Manager to restart it after failures.

Inside the application:

- the HLS supervisor checks each configured stream every few seconds;
- failed FFmpeg processes are recreated;
- an internal heartbeat monitors the supervisor itself;
- if the supervisor becomes unresponsive for a sustained period, the process exits intentionally so Windows can restart the service.

Runtime logs are written to:

```text
C:\ProgramData\Local Display Server\Logs\
```

## Display Server Manager

The MSI installs a WinForms utility named **Display Server Manager**.

It can:

- show Windows service state;
- show HLS health;
- show local network addresses;
- start, stop, or restart the service;
- open the web panel;
- open logs and data folders;
- import `Data` and `Media` from another installation.

Actions that require service control automatically request administrator privileges through UAC.

## Building the MSI locally

The normal build pipeline is defined in:

```text
.github/workflows/build-windows-installer.yml
```

It publishes the server and manager as self-contained `win-x64` applications, downloads and verifies FFmpeg, builds the WiX MSI, installs it on a clean Windows runner, verifies `/health`, and then uninstalls it.

The WiX project is:

```text
Installer/Windows/DisplayServer.Setup.wixproj
```

## Repository layout

```text
.
├── DisplayServer.Server.csproj
├── Program.cs
├── appsettings.json
├── Models/
├── Services/
├── Logging/
├── Manager/
│   └── DisplayServer.Manager.csproj
├── Installer/
│   └── Windows/
├── wwwroot/
│   └── vidaa/
├── Data/
├── Media/
└── Hls/
```

Runtime content under `Data`, `Media`, `Hls`, `Logs`, `bin`, `obj` and `artifacts` is ignored by Git.

## Security notes

This project is designed primarily for trusted local networks.

By default:

- the web administration API has no authentication;
- HLS playlists are accessible to LAN clients;
- the installer opens port 8090 to the local subnet.

Do **not** expose the service directly to the public Internet without adding authentication, TLS, access control, and an appropriate reverse proxy or VPN.

## Customization ideas

Common forks can add:

- authentication and roles;
- scheduled playlists;
- multi-item playlists per screen;
- image duration controls;
- audio controls;
- remote cloud access;
- device registration;
- analytics;
- multiple media profiles;
- Linux/systemd hosting;
- Chromecast, Android TV, Tizen or webOS clients.

The stable HLS URL convention makes it straightforward to build custom clients independently from the administration server.

## Licensing

The source code in this repository is released under the MIT License. See `LICENSE`.

The installer workflow may bundle FFmpeg. FFmpeg remains under its own license and is not relicensed by this repository. See `Installer/Windows/ThirdPartyNotices.txt`.

WiX and .NET also remain under their respective licenses.

## Contributions

Issues and pull requests that improve portability, reliability, documentation, or client compatibility are welcome.
