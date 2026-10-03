import { useEffect, useRef, useState } from "react";
import {
  X,
  ScanLine,
  Split,
  RotateCcw,
  ChevronLeft,
  ChevronRight,
} from "lucide-react";
import { api } from "../api";
import type { InvoiceRecord } from "../types";
interface Props {
  record: InvoiceRecord;
  categories: string[];
  isNew: boolean;
  onClose(): void;
  onSave(record: InvoiceRecord): Promise<void>;
  onSplit(record: InvoiceRecord): void;
  onCategory(value: string): void;
}
export default function ReviewDialog({
  record,
  categories,
  isNew,
  onClose,
  onSave,
  onSplit,
  onCategory,
}: Props) {
  const [draft, setDraft] = useState<InvoiceRecord>({
    ...record,
    warnings: [...record.warnings],
  });
  const [image, setImage] = useState("");
  const [page, setPage] = useState(record.page_start);
  const [error, setError] = useState("");
  const [working, setWorking] = useState(false);
  const [previewing, setPreviewing] = useState(true);
  const [custom, setCustom] = useState("");
  const dialog = useRef<HTMLDialogElement>(null);
  const imageBox = useRef<HTMLDivElement>(null);
  const origin = useRef<number[] | null>(null);
  useEffect(() => {
    dialog.current?.showModal();
  }, []);
  useEffect(() => {
    const id = crypto.randomUUID();
    let active = true;
    setPreviewing(true);
    setError("");
    api
      .run<{ image: string }>("preview", { record, page }, id)
      .then((result) => {
        if (active) setImage(result.image);
      })
      .catch((exc) => {
        if (active) {
          setImage("");
          setError(String(exc.message || exc));
        }
      })
      .finally(() => {
        if (active) setPreviewing(false);
      });
    return () => {
      active = false;
      void api.cancel(id).catch(() => {});
    };
  }, [record, page]);
  const patch = (values: Partial<InvoiceRecord>) =>
    setDraft((old) => ({
      ...old,
      ...values,
      resolution: values.resolution ?? null,
    }));
  async function save() {
    setWorking(true);
    setError("");
    try {
      await onSave({ ...draft, reviewed: true });
    } catch (exc) {
      setError(exc instanceof Error ? exc.message : String(exc));
    } finally {
      setWorking(false);
    }
  }
  async function recognize() {
    const id = crypto.randomUUID();
    setWorking(true);
    setError("");
    try {
      const response = await api.run<{ record: InvoiceRecord }>(
        "recognize",
        { record: draft },
        id,
      );
      setDraft(response.record);
    } catch (exc) {
      setError(exc instanceof Error ? exc.message : String(exc));
    } finally {
      setWorking(false);
    }
  }
  function point(event: React.PointerEvent) {
    const rect = imageBox.current!.getBoundingClientRect();
    return [
      Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width)),
      Math.max(0, Math.min(1, (event.clientY - rect.top) / rect.height)),
    ];
  }
  function move(event: React.PointerEvent) {
    if (!origin.current) return;
    const [x, y] = point(event),
      [x0, y0] = origin.current;
    patch({
      crop: [
        Math.min(x, x0),
        Math.min(y, y0),
        Math.max(x, x0),
        Math.max(y, y0),
      ],
    });
  }
  const crop = draft.crop;
  return (
    <dialog
      className="review-dialog"
      ref={dialog}
      aria-labelledby="review-title"
      onCancel={(event) => {
        event.preventDefault();
        if (!working) onClose();
      }}
    >
      <div className="dialog-heading">
        <div>
          <h2 id="review-title">{isNew ? "拆分新票据" : "核对票据"}</h2>
          <p>{record.filename}</p>
        </div>
        <button
          className="icon-button"
          aria-label="关闭核对"
          onClick={onClose}
          disabled={working}
        >
          <X size={21} />
        </button>
      </div>
      <div className="review-body">
        <section className="preview-panel" aria-label="原件预览">
          <div className="preview-toolbar">
            <span>原件预览</span>
            <div>
              <button
                className="icon-button"
                aria-label="上一页"
                disabled={page <= 1 || previewing}
                onClick={() => setPage((p) => p - 1)}
              >
                <ChevronLeft size={17} />
              </button>
              <span>
                {page} / {record.page_count}
              </span>
              <button
                className="icon-button"
                aria-label="下一页"
                disabled={page >= record.page_count || previewing}
                onClick={() => setPage((p) => p + 1)}
              >
                <ChevronRight size={17} />
              </button>
            </div>
          </div>
          <div className="preview-scroll">
            {previewing ? (
              <div className="preview-message">正在读取原件…</div>
            ) : image ? (
              <div
                className="preview-sheet"
                ref={imageBox}
                onPointerDown={(event) => {
                  if (working) return;
                  origin.current = point(event);
                  event.currentTarget.setPointerCapture(event.pointerId);
                }}
                onPointerMove={move}
                onPointerUp={(event) => {
                  move(event);
                  origin.current = null;
                  if (
                    draft.crop &&
                    (draft.crop[2] - draft.crop[0] < 0.02 ||
                      draft.crop[3] - draft.crop[1] < 0.02)
                  )
                    patch({ crop: null });
                }}
              >
                <img
                  src={image}
                  alt={`票据原件第 ${page} 页`}
                  draggable={false}
                />
                {crop && (
                  <div
                    className="crop-selection"
                    style={{
                      left: `${crop[0] * 100}%`,
                      top: `${crop[1] * 100}%`,
                      width: `${(crop[2] - crop[0]) * 100}%`,
                      height: `${(crop[3] - crop[1]) * 100}%`,
                    }}
                  />
                )}
              </div>
            ) : (
              <div className="preview-message">
                原件无法预览，请检查文件是否仍在原位置。
              </div>
            )}
          </div>
          <div className="preview-hint">
            在原件上拖动可选出票据区域；保留完整票号、二维码和印章。
          </div>
        </section>
        <form
          className="review-fields"
          onSubmit={(event) => {
            event.preventDefault();
            void save();
          }}
        >
          {error && (
            <div className="notice error-notice" role="alert">
              {error}
            </div>
          )}
          {draft.warnings.length > 0 && (
            <div className="notice">
              {draft.warnings.map((warning, i) => (
                <p key={i}>{warning}</p>
              ))}
            </div>
          )}
          <label>
            材料类型
            <select
              value={draft.kind}
              disabled={working}
              onChange={(e) =>
                patch({ kind: e.target.value as InvoiceRecord["kind"] })
              }
            >
              <option value="invoice">发票 · 计入报销金额</option>
              <option value="attachment">付款附件 · 不计入金额</option>
            </select>
          </label>
          <label>
            分类
            <select
              aria-label="票据分类"
              value={draft.category}
              disabled={working}
              onChange={(e) => patch({ category: e.target.value })}
            >
              {[...new Set([...categories, draft.category])].map((c) => (
                <option key={c}>{c}</option>
              ))}
            </select>
          </label>
          <div className="custom-category">
            <input
              aria-label="自定义分类"
              placeholder="添加自定义分类"
              value={custom}
              maxLength={40}
              disabled={working}
              onChange={(e) => setCustom(e.target.value)}
            />
            <button
              type="button"
              className="button small"
              disabled={working || !custom.trim()}
              onClick={() => {
                onCategory(custom.trim());
                patch({ category: custom.trim() });
                setCustom("");
              }}
            >
              添加
            </button>
          </div>
          {draft.kind === "invoice" && (
            <>
              <label>
                发票号码
                <input
                  value={draft.number}
                  inputMode="numeric"
                  maxLength={30}
                  placeholder="保留完整号码，包括开头的 0"
                  disabled={working}
                  onChange={(e) => patch({ number: e.target.value.trim() })}
                />
              </label>
              <div className="field-pair">
                <label>
                  开票日期
                  <input
                    type="date"
                    value={draft.date || ""}
                    disabled={working}
                    onChange={(e) => patch({ date: e.target.value || null })}
                  />
                </label>
                <label>
                  票面金额（元）
                  <input
                    inputMode="decimal"
                    value={draft.amount ?? ""}
                    placeholder="0.00"
                    disabled={working}
                    onChange={(e) => patch({ amount: e.target.value || null })}
                  />
                </label>
              </div>
            </>
          )}
          <div className="field-pair">
            <label>
              起始页
              <input
                type="number"
                min={1}
                max={record.page_count}
                value={draft.page_start}
                disabled={working}
                onChange={(e) => {
                  const start = Number(e.target.value);
                  patch({ page_start: start });
                  if (start >= 1 && start <= record.page_count) setPage(start);
                }}
              />
            </label>
            <label>
              结束页
              <input
                type="number"
                min={1}
                max={record.page_count}
                value={draft.page_end}
                disabled={working}
                onChange={(e) => patch({ page_end: Number(e.target.value) })}
              />
            </label>
          </div>
          {crop && (
            <div className="crop-controls">
              <span>
                选区：{crop.map((n) => Math.round(n * 100) + "%").join(" / ")}
              </span>
              <button
                type="button"
                className="text-button"
                disabled={working}
                onClick={() => patch({ crop: null })}
              >
                <RotateCcw size={14} />
                恢复整页
              </button>
            </div>
          )}
          <button
            className="button"
            type="button"
            onClick={() => void recognize()}
            disabled={working || previewing || !!error}
          >
            <ScanLine size={16} />
            {working ? "处理中…" : "重新识别选区"}
          </button>
          {record.status === "conflict" && (
            <label className="checkbox-label">
              <input
                type="checkbox"
                checked={draft.resolution === "keep"}
                disabled={working}
                onChange={(e) =>
                  setDraft((old) => ({
                    ...old,
                    resolution: e.target.checked ? "keep" : null,
                  }))
                }
              />
              我已核对原件，确认保留此同号冲突记录
            </label>
          )}
          <div className="field-help">
            保存后会重新校验；缺失字段和未解决的冲突会继续显示为待核对。
          </div>
          <button
            type="submit"
            className="button primary review-submit"
            disabled={working || previewing || !!error}
          >
            保存并确认
          </button>
        </form>
      </div>
      <div className="dialog-footer">
        <span>所有修改只影响整理结果，原文件保持不变。</span>
        <button
          className="button"
          onClick={() => onSplit(draft)}
          disabled={working}
        >
          <Split size={16} />
          从本文件拆分另一张
        </button>
      </div>
    </dialog>
  );
}
