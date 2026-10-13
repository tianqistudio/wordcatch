# 单词捕手 · WordCatch

Windows 10/11 x64 取词助手。按 **Ctrl + Alt + D**，优先读取当前软件选中的文字；没有可读取的选区时，再识别鼠标位置的单词。

## 安装使用

当前版本 **v0.4.0**：[安装程序](https://github.com/tianqistudio/wordcatch/releases/latest)（约 166 MiB）。内置运行环境、OCR 模型和词典，无需另装 Python 或 .NET。

1. 安装时可独立选择桌面和开始菜单快捷方式，默认均勾选。
2. 启动后等待就绪。选中文字或把鼠标放在单词上，按快捷键查询。
3. 优先显示本地释义。可编辑原文重查，也可手动请求云端翻译。
4. 在「设置与关于」填写 Azure Translator 密钥及资源区域，启用备用翻译并测试连接。默认关闭，不含共享密钥。
5. 主窗口关闭后收起到通知区域，选择「退出」结束程序。快捷键冲突时可更换组合。

详见 [使用说明](docs/installed-guide.md)。本机开发启动入口为 Start-WordLens.ps1，不执行安装。

## 新版体验

- 猫爪与 W 平面图标，主窗口、托盘和安装器统一识别。
- 主窗口集中查询输入和快捷键；词库详情移到设置。
- 词卡保留「选中文字／屏幕识别」来源，区分单词释义与句子译文。
- 云端显示等待状态；新查询或修改原文会取消旧请求，旧结果不会覆盖当前内容。
- 安装升级将本程序旧名称快捷方式替换为「单词捕手」，保留旧版配置。

![单词捕手界面](docs/images/main-window.png)

## 本地词库与云端

- 770,611 条词典记录，其中 768,739 条有中文释义；含短语、专名和不同拼写，并非全部是独立英文单词。
- 仅含英文字母的词条 365,948 条；允许连字符和撇号后为 400,183 条。另有 58,313 条词形变化映射。
- 本地提供词典释义，不提供离线整句机器翻译。Azure 支持单词和短句，单次最多 2,000 字符。
- OCR 和词典在本地运行。云端仅接收查询文字，不上传截图；密钥通过 Windows 当前用户加密保护，费用取决于自己的 Azure 资源。
- 不保存查询历史，不监听或改写剪贴板。配置位于 %LOCALAPPDATA%\TianqiStudio\WordLens。

## 自动检测更新

在「设置与关于」选择 **自动 / tianqistudio.com / GitHub**。默认自动检查，每 6 小时最多一次，也可关闭或手动检查。双源自动比较版本，单源失败时使用另一源。仅请求公开版本信息，不上传查词内容；发现新版后提示打开发布页，不自动下载安装。

网站清单：[latest.json](https://tianqistudio.com/updates/wordcatch/latest.json)。网站提供检测信息，安装包由 GitHub Releases 托管，不代表两处都有安装包镜像。

## 开发

C# / WPF、Windows UI Automation、RapidOCR + ONNX Runtime CPU、ECDICT SQLite。快捷键复用 DesktopShortcuts。来源和许可见 [第三方说明](docs/third-party.md)。

新开发环境需要 Python 3.12 和 .NET 10 SDK，运行 Setup-WordLens.ps1 准备环境。Python 测试命令为 .venv\Scripts\python.exe -m unittest discover -s tests -v。

## 使用说明

选区读取依赖目标软件的 UI Automation 支持；不支持或超时时回退 OCR。复杂背景、小字体、动态画面及受保护窗口可能失败。没有自动悬停、发音、自动安装更新或开机启动。

## 品牌与兼容

可编辑图标在 [wordcatch.svg](src/WordLens/Assets/wordcatch.svg)，PNG 和多尺寸 ICO 同目录。为兼容旧版本，内部命名空间、程序文件名及安装／配置标识仍保留 WordLens。

## 源码与版权

源码公开供查看，版权与第三方许可见 [NOTICE.md](NOTICE.md)。

由 [tianqistudio](https://tianqistudio.com/home) 制作。
