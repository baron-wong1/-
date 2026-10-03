# 发票整理助手

本机离线的发票整理桌面工具：导入 PDF / Word / 图片，核对识别结果，生成分类打印 PDF、合并 PDF 和 Excel 明细及分类汇总。

已实现 React + Electron 界面、Python 处理核心、中文 OCR、Word 转换、重复/冲突核对、人工修订、预览裁剪及多票拆分、任务取消、会话恢复和导出。PyInstaller 将核心和 OCR 模型打包，LibreOffice 随完整 Windows ZIP 提供，无需用户安装 Python、Node 或 Office。

## 获取和使用

Windows 10/11 x64 候选包由 [Windows 构建任务](https://github.com/baron-wong1/-/actions/workflows/windows-build.yml) 生成。任务通过后在 Artifacts 下载 `InvoiceAssistant-Windows-x64`，完整解压后双击 `发票整理助手.exe`。

**源码和本地自动验证已完成，Windows 构建及干净 Windows 断网实机验收分别记录。未通过构建的提交不视为可发行包。macOS 需在 Mac 分平台构建。**

[使用说明](docs/使用说明.md) · [开发、打包与验收](docs/开发与验收.md) · [设计说明](docs/superpowers/specs/2026-10-03-invoice-assistant-design.md)

0.1.1 对离线发行包进行依赖裁剪，保留现有功能；Office 裁剪前后须通过中文、表格、图片及分页渲染对照。构建产物附带组件体积清单，详见 [包体积优化](docs/包体积优化.md)。

## 工作流程

选择或拖入文件/文件夹 → 填写报销人和月份 → 核对待确认项 → 生成打印件和统计表。

- 支持 PDF、DOCX、DOC、JPG/JPEG、PNG，文本优先，扫描材料本地 OCR。
- 默认分类为住宿、车票、市交、退票费、其他，可自定义。
- 金额用定点小数，完整票号用字符串，缺失字段提示核对。
- 同号且关键字段一致的票据去重；冲突项人工核对。付款附件不计入金额。
- 同页多票可裁剪拆分，多页一票可设置页范围，输出前检查范围重叠。
- A4 每页最多两张，长票单页，截图附件最多半页；分类 PDF 和合并版包含页码，合并版有书签。
- Excel 六列：报销日期、报销人、发票内容、发票金额、开票日期、发票号码；保留总额和分类汇总。
- 原文件不改写，新建输出目录，错误逐项显示，自动保留本机会话。

## 开发

```sh
python -m pip install -r requirements-dev.txt
npm ci
npm run dev
```

开发环境需 LibreOffice。验证：`python -m pytest -q`、`npm run build`、`npm run test:ui -- --project=browser`。完整构建命令见开发说明。

## 文件与许可

真实发票、付款截图、报销表、生成的打印件和本机配置不进入仓库或公开构建。测试仅使用合成材料。应用不连接云端识别服务。

源码采用 [AGPL-3.0-or-later](LICENSE)，第三方组件许可随包保留，详见 [第三方说明](docs/THIRD_PARTY_NOTICES.md)。
