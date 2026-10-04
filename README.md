# 发票整理助手 · 轻量版

Windows 10/11 x64 的本机发票整理小工具。选择或拖入 PDF、图片和 Word 文件，核对票号、日期、金额与分类，生成分类打印 PDF、合并 PDF 和 Excel 统计表。

0.2.0 改为 C# 原生 Windows 界面，使用系统 .NET Framework 4.8。发行包只含应用和小型 PDF 库，不携带 Electron、Python、LibreOffice、OCR 模型，也不在启动后下载运行环境。构建对 ZIP 设置 10 MB 上限，实际大小记录于 `footprint.json`。

## 获取与使用

在 [Windows 构建任务](https://github.com/baron-wong1/-/actions/workflows/windows-build.yml) 的成功运行中下载 `InvoiceAssistant-Light-Windows-x64`。解压其中的发行 ZIP，双击 `发票整理助手.exe`。保留旁边的 DLL 和配置文件。

- 文本 PDF 直接提取文字；扫描 PDF、图片使用 Windows 已安装的中文文字识别。缺少中文识别时仍可预览、人工填写并导出。
- DOC/DOCX 转换使用本机 Microsoft Word；未安装时会逐项提示，先另存为 PDF 即可导入。
- 使用系统 .NET Framework 4.8（当前 Windows 10/11 通常已提供）；较早 Windows 10 若缺少此系统组件，需要先安装。发行包不内置这个运行环境。
- 无云端识别、账号或订阅。首次报销人和月份为空，自动恢复本机保存的记录。

默认类别为住宿、车票、市交、退票费、其他，可自定义。金额按十进制定点数计算；完整票号保存为字符串。同号一致自动排除，同号冲突需核对。付款附件不计入金额。多票同页可框选拆分，多页材料需确认范围。

A4 每页最多两张，长票单页，附件最多半页。保留原 PDF 文字及图形，合并版有连续页码和分类书签。Excel 六列及总额、分类汇总与有效发票一致。原文件不改写，输出写入新文件夹。

[使用说明](docs/使用说明.md) · [开发与验收](docs/开发与验收.md) · [体积与取舍](docs/包体积优化.md) · [第三方许可](docs/THIRD_PARTY_NOTICES.md)

## 构建

```sh
dotnet restore lightweight/InvoiceAssistant.csproj --locked-mode
dotnet build lightweight/InvoiceAssistant.csproj --no-restore -c Release -o build/lightweight
python scripts/package-lightweight.py
```

.NET SDK 和 Python 只用于开发打包，不进入发行包。Windows CI 用合成材料验证实际桌面导入、系统 PDF 预览、识别能力缺失提示、去重冲突、PDF/XLSX 导出、取消和原件完整性，并用另一套库读取输出。中文系统 OCR、本机 Office 转换、干净 Windows 10/11 断网启动和实际打印仍需对应系统的实机验收；CI 成功不代替这些验收。macOS 尚无此轻量版发行包。

源码采用 [AGPL-3.0-or-later](LICENSE)。真实发票及本机记录不进入仓库。旧版 0.1.x 的 Electron/Python 实现仅作为历史源码保留，当前发行流程只构建 `lightweight/`。
