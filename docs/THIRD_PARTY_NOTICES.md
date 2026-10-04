# 第三方许可

轻量版应用源码仍采用 AGPL-3.0-or-later，见根目录 LICENSE。发行包提供对应源码地址和构建说明。

| 当前发行组件 | 用途 | 许可 |
| --- | --- | --- |
| PDFsharp-GDI 6.2.3 | 保留原 PDF 内容并生成打印布局 | MIT |
| PdfPig 0.1.13 | 提取 PDF 文字 | Apache-2.0 |
| Microsoft.Extensions.*、Microsoft.Bcl.*、System.* 托管支持库 | PDF 库的小型依赖 | MIT |

`licenses/components.json` 列出实际发行的 NuGet 包、版本、版权、作者和项目地址；完整 MIT、Apache-2.0 和应用许可随包提供。系统 .NET Framework、Windows PDF/OCR 和用户已安装的 Office 不在发行包中重新分发，依照用户已有的系统及软件许可使用。

旧版 0.1.x 的 React/Electron、Python/PyMuPDF、RapidOCR/PaddleOCR、LibreOffice 等组件许可说明可查对应 Git 提交及历史发行包。它们不属于 0.2.0 轻量版发行内容。开发及验证所用的 Python、PyMuPDF、Pillow、openpyxl、.NET SDK 同样不随轻量包分发。

本应用候选 EXE 未进行 Authenticode 签名。源码地址：https://github.com/baron-wong1/- 。分发时应提供相应版本源码、构建方式和完整第三方许可。
