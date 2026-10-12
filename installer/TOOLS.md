# 构建工具

NSIS 3.12 归档从本工作区已保存的 `DEV_FanOrder/installer/tools/nsis-3.12-official.zip` 复制。
原官方来源：https://sourceforge.net/projects/nsis/files/NSIS%203/3.12/nsis-3.12.zip
SHA-256：`56581F90DB321581C5381193D796FFFCF2D24B2F8FED2160A6C6A3BAA67F2C4F`。
构建脚本逐文件核对编译器、插件、头文件与归档；该指纹不等于签名认证。

PyInstaller 6.22.3 只在开发环境用于打包独立 OCR 进程。运行时依赖版本见项目 requirements.lock.txt。构建入口：

```powershell
.\.venv\Scripts\python.exe -m PyInstaller --noconfirm --distpath artifacts/worker-v0.2.0 --workpath artifacts/worker-build-v0.2.0 installer/WordLens.Worker.spec
.\.venv\Scripts\python.exe tools/build_installer.py --build-id 0.4.0-001
```

build-id 必须使用新的独立目录，不能覆盖已交付安装包。原版无签名安装器不会调用外部签名服务，也不会运行安装事务。只读检查使用同工作区受管理的 `DEV_FanOrder/installer/tools/7zip-full-26.03/7z.exe`。

源码构建请从上述官方地址下载相同 NSIS 归档到 installer/tools，并解压为 nsis-3.12；安装 PyInstaller 6.22.3 后执行所列 worker 构建，再生成安装包。Setup-WordLens.ps1 只准备源码运行环境。构建工具不随 Git 仓库提交。
