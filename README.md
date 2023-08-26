
<p align="center">
  <a href="https://kitx.apps.catrol.cn/" target="_blank" rel="noopener noreferrer">
    <img width="128" src="https://source.catrol.cn/icons/Project/Catrol/KitX/KitX.png" alt="KitX Logo">
  </a>
</p>

<br>

<p align="center">
  Docs website: 
  <a href="https://crequency.github.io/KitX-Docs/en/">English</a> | <a href="https://crequency.github.io/KitX-Docs/">简体中文</a>
  🌐
</p>

<br>

<p align="center">
  <a href="https://github.com/Crequency/KitX/actions/workflows/build.yml"><img src="https://img.shields.io/github/actions/workflow/status/Crequency/KitX/build.yml?branch=main&label=Build%20Universal" alt="Build Universal"></a>
  <a href="https://github.com/Crequency/KitX/actions/workflows/build-loaders.yml"><img src="https://img.shields.io/github/actions/workflow/status/Crequency/KitX/build-loaders.yml?branch=main&label=Build%20Loaders" alt="Build Loaders"></a>
  <a href="https://github.com/Crequency/KitX/actions/workflows/build-plugins.yml"><img src="https://img.shields.io/github/actions/workflow/status/Crequency/KitX/build-plugins.yml?branch=main&label=Build%20Plugins" alt="Build Plugins"></a>
</p>

<p align="center">
  <a href="./LICENSE"><img src="https://img.shields.io/github/license/Crequency/KitX" alt="License"></a>
  <a href="https://github.com/Crequency/KitX/releases"><img src="https://img.shields.io/github/downloads/Crequency/KitX/total?color=%239F7AEA" alt="Release Downloads"></a>
  <a href="#"><img src="https://img.shields.io/github/repo-size/Crequency/KitX?color=%234682B4" alt="GitHub Repo Size"></a>
  <a href="#"><img src="https://img.shields.io/github/languages/code-size/Crequency/KitX" alt="Code Size"></a>
  <a href="https://github.com/Crequency/KitX/commits/"><img src="https://img.shields.io/github/commit-activity/m/Crequency/KitX" alt="Commit Activity"></a>
</p>

<p align="center">
  <a href="https://github.com/Crequency/KitX/network/members"><img src="https://img.shields.io/github/forks/Crequency/KitX?style=social" alt="Forks"></a>
  <a href="https://github.com/Crequency/KitX/stargazers"><img src="https://img.shields.io/github/stars/Crequency/KitX?style=social" alt="Stars"></a>
  <a href="https://github.com/Crequency/KitX/watchers"><img src="https://img.shields.io/github/watchers/Crequency/KitX?style=social" alt="Watches"></a>
  <a href="https://github.com/Crequency/KitX/discussions"><img src="https://img.shields.io/github/discussions/Crequency/KitX?style=social" alt="Discussions"></a>
</p>

<br>

<!--

