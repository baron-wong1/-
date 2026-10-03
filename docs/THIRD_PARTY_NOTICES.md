# 第三方组件

应用源码采用 AGPL-3.0-or-later。发行包包含以下组件的许可副本；修改和分发时须保留。使用 PyMuPDF/MuPDF 的免费许可意味着完整应用须遵守 AGPL；商业闭源发行需要另行获得 Artifex 商业许可。

| 组件 | 用途 | 许可 |
| --- | --- | --- |
| React / React DOM | 单页界面 | MIT |
| Electron / Chromium / Node.js | 桌面运行环境 | MIT 及各第三方许可（Electron 自带 notices） |
| Lucide | 图标 | ISC |
| Python | 处理运行时 | PSF |
| PyMuPDF / MuPDF | PDF 提取、预览、排版 | AGPL-3.0 / 商业双许可，本应用使用 AGPL |
| RapidOCR ONNX Runtime | 离线 OCR | Apache-2.0 |
| PaddleOCR 预训练模型（RapidOCR 随包 ONNX） | 中文检测、方向和识别 | Apache-2.0 |
| ONNX Runtime | 模型推理 | MIT |
| OpenCV | 图像预处理 | Apache-2.0 及第三方许可 |
| Pillow | 图片读取和方向校正 | HPND |
| openpyxl | Excel 输出 | MIT |
| LibreOffice 26.8.0.3 | Word 转换、字体和资源 | MPL-2.0 / LGPL 等，完整许可随 LibreOffice 目录保留 |
| Noto Sans CJK SC 2.004 | 非中文 Windows 上的 Word 中文字体回退 | SIL Open Font License 1.1，许可随 LibreOffice 目录保留 |
| Microsoft Visual C++ CRT | 随包的应用本地运行库（从 Windows 构建机合法 redist 文件取得） | Noto Sans CJK SC 2.004 | 非中文 Windows 上的 Word 中文字体回退 | SIL Open Font License 1.1，许可随 LibreOffice 目录保留 |
| Microsoft Visual Studio REDIST 条款 |
| PyInstaller | 独立核心打包 | GPL 及 bootloader exception，例外允许按应用自身许可分发 |

`python scripts/collect-licenses.py` 收集运行环境中的 notices，并补齐部分 wheel 未携带的上游许可。发行包中查看 `resources/licenses`、Electron 自带 `LICENSES.chromium.html` 和 LibreOffice 的许可文件。OCR 不在运行时下载模型。

源码地址：https://github.com/baron-wong1/- 。发行包的源代码对应其构建提交，分发二进制时须同时提供对应源码和构建说明。Windows ZIP 为未签名候选包；Authenticode 校验用于下载的 LibreOffice 组件，不表示本应用已有签名。
