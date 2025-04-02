
<p align="center">
  <a href="https://kitx.apps.crequency.com" target="_blank" rel="noopener noreferrer">
    <img width="128" src="https://github.com/Crequency/KitX/assets/50760269/d2f5ee3b-5e01-49d7-ae69-19318a74b8c2" alt="KitX Logo">
  </a>
</p>

<br>

<p align="center">
  Docs website: <a href="https://kitx.docs.crequency.com/en/">English</a> | <a href="https://kitx.docs.catrol.cn/">简体中文</a>
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

<p align="center">
    <img src="https://profile-counter.glitch.me/Crequency-KitX/count.svg"></img>
</p>

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

```plaintext
Plugin <-> Loader \
                   \
Plugin <-> Loader <-> Dashboard <-> User
                   /
Plugin <-> Loader /
```

The third party is responsible for referring to the documentation to implement the interface that the Plugin should implement.

How to implement the different frameworks of each language and the Loaders chosen to implement are different.

Each language or framework will have a corresponding Loader to achieve interoperability with Plugin, and Loader communicates with Dashboard through Socket, reporting the situation and passing commands.

Each of these three-layer designs can be replaced, and any layer can be customized or a third-party solution can be used.

In this way, plug-ins on other devices in the LAN can also be connected to the current device, so LAN interconnection can be achieved.

## Requirements