![ScreenShot of About View](https://raw.githubusercontent.com/Dynesshely/SmallStorge/master/Crequency-KitX/screenshot_about.png)

<br>

<details>
<summary>More Screenshots</summary>

<br>

![ScreenShot of About View](https://raw.githubusercontent.com/Dynesshely/SmallStorge/master/Crequency-KitX/screenshot_plugins.png)
![ScreenShot of About View](https://raw.githubusercontent.com/Dynesshely/SmallStorge/master/Crequency-KitX/screenshot_devices.png)
![ScreenShot of About View](https://raw.githubusercontent.com/Dynesshely/SmallStorge/master/Crequency-KitX/screenshot_update.png)

</details>

<br>

-->

# About

> `KitX Project` is going to build a world that everything is connected.

KitX is an open, shared, connected and free tools platform.
After plugins developed by developers with their prefered languages and frameworks uploading to plugins market, users can download and combine plugins they like.
Every plugin contains atomized and platform independent functions, which will be connected with other functions by KitX.

For example:
0. KitX is running on user's all devices
1. User pressed `Ctrl + Alt + A` which has been assigned to 'Cast remote screenshot' function from plugin 'Screenshot'
2. This function asked KitX for a remote device and suspended to wait KitX's response
3. User selected device 'DESKTOP-Bedroom' and KitX returned user's selection to function above
4. Function then asked KitX to call 'Cast screenshot' function from plugin 'Screenshot' on device 'DESKTOP-Bedroom'
5. Remote returned a screenshot and displayed on user's current device by local plugin

# Architecture

KitX uses a three-layer design

`Third Party` Development =--> `Plugins` <--= Interop =--> `Loaders` <--= Socket Communication =--> `Dashboard` <--= UI Operation =--> User

The third party is responsible for referring to the documentation to implement the interface that the Plugin should implement.

How to implement the different frameworks of each language and the Loaders chosen to implement are different.

Each language or framework will have a corresponding Loader to achieve interoperability with Plugin, and Loader communicates with Dashboard through Socket, reporting the situation and passing commands.

Each of these three-layer designs can be replaced, and any layer can be customized or a third-party solution can be used.

In this way, plug-ins on other devices in the LAN can also be connected to the current device, so LAN interconnection can be achieved.

## Requirements

| platforms                                                                            | x86           | arm                         | loongarch                     |
|--------------------------------------------------------------------------------------|---------------|-----------------------------|-------------------------------|
| ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) | `x32` , `x64` | `arm` , `arm64`             | :x:                           |
| ![Linux](https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black)       | `x64`         | `arm` , `arm64`             | `la32 (todo)` , `la64 (todo)` |
| ![MacOS](https://img.shields.io/badge/mac%20os-000000?logo=macos&logoColor=F0F0F0)   | `x64`         | `arm64`                     | :x:                           |
| ![Android](https://img.shields.io/badge/Android-3DDC84?logo=android&logoColor=white) | `x64`         | `armeabi-v7a` , `arm64-v8a` | :x:                           |
| ![iOS](https://img.shields.io/badge/iOS-000000?logo=ios&logoColor=white)             | :x:           | `arm64`                     | :x:                           |

# Development

> We strongly suggest you to configure your ssh environment, in order to use git link format like “git@github.com:Crequency/KitX.git” instead of "https://github.com/Crequency/KitX.git"

1. Get source code

```shell
git clone git@github.com:Crequency/KitX.git
cd KitX
```

2. Init submodules

```shell
git submodule init
```

3. Select and init your development area

```shell
# Linux / MacOS
chmod +x ToolKits/start.sh
ToolKits/start.sh <type>

# or

# Windows OS
ToolKits/start.ps1 <type>
```

`<type>` is area you want to develop, you can choose `dashboard`, `mobile`, `loader`, `plugin`, `installer`
This script help you get source code of this area, include its dependencies.
If you want to get source code of all submodules at once, please execute following command instead:

```shell
git submodule update --init --recursive
```

# Versions Roadmap

<br>

<details>
<summary>Deprecated Versions</summary>

<br>

| Version                                                                 | Info    | Code       | Support | Term | Require            | Runs on |
|-------------------------------------------------------------------------|---------|------------|---------|------|--------------------|---------|
| Beta_10016                                                              | Beta    | Beta1      | :x:     | 0    | .Net Framework 4.8 | Windows |
| Beta_10213                                                              | Beta    | Beta2      | :x:     | 0    | .Net Framework 4.8 | Windows |
| Beta_10235                                                              | Beta    | Beta3      | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v1.0.0](https://github.com/Crequency/KitX/releases/tag/v1.0.0)         | Release | Hello      | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v1.0.4](https://github.com/Crequency/KitX/releases/tag/v1.0.4)         | Release | WoW        | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v1.0.5](https://github.com/Crequency/KitX/releases/tag/v1.0.5)         | Release | Nice Try   | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v1.1.0](https://github.com/Crequency/KitX/releases/tag/v1.1.0)         | Release | Apple      | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v1.1.1](https://github.com/Crequency/KitX/releases/tag/v1.1.1-v1.1.5)  | Release | Banana     | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v1.1.2](https://github.com/Crequency/KitX/releases/tag/v1.1.1-v1.1.5)  | Release | Cabbage    | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v1.1.4](https://github.com/Crequency/KitX/releases/tag/v1.1.1-v1.1.5)  | Release | Durin      | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v1.1.5](https://github.com/Crequency/KitX/releases/tag/v1.1.1-v1.1.5)  | Release | Grape      | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v1.2.0](https://github.com/Crequency/KitX/releases/tag/v1.2.0)         | Release | Herring    | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v1.2.1](https://github.com/Crequency/KitX/releases/tag/v1.2.1)         | Release | Wonderful  | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v1.2.2](https://github.com/Crequency/KitX/releases/tag/v1.2.2)         | Release | Abandon    | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v1.2.4](https://github.com/Crequency/KitX/releases/tag/v1.2.4-preview) | Preview | Panda      | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v1.2.4](https://github.com/Crequency/KitX/releases/tag/v1.2.4)         | Release | Panda      | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v1.2.5](https://github.com/Crequency/KitX/releases/tag/v1.2.5)         | Release | Orange     | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v1.2.6](https://github.com/Crequency/KitX/releases/tag/v1.2.6)         | Release | Muik       | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v1.2.7](https://github.com/Crequency/KitX/releases/tag/v1.2.7)         | Release | Cookie     | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v2.0.0](https://github.com/Crequency/KitX/releases/tag/v2.0.0)         | Release | Sea        | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v2.0.1](https://github.com/Crequency/KitX/releases/tag/v2.0.1)         | Release | Ocean      | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v2.0.2](https://github.com/Crequency/KitX/releases/tag/v2.0.2)         | Release | Calculator | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v2.0.3](https://github.com/Crequency/KitX/releases/tag/v2.0.3)         | Release | Wood       | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v2.0.4](https://github.com/Crequency/KitX/releases/tag/v2.0.4)         | Release | Computer   | :x:     | 0    | .Net Framework 4.8 | Windows |
| [v2.0.5](https://github.com/Crequency/KitX/releases/tag/v2.0.5-preview) | Preview | Laptop     | :x:     | 0    | .Net Framework 4.8 | Windows |

</details>

<br>

| Version                                                                           | Info    | Code      | Support            | Term               | Require                                                       | Runs on                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
|-----------------------------------------------------------------------------------|---------|-----------|--------------------|--------------------|---------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| [v3.0.6187.47831](https://github.com/Crequency/KitX/releases/tag/v3.0.6187.47831) | Preview | Fly       | :x:                | 2022.04 -> 2023.04 | `Desktop`: .Net 6 (Also Self-Contained) <br> `Mobile`: Native | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) ![Linux](https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black) ![MacOS](https://img.shields.io/badge/mac%20os-000000?logo=macos&logoColor=F0F0F0)                                                                                                                                                                                                                                                                                                                                             |
| [v3.22.04.6230](https://github.com/Crequency/KitX/releases/tag/v3.22.04.6230)     | Preview | Telegram  | :x:                | 2022.04 -> 2023.04 | `Desktop`: .Net 6 (Also Self-Contained) <br> `Mobile`: Native | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) ![Linux](https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black) ![MacOS](https://img.shields.io/badge/mac%20os-000000?logo=macos&logoColor=F0F0F0)                                                                                                                                                                                                                                                                                                                                             |
| [v3.22.04.6235](https://github.com/Crequency/KitX/releases/tag/v3.22.04.6235)     | Release | Break     | :x:                | 2022.04 -> 2023.04 | `Desktop`: .Net 6 (Also Self-Contained) <br> `Mobile`: Native | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) ![Linux](https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black) ![MacOS](https://img.shields.io/badge/mac%20os-000000?logo=macos&logoColor=F0F0F0)                                                                                                                                                                                                                                                                                                                                             |
| [v3.22.04.6287](https://github.com/Crequency/KitX/releases/tag/v3.22.04.6287)     | Release | Evolution | :x:                | 2022.04 -> 2023.04 | `Desktop`: .Net 6 (Also Self-Contained) <br> `Mobile`: Native | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) ![Linux](https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black) ![MacOS](https://img.shields.io/badge/mac%20os-000000?logo=macos&logoColor=F0F0F0)                                                                                                                                                                                                                                                                                                                                             |
| [v3.23.04.6488](https://github.com/Crequency/KitX/releases/tag/v3.23.04.6488)     | Release | ToYou     | :white_check_mark: | 2023.04 -> 2024.04 | `Desktop`: .Net 6 (Also Self-Contained) <br> `Mobile`: Native | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) ![Linux](https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black) ![MacOS](https://img.shields.io/badge/mac%20os-000000?logo=macos&logoColor=F0F0F0) ![Android](https://img.shields.io/badge/Android-3DDC84?logo=android&logoColor=white) ![Raspberry Pi](https://img.shields.io/badge/-RaspberryPi-C51A4A?logo=Raspberry-Pi)                                                                                                                                                                    |
| v3.24.10.x                                                                        | Release | -         | developing         | 2024.10 -> 2025.04 | `Desktop`: .Net 7 (Also Self-Contained) <br> `Mobile`: Native | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) ![Linux](https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black) ![MacOS](https://img.shields.io/badge/mac%20os-000000?logo=macos&logoColor=F0F0F0) ![Android](https://img.shields.io/badge/Android-3DDC84?logo=android&logoColor=white) ![iOS](https://img.shields.io/badge/iOS-000000?logo=ios&logoColor=white) ![Browser](https://img.shields.io/badge/Browser-4285F4?logo=GoogleChrome&logoColor=white) ![Raspberry Pi](https://img.shields.io/badge/-RaspberryPi-C51A4A?logo=Raspberry-Pi) |

See details in [ChangeLog](./ChangeLog.md)

# Contributors

<a href = "https://github.com/Crequency/KitX/graphs/contributors">
  <img src = "https://contrib.rocks/image?repo=Crequency/KitX"/>
</a>

<br>
<br>

<pre align="center">
██╗  ██╗    ██╗    ████████╗              ██╗  ██╗
██║ ██╔╝    ██║    ╚══██╔══╝              ╚██╗██╔╝
█████╔╝     ██║       ██║       █████╗     ╚███╔╝
██╔═██╗     ██║       ██║       ╚════╝     ██╔██╗
██║  ██╗    ██║       ██║                 ██╔╝ ██╗
╚═╝  ╚═╝    ╚═╝       ╚═╝                 ╚═╝  ╚═╝
</pre>
