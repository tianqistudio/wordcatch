# 安装容器校验依赖

构建入口 `tools/build_installer.py` 与校验入口 `tools/verify_installer.py` 均按以下顺序解析 7-Zip：`--seven-zip`、`WORDCATCH_7ZIP` 环境变量、`PATH` 中的 `7z.exe` / `7z`。工具可位于任意本地目录，不依赖其他项目的位置；本仓库不复制二进制。

执行前按 `tools/seven-zip-toolchain.json` 核对 7-Zip 26.03 x64 的 `7z.exe` 与同目录 `7z.dll`。缺失或哈希不符即中止，不自动下载、不跳过检查。工具升级需要审查官方来源，更新两个文件及来源归档哈希，并重新执行测试。记录的是官方归档的下载指纹及展开文件哈希，不声称独立签名认证。

例如：`python tools/build_installer.py --build-id 0.7.0-new --seven-zip C:/Tools/7-Zip/7z.exe`。校验同样接受该参数；构建先做工具预检，再创建独立目录。验证回执包含实际解析路径和两个文件哈希。所有容器读取均为测试或流式解包到哈希，不执行安装器。
