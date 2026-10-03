import { test, expect, _electron as electron } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';

test('desktop IPC imports, previews, exports and restores actual files',async()=>{
  test.skip(process.env.INVOICE_ELECTRON_E2E!=='1','Run with a desktop display or packaged Windows CI');
  const root=await fs.mkdtemp(path.join(os.tmpdir(),'invoice-desktop-'));
  const input=path.join(root,'invoice.pdf'),output=path.join(root,'output');await fs.mkdir(output);
  const python=process.platform==='win32'?'python':'python3';
  execFileSync(python,['-c',`import fitz,sys\nd=fitz.open();p=d.new_page(width=600,height=260);p.insert_text((20,35),'电子发票\\n发票号码:00123456789012345678\\n开票日期:2026年09月03日\\n住宿服务\\n价税合计(小写):￥123.45',fontname='china-s',fontsize=15,lineheight=1.6);d.save(sys.argv[1])`,input]);
  const executablePath=process.env.INVOICE_E2E_PACKAGED;
  const launch=()=>electron.launch({...(executablePath?{executablePath,args:[]}:{args:[...(process.platform==='linux'?['--no-sandbox','--ozone-platform=headless','--disable-gpu']:[]),'.']}),
    env:{...process.env,INVOICE_TEST_USER_DATA:root}});
  let desktop=await launch();
  try{
    let page=await desktop.firstWindow();await page.waitForLoadState('domcontentloaded');
    await desktop.evaluate(({dialog},data)=>{dialog.showOpenDialog=async()=>({canceled:false,filePaths:[data.input]});},{input});
    await page.getByRole('button',{name:'选择文件',exact:true}).click();await expect(page.getByText('导入完成，请核对待确认项目。')).toBeVisible();
    await page.getByRole('button',{name:'核对 invoice.pdf'}).click();await expect(page.getByAltText('票据原件第 1 页')).toBeVisible();
    await page.getByRole('button',{name:'保存并确认'}).click();
    await page.getByLabel('报销月份',{exact:true}).fill('2026-09');await page.getByLabel('报销人',{exact:true}).fill('合成测试');
    await desktop.evaluate(({dialog},data)=>{dialog.showOpenDialog=async()=>({canceled:false,filePaths:[data.output]});},{output});
    await page.getByRole('button',{name:'生成打印件和统计表'}).click();await expect(page.getByText('文件已生成',{exact:true})).toBeVisible();
    await expect(page.getByText(/1 张发票 · ¥ 123.45/)).toBeVisible();
    await page.waitForTimeout(450);await desktop.close();desktop=await launch();page=await desktop.firstWindow();
    await expect(page.getByText(/已恢复上次整理/)).toBeVisible();await expect(page.getByText('00123456789012345678',{exact:true})).toBeVisible();
    const dirs=await fs.readdir(output);expect(dirs.length).toBe(1);const files=await fs.readdir(path.join(output,dirs[0]));expect(files.length).toBe(3);
    expect(await fs.stat(input)).toBeTruthy();
  }finally{await desktop.close();await fs.rm(root,{recursive:true,force:true});}
});
