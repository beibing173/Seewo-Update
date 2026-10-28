# 希沃白板自动更新（Seewo-Update）

一个常驻系统托盘的 Windows 小工具：配置开机自启后，每次登录自动检查希沃白板 5 是否有新版本。

- 有新版本：后台下载并静默安装
- 没有新版本：不做任何事
- 全程托盘气泡提示，无安装向导、无弹窗打扰
- 使用 GitHub Actions 自动构建并发布

## 功能

| 功能 | 说明 |
| --- | --- |
| 开机自检 | 登录后延时检查一次，避免和系统启动争抢网络 |
| 版本检测 | 读取注册表卸载项中的「希沃白板 5」版本 |
| 最新版本查询 | 通过希沃官方下载地址 `https://e.seewo.com/download/file?code=EasiNote5` 的 302 跳转解析安装包版本 |
| 后台更新 | 下载官方安装包，使用 NSIS `/S` 参数静默安装，安装前自动结束正在运行的希沃白板 |
| 托盘 | 立即检查更新、开机自启开关、打开日志文件夹、退出 |
| 开机自启 | 创建“登录时运行 + 最高权限”的计划任务，登录时不弹 UAC |
| 日志 | `%ProgramData%\SeewoUpdate\logs`，保留 7 天 |

## 使用

1. 从 [Releases](https://github.com/beibing173/Seewo-Update/releases) 下载 `SeewoUpdate-win-x64.zip`。
2. 解压到一个固定目录，例如 `C:\Program Files\SeewoUpdate`（建议，不要放在桌面临时目录）。
3. 双击 `SeewoUpdate.exe`，在 UAC 提示中点击“是”（安装希沃白板需要管理员权限）。
4. 托盘出现蓝色更新图标后，右键勾选 **开机自动运行**。
5. 之后每次登录 Windows，程序都会在后台检查并静默更新，无需人工干预。

> 手动检查：右键托盘图标 → **立即检查更新**。
> 关闭自动更新：右键托盘图标 → 取消勾选 **开机自动运行**，并退出程序。

## 工作流程

```text
登录 Windows
  └─ 计划任务（最高权限）启动 SeewoUpdate.exe --autostart
       └─ 延时等待网络就绪
            ├─ 读取已安装的希沃白板版本（注册表）
            ├─ 请求官方下载地址，解析最新版本号
            ├─ 已是最新 → 静默退出，不做任何操作
            └─ 发现新版本
                 ├─ 托盘提示“发现新版本”
                 ├─ 下载官方安装包到 %ProgramData%\SeewoUpdate\downloads
                 ├─ 结束正在运行的希沃白板
                 ├─ 运行安装包 /S 静默安装
                 ├─ 校验安装后版本
                 └─ 托盘提示更新结果
```

## 本地构建

需要 .NET 8 SDK（Windows）：

```powershell
dotnet publish src/SeewoUpdate/SeewoUpdate.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o publish
```

产物为单个 `publish\SeewoUpdate.exe`。

## GitHub Actions

推送 `v*` 标签（如 `v1.0.0`）会自动构建并把 zip 发布到 GitHub Release：

```powershell
git tag v1.0.0
git push origin v1.0.0
```

普通推送会构建并上传 Artifact，便于验证。

## 说明与限制

- 仅处理已安装的希沃白板 5；未安装时只提示，不会自动安装。
- 静默安装使用官方安装包的 NSIS `/S` 参数，安装过程可能需要数十秒到数分钟。
- 更新期间会关闭正在运行的希沃白板；建议在开机后、尚未开始备课/上课时完成更新。
- 本工具不修改希沃白板的任何文件，只调用官方下载地址和官方安装包。
- 版本判断为逐段数字比较，例如 `5.2.4.10148 < 5.2.4.11582`。

## 项目结构

```text
src/SeewoUpdate/
  Program.cs             入口、单实例
  TrayApp.cs             托盘与交互
  Services/              版本检测、下载、安装、自启、日志
.github/workflows/       GitHub Actions 构建与发布
```