| platforms                                                                                 | versions                                          | x86           | arm                         | risc-v | mips | loongarch                                   |
|-------------------------------------------------------------------------------------------|---------------------------------------------------|---------------|-----------------------------|--------|------|---------------------------------------------|
| ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white)      | 10, 11                                            | `x32` , `x64` | `arm` , `arm64`             | :x:    | :x:  | :x:                                         |
| ![Linux](https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black)            | -                                                 | `x64`         | `arm` , `arm64`             | :x:    | :x:  | `loongarch32 (ToDo)` , `loongarch64 (ToDo)` |
| ![MacOS](https://img.shields.io/badge/mac%20os-000000?logo=macos&logoColor=F0F0F0)        | -                                                 | `x64`         | `arm64`                     | :x:    | :x:  | :x:                                         |
| ![Android](https://img.shields.io/badge/Android-3DDC84?logo=android&logoColor=white)      | Android 5.0 + (min: 21, compiled: 33, target: 33) | `x64`         | `armeabi-v7a` , `arm64-v8a` | :x:    | :x:  | :x:                                         |
| ![iOS](https://img.shields.io/badge/iOS-000000?logo=ios&logoColor=white)                  | iOS 12.0 +                                        | :x:           | `arm64`                     | :x:    | :x:  | :x:                                         |
| ![Raspberry Pi](https://img.shields.io/badge/-RaspberryPi-C51A4A?logo=Raspberry-Pi)       | -                                                 | :x:           | :x:                         | :x:    | :x:  | :x:                                         |
| ![Browser](https://img.shields.io/badge/Browser-4285F4?logo=GoogleChrome&logoColor=white) | -                                                 | :x:           | :x:                         | :x:    | :x:  | :x:                                         |

# Development

> We strongly suggest you to configure your ssh environment,
> in order to use git link format like “git@github.com:Crequency/KitX.git”
> instead of "https://github.com/Crequency/KitX.git"

1. Get source code

```shell
git clone git@github.com:Crequency/KitX.git
cd KitX
```

2. Init submodules

```shell
git submodule update --init --recursive
```

3. Setup

```shell
cheese setup --reference
```

# Versions Roadmap

<br>

<details>
<summary>Deprecated Versions</summary>

<br>

| Version                                                                 | Info    | Code                     | Support | Term                     | Require            | Runs on                                                                              |
|-------------------------------------------------------------------------|---------|--------------------------|---------|--------------------------|--------------------|--------------------------------------------------------------------------------------|
| Beta_10016                                                              | Beta    | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| Beta_10213                                                              | Beta    | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| Beta_10235                                                              | Beta    | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v1.0.0](https://github.com/Crequency/KitX/releases/tag/v1.0.0)         | Release | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v1.0.4](https://github.com/Crequency/KitX/releases/tag/v1.0.4)         | Release | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v1.0.5](https://github.com/Crequency/KitX/releases/tag/v1.0.5)         | Release | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v1.1.0](https://github.com/Crequency/KitX/releases/tag/v1.1.0)         | Release | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v1.1.1](https://github.com/Crequency/KitX/releases/tag/v1.1.1-v1.1.5)  | Release | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v1.1.2](https://github.com/Crequency/KitX/releases/tag/v1.1.1-v1.1.5)  | Release | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v1.1.4](https://github.com/Crequency/KitX/releases/tag/v1.1.1-v1.1.5)  | Release | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v1.1.5](https://github.com/Crequency/KitX/releases/tag/v1.1.1-v1.1.5)  | Release | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v1.2.0](https://github.com/Crequency/KitX/releases/tag/v1.2.0)         | Release | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v1.2.1](https://github.com/Crequency/KitX/releases/tag/v1.2.1)         | Release | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v1.2.2](https://github.com/Crequency/KitX/releases/tag/v1.2.2)         | Release | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v1.2.4](https://github.com/Crequency/KitX/releases/tag/v1.2.4-preview) | Preview | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v1.2.4](https://github.com/Crequency/KitX/releases/tag/v1.2.4)         | Release | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v1.2.5](https://github.com/Crequency/KitX/releases/tag/v1.2.5)         | Release | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v1.2.6](https://github.com/Crequency/KitX/releases/tag/v1.2.6)         | Release | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v1.2.7](https://github.com/Crequency/KitX/releases/tag/v1.2.7)         | Release | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v2.0.0](https://github.com/Crequency/KitX/releases/tag/v2.0.0)         | Release | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v2.0.1](https://github.com/Crequency/KitX/releases/tag/v2.0.1)         | Release | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v2.0.2](https://github.com/Crequency/KitX/releases/tag/v2.0.2)         | Release | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v2.0.3](https://github.com/Crequency/KitX/releases/tag/v2.0.3)         | Release | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v2.0.4](https://github.com/Crequency/KitX/releases/tag/v2.0.4)         | Release | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |
| [v2.0.5](https://github.com/Crequency/KitX/releases/tag/v2.0.5-preview) | Preview | This version has no code | :x:     | This version has no term | .Net Framework 4.8 | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) |

</details>

<br>

<details>
<summary>Not Finished Versions</summary>

<br>

| Version                                                                           | Info    | Code      | Support            | Term               | Require                                                       | Runs on                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
|-----------------------------------------------------------------------------------|---------|-----------|--------------------|--------------------|---------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| [v3.0.6187.47831](https://github.com/Crequency/KitX/releases/tag/v3.0.6187.47831) | Preview | Fly       | :x:                | 2022.04 -> 2023.04 | `Desktop`: .Net 6 (Also Self-Contained) <br> `Mobile`: Native | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) ![Linux](https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black) ![MacOS](https://img.shields.io/badge/mac%20os-000000?logo=macos&logoColor=F0F0F0)                                                                                                                                                                                                                                                                                                                                             |
| [v3.22.04.6230](https://github.com/Crequency/KitX/releases/tag/v3.22.04.6230)     | Preview | Telegram  | :x:                | 2022.04 -> 2023.04 | `Desktop`: .Net 6 (Also Self-Contained) <br> `Mobile`: Native | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) ![Linux](https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black) ![MacOS](https://img.shields.io/badge/mac%20os-000000?logo=macos&logoColor=F0F0F0)                                                                                                                                                                                                                                                                                                                                             |
| [v3.22.04.6235](https://github.com/Crequency/KitX/releases/tag/v3.22.04.6235)     | Release | Break     | :x:                | 2022.04 -> 2023.04 | `Desktop`: .Net 6 (Also Self-Contained) <br> `Mobile`: Native | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) ![Linux](https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black) ![MacOS](https://img.shields.io/badge/mac%20os-000000?logo=macos&logoColor=F0F0F0)                                                                                                                                                                                                                                                                                                                                             |
| [v3.22.04.6287](https://github.com/Crequency/KitX/releases/tag/v3.22.04.6287)     | Release | Evolution | :x:                | 2022.04 -> 2023.04 | `Desktop`: .Net 6 (Also Self-Contained) <br> `Mobile`: Native | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) ![Linux](https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black) ![MacOS](https://img.shields.io/badge/mac%20os-000000?logo=macos&logoColor=F0F0F0)                                                                                                                                                                                                                                                                                                                                             |
| [v3.23.04.6488](https://github.com/Crequency/KitX/releases/tag/v3.23.04.6488)     | Release | ToYou     | :white_check_mark: | 2023.04 -> 2024.04 | `Desktop`: .Net 6 (Also Self-Contained) <br> `Mobile`: Native | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) ![Linux](https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black) ![MacOS](https://img.shields.io/badge/mac%20os-000000?logo=macos&logoColor=F0F0F0) ![Android](https://img.shields.io/badge/Android-3DDC84?logo=android&logoColor=white) ![Raspberry Pi](https://img.shields.io/badge/-RaspberryPi-C51A4A?logo=Raspberry-Pi)                                                                                                                                                                    |

</details>

<br>

| Version                                                                           | Info    | Code      | Support            | Term               | Require                                                       | Runs on                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
|-----------------------------------------------------------------------------------|---------|-----------|--------------------|--------------------|---------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| v3.25.04.x                                                                        | Release | -         | developing         | 2024.10 -> 2025.04 | `Desktop`: .Net 8 (Also Self-Contained) <br> `Mobile`: Native | ![Windows](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white) ![Linux](https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black) ![MacOS](https://img.shields.io/badge/mac%20os-000000?logo=macos&logoColor=F0F0F0) ![Android](https://img.shields.io/badge/Android-3DDC84?logo=android&logoColor=white) ![iOS](https://img.shields.io/badge/iOS-000000?logo=ios&logoColor=white) ![Raspberry Pi](https://img.shields.io/badge/-RaspberryPi-C51A4A?logo=Raspberry-Pi) |

See details in [ChangeLog](./ChangeLog.md)

# Contributors

[![Contributors](https://contrib.rocks/image?repo=Crequency/KitX)](https://github.com/Crequency/KitX/graphs/contributors)

# Star History

<!-- [![Star History Chart](https://api.star-history.com/svg?repos=Crequency/KitX&type=Timeline)](https://star-history.com/#Crequency/KitX&Timeline) -->

[![Star History Chart](https://starchart.cc/Crequency/KitX.svg?variant=adaptive)](https://starchart.cc/Crequency/KitX)

# Thanks to

<p align="center">
  <a href="https://www.jetbrains.com/" target="_blank" rel="noopener noreferrer">
    <img width="128" src="https://resources.jetbrains.com/storage/products/company/brand/logos/jb_beam.svg" alt="JetBrains Logo (Main) logo">
  </a>
</p>

<p align="center">
    Thanks to the great tools from <a href="https://www.jetbrains.com/" target="_blank">JetBrains</a>, we can turn our ideas into reality.
</p>
