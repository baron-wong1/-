export const CATEGORIES = ["住宿", "车票", "市交", "退票费", "其他"];
export type Status = "ready" | "pending" | "duplicate" | "conflict" | "error";
export interface InvoiceRecord {
  id: string;
  source: string;
  filename: string;
  source_hash: string;
  render_path?: string;
  render_hash?: string;
  number: string;
  date: string | null;
  amount: string | null;
  category: string;
  kind: "invoice" | "attachment";
  page_start: number;
  page_end: number;
  page_count: number;
  crop: number[] | null;
  reviewed: boolean;
  method?: string;
  warnings: string[];
  issues?: string[];
  status?: Status;
  error?: string;
  resolution?: "keep" | null;
  travel_time?: string;
  duplicate_of?: string;
}
export interface Session {
  version: 1;
  records: InvoiceRecord[];
  person: string;
  month: string;
  categories: string[];
}
export interface ExportResult {
  directory: string;
  files: string[];
  count: number;
  amount: string;
  pages: number;
  duplicates: number;
}
export interface CoreEvent {
  id: string;
  event: string;
  record?: InvoiceRecord;
  current?: number;
  total?: number;
  filename?: string;
}
export interface Api {
  chooseInput(kind: "files" | "folder"): Promise<string[]>;
  chooseOutput(): Promise<string | null>;
  run<T>(op: string, args: Record<string, unknown>, id: string): Promise<T>;
  cancel(id: string): Promise<void>;
  load(): Promise<Session | null>;
  save(state: Session): Promise<void>;
  clearCache(): Promise<void>;
  open(path: string): Promise<void>;
  pathForFile(file: File): string;
  onEvent(callback: (event: CoreEvent) => void): () => void;
}
declare global {
  interface Window {
    invoice?: Api;
  }
}
export const statusLabel: Record<Status, string> = {
  ready: "已就绪",
  pending: "待核对",
  duplicate: "重复排除",
  conflict: "号码冲突",
  error: "导入失败",
};
export function cents(amount: string | null | undefined): bigint {
  if (!amount || !/^\d+(\.\d{1,2})?$/.test(amount)) return 0n;
  const [whole, fraction = ""] = amount.split(".");
  return BigInt(whole) * 100n + BigInt(fraction.padEnd(2, "0"));
}
export function formatMoney(value: bigint): string {
  return `${(value / 100n).toLocaleString("zh-CN")}.${(value % 100n).toString().padStart(2, "0")}`;
}
