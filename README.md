# AndroidTool

一个面向 Windows 的 Android APK 批量安装与调试工具，提供可视化界面，适合同时管理多个 APK，并通过 ADB 与 Android 设备通信。

## 主要功能

- 多选 APK，并在界面中显示 APK 信息
- 记录 OBB 文件目录，安装时自动复制 OBB
- 多个 APK 同时安装、卸载和启动
- 以卡片颜色直观显示当前选择状态
- 显示每个 APK 的安装、卸载和 OBB 复制进度
- 查看 Android 日志和 Unity 日志
- 导出日志文件
- 显示设备序列号、品牌、型号、Android 版本、电量、IP 地址和存储容量
- 通过 `cast_now` 投屏地址打开投屏页面
- 单文件、自包含 Windows x64 发布版本

## 技术栈

- C#
- .NET 10
- WPF
- xUnit
- Android Debug Bridge（ADB）
- Android Asset Packaging Tool（AAPT）

## 项目结构

```text
AndroidTool/
├─ AndroidTool.sln
├─ AndroidTool/           # WPF 应用源码
├─ AndroidTool.Tests/     # 自动化测试
├─ README.md
├─ LICENSE
└─ .gitignore
```

## 开发环境

建议使用：

- Windows 10/11 x64
- Visual Studio 2022 或支持 .NET 10 的 .NET SDK
- 已启用 Windows Desktop 开发工作负载

## 编译

在项目根目录执行：

```powershell
dotnet restore .\AndroidTool.sln
dotnet build .\AndroidTool.sln --configuration Release --no-restore
dotnet test .\AndroidTool.Tests\AndroidTool.Tests.csproj --configuration Release --no-restore
```

## 发布单文件 EXE

```powershell
dotnet publish .\AndroidTool\AndroidTool.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output .\publish
```

发布配置会生成 Windows x64 自包含单文件程序。运行时不需要另外安装 .NET Runtime。

## ADB 和 AAPT

项目会将以下工具嵌入最终 EXE：

- `adb.exe`
- `AdbWinApi.dll`
- `AdbWinUsbApi.dll`
- `aapt.exe`

这些工具来自 Android SDK Platform Tools 或 Android SDK Build Tools。公开仓库前，请确认所使用版本的许可条件，并在仓库中补充对应的来源和版本信息。

## 设备要求

- Android 设备已开启 USB 调试
- Windows 已正确安装设备驱动
- 使用网络连接时，电脑与设备应处于可通信网络中

## 开源许可

本项目使用 MIT License，详见 [LICENSE](LICENSE)。第三方工具的许可仍以其各自的许可协议为准。
