import { test, expect, type Page } from "@playwright/test";
import { spawn, execFileSync } from "node:child_process";
import readline from "node:readline";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";

let directory: string,
  chosen: string[],
  saved: any,
  outputs: string[],
  jobs: Map<string, ReturnType<typeof spawn>>;
const python =
  process.env.INVOICE_PYTHON ||
  (process.platform === "win32" ? "python" : "python3");
const worker = path.resolve("core/worker.py");

async function bridge(page: Page) {
  jobs = new Map();
  chosen = [];
  saved = null;
  outputs = [];
  await page.exposeFunction("qaChoose", async () => chosen);
  await page.exposeFunction("qaOutput", async () =>
    path.join(directory, "outputs"),
  );
  await page.exposeFunction("qaLoad", async () => saved);
  await page.exposeFunction("qaSave", async (state: any) => {
    saved = state;
  });
  await page.exposeFunction("qaOpen", async (target: string) => {
    outputs.push(target);
  });
  await page.exposeFunction("qaClear", async () => {
    await fs.rm(path.join(directory, "cache"), {
      recursive: true,
      force: true,
    });
  });
  await page.exposeFunction("qaCancel", async (id: string) => {
    jobs.get(id)?.kill("SIGKILL");
  });
  await page.exposeFunction(
    "qaRun",
    async (op: string, args: any, id: string) =>
      new Promise((resolve, reject) => {
        const child = spawn(python, [worker], {
          env: {
            ...process.env,
            PYTHONUNBUFFERED: "1",
            PYTHONIOENCODING: "utf-8",
          },
          stdio: ["pipe", "pipe", "pipe"],
        });
        jobs.set(id, child);
        let result: any,
          error = "",
          queue = Promise.resolve();
        const lines = readline.createInterface({ input: child.stdout });
        lines.on("line", (line) => {
          try {
            const event = JSON.parse(line);
            if (event.event === "result") result = event.result;
            else if (event.event === "error") error = event.message;
            else
              queue = queue.then(async () => {
                await page.evaluate(
                  (e) =>
                    window.dispatchEvent(
                      new CustomEvent("qa-core-event", { detail: e }),
                    ),
                  { id, ...event },
                );
              });
          } catch {}
        });
        child.stderr.on("data", () => {});
        child.on("close", async (code, signal) => {
          jobs.delete(id);
          await queue;
          code === 0
            ? resolve(result)
            : reject(
                new Error(signal ? "任务已取消" : error || "worker failure"),
              );
        });
        child.on("error", reject);
        child.stdin.end(
          JSON.stringify({
            ...args,
            op,
            cache: path.join(directory, "cache"),
          }) + "\n",
        );
      }),
  );
  await page.addInitScript(() => {
    const w = window as any;
    w.invoice = {
      chooseInput: w.qaChoose,
      chooseOutput: w.qaOutput,
      load: w.qaLoad,
      save: w.qaSave,
      open: w.qaOpen,
      clearCache: w.qaClear,
      cancel: w.qaCancel,
      run: w.qaRun,
      pathForFile: (f: File) => (f as any).qaPath || "",
      onEvent: (callback: any) => {
        const handler = (e: any) => callback(e.detail);
        window.addEventListener("qa-core-event", handler);
        return () => window.removeEventListener("qa-core-event", handler);
      },
    };
  });
}
function generate(
  name: string,
  number = "00123456789012345678",
  amount = "123.45",
  multi = false,
) {
  const output = path.join(directory, name);
  const content = `电子发票\n发票号码:${number}\n开票日期:2026年09月03日\n住宿服务\n价税合计(小写):￥${amount}`;
  // Arguments carry data; never interpolate filenames or document text as Python code.
  execFileSync(python, [
    "-c",
    `import fitz,sys,json\na=json.loads(sys.argv[1]);d=fitz.open();p=d.new_page(width=600,height=520 if a['multi'] else 260);p.insert_text((20,35),a['content'],fontname='china-s',fontsize=15,lineheight=1.6)\nif a['multi']:p.insert_text((20,295),a['content'].replace('00123456789012345678','00123456789012345679'),fontname='china-s',fontsize=15,lineheight=1.6)\nd.save(a['path'])`,
    JSON.stringify({ path: output, content, multi }),
  ]);
  return output;
}
async function imported(page: Page, paths: string[]) {
  chosen = paths;
  await page.getByRole("button", { name: "选择文件", exact: true }).click();
  await expect(page.getByText("导入完成，请核对待确认项目。")).toBeVisible({
    timeout: 30000,
  });
}
async function info(page: Page) {
  await page.getByLabel("报销人", { exact: true }).fill("合成测试");
  await page.getByLabel("报销月份", { exact: true }).fill("2026-09");
}

test.beforeEach(async ({ page }) => {
  directory = await fs.mkdtemp(path.join(os.tmpdir(), "invoice-ui-"));
  await bridge(page);
  await page.goto("/");
  await expect(
    page.getByRole("button", { name: "选择文件", exact: true }),
  ).toBeEnabled();
});
test.afterEach(async () => {
  for (const job of jobs.values()) job.kill();
  await fs.rm(directory, { recursive: true, force: true });
});

