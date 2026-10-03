const { app, BrowserWindow, ipcMain, dialog, shell } = require("electron");
const { spawn, execFile } = require("node:child_process");
const fs = require("node:fs/promises");
const path = require("node:path");
const readline = require("node:readline");
const crypto = require("node:crypto");

if (process.env.INVOICE_TEST_USER_DATA)
  app.setPath("userData", path.resolve(process.env.INVOICE_TEST_USER_DATA));

app.commandLine.appendSwitch("lang", "zh-CN");

let window;
const jobs = new Map();
const approvedOutputs = new Set();
let stateWrite = Promise.resolve();
const root = path.resolve(__dirname, "..");
const isDev = !app.isPackaged && process.env.INVOICE_DEV === "1";
const userRoot = () => app.getPath("userData");
const cacheRoot = () => path.join(userRoot(), ".invoice-assistant-cache");
const statePath = () => path.join(userRoot(), "session.json");

function validateSender(event) {
  if (
    !window ||
    event.sender !== window.webContents ||
    event.senderFrame !== window.webContents.mainFrame
  ) {
    throw new Error("无效的窗口请求");
  }
}

function runCore(op, args = {}, id = crypto.randomUUID()) {
  if (jobs.has(id)) throw new Error("任务编号重复");
  const operations = new Set([
    "import",
    "export",
    "reconcile",
    "preview",
    "recognize",
    "doctor",
  ]);
  if (!operations.has(op)) throw new Error("不支持的操作");
  const packaged = app.isPackaged;
  const executable = packaged
    ? path.join(
        process.resourcesPath,
        "core",
        process.platform === "win32" ? "invoice-core.exe" : "invoice-core",
      )
    : process.env.INVOICE_PYTHON ||
      (process.platform === "win32" ? "python" : "python3");
  const commandArgs = packaged ? [] : [path.join(root, "core", "worker.py")];
  const child = spawn(executable, commandArgs, {
    cwd: packaged ? userRoot() : root,
    env: {
      ...process.env,
      PYTHONUNBUFFERED: "1",
      PYTHONIOENCODING: "utf-8",
      INVOICE_RESOURCES: packaged
        ? process.resourcesPath
        : process.env.INVOICE_RESOURCES || root,
    },
    windowsHide: true,
    detached: process.platform !== "win32",
    stdio: ["pipe", "pipe", "pipe"],
  });
  const entry = { child, cancelled: false, op };
  jobs.set(id, entry);
  return new Promise((resolve, reject) => {
    let result,
      failure,
      stderr = "";
    const lines = readline.createInterface({ input: child.stdout });
    lines.on("line", (line) => {
      let message;
      try {
        message = JSON.parse(line);
      } catch {
        return;
      }
      if (message.event === "result") result = message.result;
      else if (message.event === "error") failure = message.message;
      else if (
        message.event === "staging" &&
        op === "export" &&
        path.dirname(message.directory) === path.resolve(args.destination) &&
        path.basename(message.directory).startsWith(".invoice-export-")
      )
        entry.stagePath = message.directory;
      else if (!window?.isDestroyed())
        window.webContents.send("core-event", { id, ...message });
    });
    child.stderr.on("data", (chunk) => {
      stderr = (stderr + chunk.toString()).slice(-6000);
    });
    child.stdin.on("error", () => {});
    child.on("error", (error) => {
      failure = "无法启动处理核心：" + error.message;
    });
    child.on("close", async (code) => {
      jobs.delete(id);
      lines.close();
      if (entry.stagePath)
        await fs
          .rm(entry.stagePath, { recursive: true, force: true })
          .catch(() => {});
      if (entry.cancelled) return reject(new Error("任务已取消"));
      if (failure || code !== 0 || result === undefined) {
        return reject(
          new Error(failure || "处理核心意外退出，请重试或检查组件完整性"),
        );
      }
      if (op === "export") {
        approvedOutputs.add(path.resolve(result.directory));
        result.files.forEach((file) => approvedOutputs.add(path.resolve(file)));
      }
      resolve(result);
    });
    child.stdin.end(
      JSON.stringify({
        ...args,
        op,
        cache: cacheRoot(),
        excluded: [
          root,
          ...(packaged ? [process.resourcesPath] : []),
          cacheRoot(),
        ],
      }) + "\n",
    );
  });
}

async function terminate(entry) {
  entry.cancelled = true;
  const pid = entry.child.pid;
  if (!pid) return;
  if (process.platform === "win32") {
    await new Promise((resolve) =>
      execFile(
        "taskkill",
        ["/PID", String(pid), "/T", "/F"],
        { windowsHide: true },
        resolve,
      ),
    );
  } else {
    try {
      process.kill(-pid, "SIGKILL");
    } catch {}
  }
}

