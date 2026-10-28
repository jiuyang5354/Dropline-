**中文** | [English](README.en.md)

# Dropline

Windows 上的拖拽暂存架。拖动文件时，屏幕右侧会弹出一个暂存架：先把文件放上去，切换窗口、桌面或应用之后，再从架子上把文件拖到目的地。

A drag-and-drop shelf for Windows. Start dragging a file and a shelf slides out at the right edge of the screen. Park files there, switch windows, then drag them back out wherever they need to go.

> 灵感来自 Mac 应用 [Yoink](https://eternalstorms.at/yoink/mac/)（Eternal Storms Software）。Dropline 是独立实现的开源项目，与 Yoink 及其作者没有任何关联，也没有使用其代码或素材。
> Inspired by Yoink for Mac by Eternal Storms Software. Dropline is an independent, open-source implementation and is not affiliated with Yoink or its developer.

## 功能 (v0.1)

- **拖动即出现**：从资源管理器或桌面开始拖动文件时，暂存架会自动从屏幕右侧滑出（可在托盘菜单里改成在任何应用中拖动都弹出）
- **放入 / 拖出**：放到暂存架上的文件只记录位置、不复制；拖出时就是真实的文件拖放，可以放进任何文件夹、聊天窗口、浏览器上传框或其他应用
- **多文件叠放**：一次拖进来的多个文件会合成一叠，拖出时一起带走；右键选“拆开这一叠”可以分开
- **图片、文字、链接**：从浏览器或编辑器拖来的图片、文字、网址，会自动存成 PNG、TXT、URL 文件放进剪藏文件夹
- **收起成小标签**：架子上有东西时，会收起成屏幕边缘的小标签，鼠标悬停即可展开
- **快捷键** `Ctrl + Alt + D` 随时打开 / 收起
- 托盘菜单：开机自启、拖出后保留、清空、打开剪藏文件夹
- 双击打开文件，右键可在资源管理器中显示、复制路径、移出暂存架
- 暂存架内容在重启后保留（存放于 `%AppData%\Dropline`）

## 下载与运行

在 [Releases](../../releases) 下载 `Dropline-Windows-vX.Y.Z.zip`，解压后运行 `Dropline.exe`。程序常驻托盘，无需安装。需要 Windows 10/11（系统自带 .NET Framework 4.8）。

## 构建

```powershell
cd windows
dotnet build -c Release
```

## 已知限制

- Windows 没有公开的“开始拖动文件”系统事件，自动弹出依靠全局鼠标钩子判断（按下后移动超过 10 像素，且不是在标题栏上拖动窗口）。
- 以管理员身份运行的窗口无法与普通权限的 Dropline 互相拖放，这是 Windows 的权限隔离机制（UIPI）。
- 目前只停靠在主显示器右侧。

## License

MIT