test("empty state, desktop and narrow viewport", async ({ page }) => {
  await expect(page.getByText("还没有导入票据")).toBeVisible();
  await expect(page.getByLabel("报销人", { exact: true })).toHaveValue("");
  await expect(
    page.getByRole("button", { name: "生成打印件和统计表" }),
  ).toBeDisabled();
  await page.screenshot({ path: "/tmp/invoice-ui-empty.png", fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
  ).toBe(true);
});

test("import, correct, export PDFs and Excel through real worker", async ({
  page,
}) => {
  await imported(page, [generate("invoice.pdf")]);
  await info(page);
  await expect(
    page.getByText("00123456789012345678", { exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "核对 invoice.pdf" }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByAltText("票据原件第 1 页")).toBeVisible();
  await dialog.getByLabel("票面金额（元）", { exact: true }).fill("200.10");
  await dialog.getByRole("button", { name: "保存并确认" }).click();
  await expect(dialog).not.toBeVisible();
  await expect(page.getByText("¥ 200.10", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "生成打印件和统计表" }).click();
  await expect(page.getByText("文件已生成", { exact: true })).toBeVisible();
  await expect(page.getByText(/1 张发票 · ¥ 200.10/)).toBeVisible();
  await page.getByRole("button", { name: "打开输出文件夹" }).click();
  expect(outputs.length).toBe(1);
  const files = await fs.readdir(outputs[0]);
  expect(files.filter((f) => f.endsWith(".pdf")).length).toBe(2);
  expect(files.some((f) => f.endsWith(".xlsx"))).toBe(true);
  await page.screenshot({
    path: "/tmp/invoice-ui-complete.png",
    fullPage: true,
  });
});

test("duplicate import, folder input, remove, restore and clear", async ({
  page,
}) => {
  const source = generate("invoice.pdf");
  chosen = [directory];
  await page.getByRole("button", { name: "选择文件夹", exact: true }).click();
  await expect(page.getByText("导入完成，请核对待确认项目。")).toBeVisible();
  chosen = [source];
  await page.getByRole("button", { name: "选择文件", exact: true }).click();
  await expect(page.getByText(/跳过 1 个/)).toBeVisible();
  await expect(
    page.getByRole("row").filter({ hasText: "invoice.pdf" }),
  ).toHaveCount(1);
  await page.waitForTimeout(450);
  await page.reload();
  await expect(page.getByText(/已恢复上次整理/)).toBeVisible();
  await page.getByRole("button", { name: "移除 invoice.pdf" }).click();
  await expect(page.getByText("还没有导入票据")).toBeVisible();
  await imported(page, [source]);
  await page.getByRole("button", { name: "清空列表", exact: true }).click();
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "清空列表", exact: true })
    .click();
  await expect(page.getByText("还没有导入票据")).toBeVisible();
  expect(await fs.stat(source)).toBeTruthy();
});

test("broken files block export until removed; dates stay independent", async ({
  page,
}) => {
  const source = generate("invoice.pdf");
  const broken = path.join(directory, "broken.pdf");
  await fs.writeFile(broken, "broken");
  await imported(page, [source, broken]);
  await info(page);
  await expect(page.getByText("导入失败", { exact: true })).toBeVisible();
  await expect(
    page.getByRole("button", { name: "生成打印件和统计表" }),
  ).toBeDisabled();
  await page.getByRole("button", { name: "移除 broken.pdf" }).click();
  await expect(
    page.getByRole("button", { name: "生成打印件和统计表" }),
  ).toBeEnabled();
  await page.getByLabel("报销月份", { exact: true }).fill("2026-10");
  await expect(
    page.getByRole("cell", { name: "2026-09-03", exact: true }),
  ).toBeVisible();
});

test("crop and split a page with two invoices", async ({ page }) => {
  await imported(page, [generate("two.pdf", undefined, undefined, true)]);
  await info(page);
  await page.getByRole("button", { name: "核对 two.pdf" }).click();
  const dialog = page.getByRole("dialog");
  const preview = dialog.getByAltText("票据原件第 1 页");
  await expect(preview).toBeVisible();
  const box = (await preview.boundingBox())!;
  await page.mouse.move(box.x + 2, box.y + 2);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width - 2, box.y + box.height * 0.48);
  await page.mouse.up();
  await dialog.getByRole("button", { name: "重新识别选区" }).click();
  await expect(dialog.getByLabel("发票号码", { exact: true })).toHaveValue(
    "00123456789012345678",
  );
  await dialog.getByRole("button", { name: "从本文件拆分另一张" }).click();
  await expect(page.getByRole("heading", { name: "拆分新票据" })).toBeVisible();
  const next = page.getByRole("dialog").getByAltText("票据原件第 1 页");
  await expect(next).toBeVisible();
  const b = (await next.boundingBox())!;
  await page.mouse.move(b.x + 2, b.y + b.height * 0.51);
  await page.mouse.down();
  await page.mouse.move(b.x + b.width - 2, b.y + b.height - 2);
  await page.mouse.up();
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "重新识别选区" })
    .click();
  await expect(
    page.getByRole("dialog").getByLabel("发票号码", { exact: true }),
  ).toHaveValue("00123456789012345679");
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "保存并确认" })
    .click();
  await expect(
    page.getByRole("button", { name: "生成打印件和统计表" }),
  ).toBeEnabled();
  await page.getByRole("button", { name: "生成打印件和统计表" }).click();
  await expect(page.getByText(/2 张发票 · ¥ 246.90/)).toBeVisible();
});

test("cancel task restores usable controls", async ({ page }) => {
  chosen = Array.from({ length: 30 }, (_, i) =>
    generate(`invoice-${i}.pdf`, String(10000000000000000000n + BigInt(i))),
  );
  await page.getByRole("button", { name: "选择文件", exact: true }).click();
  await page.getByRole("button", { name: "取消任务", exact: true }).click();
  await expect(
    page.getByText("已取消导入，已识别的项目已保留。"),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "选择文件", exact: true }),
  ).toBeEnabled();
});
