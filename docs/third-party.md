# 第三方与共享组件来源

## 快捷键

源项目：`../DEV_FanOrder/artifacts/components/desktop-shortcuts-0.1.0/`。
本项目复制两个 0.1.0 NuGet 包到 `vendor/nuget/`，不依赖原项目的运行目录、配置或设备业务。SHA-256 见 `vendor/nuget/SHA256SUMS.txt`。

使用 `TianqiStudio.DesktopShortcuts` 与 WPF 适配层。宿主定义「屏幕取词」动作，并注入 WordLens 独立配置路径。未修改组件包。这是 tianqistudio 自有共享组件，本次随作者授权的源码与安装包发布。保留组件版权，不对其另行声明第三方开源许可证。

## OCR

RapidOCR：https://github.com/RapidAI/RapidOCR ，上游声明 Apache-2.0。
ONNX Runtime：https://github.com/microsoft/onnxruntime ，上游声明 MIT。
实际 Python 版本固定在 `requirements.lock.txt`；模型文件 SHA-256 见 `data/models.json`。模型由固定 RapidOCR 版本提供。该版本的 MODEL_LICENSES.md 明确覆盖这里使用的 PP-OCRv6 det/rec 和旧版分类模型，均为 PaddleOCR 衍生的 Apache-2.0 模型；模型版权归 Baidu／PaddleOCR 权利人。安装包 licenses 保留完整许可证、模型归属和转换说明；worker 内的 RapidOCR 模型注册表记录来源 URL 和 SHA-256。

## 英汉词典

ECDICT：https://github.com/skywind3000/ECDICT 。固定上游提交、CSV 哈希和导入条数见 `data/source.json`。
保留原始 `data/ecdict.csv` 和上游 `data/LICENSE`；SQLite 是本地派生查询索引，不替代来源。上游仓库声明 MIT；原始数据未改写释义，仅派生为 SQLite 查询索引。

安装包还保留 Python、.NET、NSIS 和实际 Python 依赖的许可证与 notices。Azure Translator 是用户自行配置的在线服务，不包含账户密钥。