function installHandlers() {
  const handle = (name, fn) =>
    ipcMain.handle(name, (event, ...args) => {
      validateSender(event);
      return fn(...args);
    });
  handle("choose-input", async (kind) => {
    if (kind !== "files" && kind !== "folder") throw new Error("选择类型无效");
    const response = await dialog.showOpenDialog(window, {
      title: kind === "folder" ? "选择发票文件夹" : "选择发票文件",
      properties:
        kind === "folder" ? ["openDirectory"] : ["openFile", "multiSelections"],
      filters: [
        {
          name: "发票与附件",
          extensions: ["pdf", "docx", "doc", "jpg", "jpeg", "png"],
        },
      ],
    });
    return response.canceled ? [] : response.filePaths;
  });
  handle("choose-output", async () => {
    const result = await dialog.showOpenDialog(window, {
      title: "选择输出位置",
      properties: ["openDirectory", "createDirectory"],
    });
    return result.canceled ? null : result.filePaths[0];
  });
  handle("run-core", (op, args, id) => {
    if (
      typeof id !== "string" ||
      id.length > 80 ||
      !args ||
      typeof args !== "object"
    )
      throw new Error("任务参数无效");
    if (
      (op === "import" || op === "export") &&
      [...jobs.values()].some((job) => ["import", "export"].includes(job.op))
    ) {
      throw new Error("请等待当前任务结束");
    }
    return runCore(op, args, id);
  });
  handle("cancel-core", async (id) => {
    const entry = jobs.get(id);
    if (entry) await terminate(entry);
  });
  handle("load-session", async () => {
    try {
      const state = JSON.parse(await fs.readFile(statePath(), "utf-8"));
      if (state.version !== 1 || !Array.isArray(state.records)) return null;
      return state;
    } catch {
      return null;
    }
  });
  handle("save-session", (state) => {
    if (
      state.version !== 1 ||
      !Array.isArray(state.records) ||
      state.records.length > 100000
    )
      throw new Error("会话数据无效");
    const contents = JSON.stringify(state);
    stateWrite = stateWrite
      .catch(() => {})
      .then(async () => {
        await fs.mkdir(userRoot(), { recursive: true });
        const temporary = statePath() + ".tmp";
        await fs.writeFile(temporary, contents, {
          encoding: "utf-8",
          mode: 0o600,
        });
        await fs.rename(temporary, statePath());
      });
    return stateWrite;
  });
  handle("open-output", async (target) => {
    if (
      typeof target !== "string" ||
      !approvedOutputs.has(path.resolve(target))
    )
      throw new Error("请打开本次生成的文件");
    const failure = await shell.openPath(target);
    if (failure) throw new Error(failure);
  });
  handle("clear-cache", async () => {
    if (jobs.size) throw new Error("请先结束当前任务");
    await fs.rm(cacheRoot(), { recursive: true, force: true });
  });
}

app.whenReady().then(async () => {
  await fs.mkdir(userRoot(), { recursive: true });
  installHandlers();
  window = new BrowserWindow({
    width: 1220,
    height: 850,
    minWidth: 850,
    minHeight: 620,
    title: "发票整理助手",
    backgroundColor: "#ffffff",
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(__dirname, "preload.cjs"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      webSecurity: true,
      spellcheck: false,
    },
  });
  let closing = false;
  window.on("close", (event) => {
    if (closing) return;
    event.preventDefault();
    closing = true;
    for (const entry of jobs.values()) void terminate(entry);
    void stateWrite.catch(() => {}).finally(() => window.destroy());
  });
  window.webContents.setWindowOpenHandler(() => ({ action: "deny" }));
  window.webContents.on("will-navigate", (event) => event.preventDefault());
  window.webContents.session.setPermissionRequestHandler(
    (_contents, _permission, callback) => callback(false),
  );
  // Production renderer and its documents have no internet access.
  if (!isDev)
    window.webContents.session.webRequest.onBeforeRequest(
      { urls: ["http://*/*", "https://*/*"] },
      (_details, callback) => callback({ cancel: true }),
    );
  if (isDev) await window.loadURL("http://127.0.0.1:5173");
  else await window.loadFile(path.join(root, "dist", "index.html"));
});
app.on("window-all-closed", () => app.quit());
app.on("before-quit", () => {
  for (const entry of jobs.values()) void terminate(entry);
});
