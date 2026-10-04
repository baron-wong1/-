import type { Api } from "./types";
// A browser preview starts empty and clearly explains desktop-only actions.
// Tests inject a separate bridge; no sample invoices are shipped to users.
const unavailable = async (): Promise<never> => {
  throw new Error("请在桌面应用中使用文件导入与导出；浏览器仅用于界面预览。");
};
export const api: Api = window.invoice ?? {
  chooseInput: unavailable,
  chooseOutput: unavailable,
  run: unavailable,
  cancel: unavailable,
  load: async () => null,
  save: async () => {},
  clearCache: async () => {},
  open: unavailable,
  pathForFile: () => "",
  onEvent: () => () => {},
};
