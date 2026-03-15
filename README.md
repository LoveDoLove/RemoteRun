<!-- Improved compatibility of back to top link: See: https://github.com/othneildrew/Best-README-Template/pull/73 -->

<a id="readme-top"></a>

[![Contributors][contributors-shield]][contributors-url]
[![Forks][forks-shield]][forks-url]
[![Stargazers][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![Apache-2.0 License][license-shield]][license-url]

<br />
<div align="center">
  <a href="https://github.com/LoveDoLove/AdvancedRun-Rework">
    <img src="images/logo.png" alt="Windows" width="80" height="80">
  </a>

<h3 align="center">RemoteRun</h3>

  <p align="center">
    Lightweight .NET 8 Windows utility to run programs as <b>NT AUTHORITY\\SYSTEM</b> locally or remotely.
    <br />
    <a href="https://github.com/LoveDoLove/AdvancedRun-Rework/tree/main/RemoteRun"><strong>Explore the docs »</strong></a>
    <br />
    <br />
    <a href="https://github.com/LoveDoLove/AdvancedRun-Rework">View Project</a>
    &middot;
    <a href="https://github.com/LoveDoLove/AdvancedRun-Rework/issues/new?labels=bug">Report Bug</a>
    &middot;
    <a href="https://github.com/LoveDoLove/AdvancedRun-Rework/issues/new?labels=enhancement">Request Feature</a>
  </p>
</div>

<details>
  <summary>Table of Contents</summary>
  <ol>
    <li>
      <a href="#about-the-project">About The Project</a>
      <ul>
        <li><a href="#built-with">Built With</a></li>
      </ul>
    </li>
    <li>
      <a href="#getting-started">Getting Started</a>
      <ul>
        <li><a href="#prerequisites">Prerequisites</a></li>
        <li><a href="#installation">Installation</a></li>
      </ul>
    </li>
    <li><a href="#usage">Usage</a></li>
    <li><a href="#contributing">Contributing</a></li>
    <li><a href="#license">License</a></li>
    <li><a href="#contact">Contact</a></li>
    <li><a href="#acknowledgments">Acknowledgments</a></li>
  </ol>
</details>

## About The Project

`RemoteRun` is a Windows command-line tool that executes processes as `NT AUTHORITY\SYSTEM`:

- **Locally:** uses SYSTEM token duplication (`CreateProcessWithTokenW`) as the fast path.
- **Remotely:** copies the executable over `\\computer\admin$`, installs a temporary service, runs the command, captures output, and cleans up.

It is designed as a lightweight, dependency-free utility in .NET 8 and follows a dual-mode architecture where the same executable acts as both CLI client and service worker (`--service`).

<p align="right">(<a href="#readme-top">back to top</a>)</p>

### Built With

- [![.NET 8][dotnet-shield]][dotnet-url]
- [![C#][csharp-shield]][csharp-url]
- [![Windows][windows-shield]][windows-url]
- [![Inno Setup][inno-shield]][inno-url]

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Getting Started

Follow these steps to build and run the project locally.

### Prerequisites

- Windows machine
- [.NET SDK 8.0+](https://dotnet.microsoft.com/en-us/download)
- Administrator privileges (required at runtime; UAC elevation is automatic)
- Optional: [Inno Setup](https://jrsoftware.org/isinfo.php) for building installer binaries

### Installation

1. Clone the repository
   ```sh
   git clone https://github.com/LoveDoLove/AdvancedRun-Rework.git
   ```
2. Build the project
   ```sh
   dotnet build RemoteRun/RemoteRun.csproj -c Release
   ```
3. (Optional) Publish self-contained binaries
   ```sh
   dotnet publish RemoteRun/RemoteRun.csproj -c Release -r win-x64 --self-contained true -o ./publish/windows-latest-x64/RemoteRun
   dotnet publish RemoteRun/RemoteRun.csproj -c Release -r win-x86 --self-contained true -o ./publish/windows-latest-x86/RemoteRun
   ```
4. (Optional) Build installer packages
   ```sh
   ISCC.exe /DMyAppArch=x64 setup.iss
   ISCC.exe /DMyAppArch=x86 setup.iss
   ```

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Usage

```text
RemoteRun.exe [options] program [arguments]
RemoteRun.exe [options] \\computer program [arguments]
```

Options:

- `-w <directory>`: Working directory for launched process
- `-d`: Don't wait for process completion
- `-t <seconds>`: Timeout in seconds (default: `60`, `0` = unlimited)
- `-h`, `--help`, `/?`: Show help

Examples:

```bat
RemoteRun.exe
RemoteRun.exe cmd.exe
RemoteRun.exe cmd.exe "/c whoami /all"
RemoteRun.exe -w "C:\Windows\System32" cmd.exe "/c dir"
RemoteRun.exe \\192.168.1.100 cmd.exe "/c ipconfig /all"
RemoteRun.exe \\MYSERVER -t 120 powershell.exe "-Command Get-Process"
```

Additional notes:

- Running with **no arguments** opens an interactive `cmd.exe` as SYSTEM.
- If not elevated, the tool relaunches with UAC (`runas`) automatically.
- Interactive vs captured-output mode is selected automatically based on console redirection.

See the [module README](./RemoteRun/README.md) for deeper technical behavior.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

See the [open issues](https://github.com/LoveDoLove/AdvancedRun-Rework/issues) for a full list of proposed features (and known issues).

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Contributing

Contributions are welcome and appreciated.

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

<p align="right">(<a href="#readme-top">back to top</a>)</p>

### Top contributors:

<a href="https://github.com/LoveDoLove/AdvancedRun-Rework/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=LoveDoLove/AdvancedRun-Rework" alt="contrib.rocks image" />
</a>

## License

Distributed under the Apache License 2.0. See `LICENSE` for more information.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Contact

LoveDoLove

- GitHub: [LoveDoLove](https://github.com/LoveDoLove)
- Discord: https://discord.com/invite/FyYEmtRCRE
- Telegram Channel: https://t.me/lovedoloveofficialchannel

Project Link: [https://github.com/LoveDoLove/AdvancedRun-Rework](https://github.com/LoveDoLove/AdvancedRun-Rework)

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Acknowledgments

- [Microsoft .NET](https://dotnet.microsoft.com/)
- [Windows API documentation](https://learn.microsoft.com/windows/win32/api/)
- [Best-README-Template](https://github.com/othneildrew/Best-README-Template)

<p align="right">(<a href="#readme-top">back to top</a>)</p>

<!-- MARKDOWN LINKS & IMAGES -->

[contributors-shield]: https://img.shields.io/github/contributors/LoveDoLove/AdvancedRun-Rework.svg?style=for-the-badge
[contributors-url]: https://github.com/LoveDoLove/AdvancedRun-Rework/graphs/contributors
[forks-shield]: https://img.shields.io/github/forks/LoveDoLove/AdvancedRun-Rework.svg?style=for-the-badge
[forks-url]: https://github.com/LoveDoLove/AdvancedRun-Rework/network/members
[stars-shield]: https://img.shields.io/github/stars/LoveDoLove/AdvancedRun-Rework.svg?style=for-the-badge
[stars-url]: https://github.com/LoveDoLove/AdvancedRun-Rework/stargazers
[issues-shield]: https://img.shields.io/github/issues/LoveDoLove/AdvancedRun-Rework.svg?style=for-the-badge
[issues-url]: https://github.com/LoveDoLove/AdvancedRun-Rework/issues
[license-shield]: https://img.shields.io/github/license/LoveDoLove/AdvancedRun-Rework.svg?style=for-the-badge
[license-url]: https://github.com/LoveDoLove/AdvancedRun-Rework/blob/main/LICENSE
[dotnet-shield]: https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white
[dotnet-url]: https://dotnet.microsoft.com/
[csharp-shield]: https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=csharp&logoColor=white
[csharp-url]: https://learn.microsoft.com/dotnet/csharp/
[windows-shield]: https://img.shields.io/badge/Windows-API-0078D6?style=for-the-badge&logo=windows&logoColor=white
[windows-url]: https://learn.microsoft.com/windows/win32/
[inno-shield]: https://img.shields.io/badge/Inno_Setup-Installer-2F6DB5?style=for-the-badge
[inno-url]: https://jrsoftware.org/isinfo.php
