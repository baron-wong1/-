import {
  FileText,
  Image,
  Pencil,
  Trash2,
  AlertCircle,
  CheckCircle2,
  Copy,
} from "lucide-react";
import { statusLabel, type InvoiceRecord, type Status } from "../types";
interface Props {
  records: InvoiceRecord[];
  busy: boolean;
  onEdit(record: InvoiceRecord): void;
  onRemove(id: string): void;
}
export default function InvoiceTable({
  records,
  busy,
  onEdit,
  onRemove,
}: Props) {
  return (
    <div className="table-scroll">
      <table className="invoice-table">
        <thead>
          <tr>
            <th className="file-column">文件 / 票据</th>
            <th>分类</th>
            <th>开票日期</th>
            <th className="amount-column">金额（元）</th>
            <th>状态</th>
            <th className="actions-column">操作</th>
          </tr>
        </thead>
        <tbody>
          {records.map((record) => {
            const status: Status =
              record.status ?? (record.error ? "error" : "pending");
            const Icon =
              status === "ready"
                ? CheckCircle2
                : status === "duplicate"
                  ? Copy
                  : AlertCircle;
            return (
              <tr
                key={record.id}
                className={status === "duplicate" ? "duplicate-row" : ""}
              >
                <td>
                  <div className="file-cell">
                    <span className="file-icon">
                      {/\.(jpg|jpeg|png)$/i.test(record.filename) ? (
                        <Image size={18} />
                      ) : (
                        <FileText size={18} />
                      )}
                    </span>
                    <div>
                      <div className="filename" title={record.source}>
                        {record.filename}
                      </div>
                      <div className="file-detail">
                        {record.page_count > 1
                          ? `第 ${record.page_start}${record.page_end !== record.page_start ? `–${record.page_end}` : ""} 页 · `
                          : ""}
                        {record.kind === "attachment"
                          ? "付款附件 · 不计入金额"
                          : record.number || "发票号码待核对"}
                        {record.crop ? " · 已裁剪" : ""}
                      </div>
                    </div>
                  </div>
                </td>
                <td>{record.category}</td>
                <td className="tabular">{record.date || "—"}</td>
                <td className="money tabular">
                  {record.kind === "attachment"
                    ? "—"
                    : record.amount == null
                      ? "待核对"
                      : Number(record.amount).toLocaleString("zh-CN", {
                          minimumFractionDigits: 2,
                          maximumFractionDigits: 2,
                        })}
                </td>
                <td>
                  <span
                    className={`status status-${status}`}
                    title={(record.issues || []).join("\n")}
                  >
                    <Icon size={13} />
                    {statusLabel[status]}
                  </span>
                </td>
                <td>
                  <div className="row-actions">
                    <button
                      className="icon-button"
                      aria-label={`核对 ${record.filename}`}
                      title="核对与预览"
                      onClick={() => onEdit(record)}
                      disabled={busy || !!record.error}
                    >
                      <Pencil size={16} />
                    </button>
                    <button
                      className="icon-button danger"
                      aria-label={`移除 ${record.filename}`}
                      title="移除这条记录，保留原文件"
                      onClick={() => onRemove(record.id)}
                      disabled={busy}
                    >
                      <Trash2 size={16} />
                    </button>
                  </div>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
      {records.length === 0 && (
        <div className="table-empty">没有符合筛选条件的票据</div>
      )}
    </div>
  );
}
