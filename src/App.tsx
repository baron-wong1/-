import { useCallback, useEffect, useRef, useState } from 'react';
import { FilePlus2, FolderOpen, Upload, FileCheck2, ArrowRight, X, Check, LoaderCircle, Trash2 } from 'lucide-react';
import { api } from './api';
import { CATEGORIES, cents, formatMoney, type ExportResult, type InvoiceRecord, type Session } from './types';
import InvoiceTable from './components/InvoiceTable';
import ReviewDialog from './components/ReviewDialog';
import ConfirmClear from './components/ConfirmClear';

type Task = {id:string; kind:'import'|'export'; current:number; total:number; filename:string};
export default function App() {
  const [records, setRecords] = useState<InvoiceRecord[]>([]);
  const recordRef = useRef<InvoiceRecord[]>([]);
  const [person,setPerson] = useState(''), [month,setMonth] = useState('');
  const [categories,setCategories] = useState(CATEGORIES);
  const [task,setTask] = useState<Task|null>(null), taskRef = useRef<Task|null>(null);
  const [loaded,setLoaded] = useState(false), [restored,setRestored] = useState(false);
  const [message,setMessage] = useState(''), [error,setError] = useState('');
  const [filter,setFilter] = useState('all'), [dragging,setDragging] = useState(false);
  const [editing,setEditing] = useState<{record:InvoiceRecord; isNew:boolean}|null>(null);
  const [result,setResult] = useState<ExportResult|null>(null);
  const [runtime,setRuntime] = useState<{ocr:boolean;word:boolean}|null>(null);
  const [clearing,setClearing] = useState(false), [confirmClear,setConfirmClear] = useState(false);
  const reconcileVersion = useRef(0);
  const update = useCallback((next:InvoiceRecord[] | ((old:InvoiceRecord[])=>InvoiceRecord[])) => {
    const rows = typeof next === 'function' ? next(recordRef.current) : next;
    recordRef.current=rows; setRecords(rows); setResult(null);
  }, []);
  const setBusy = (value:Task|null) => {taskRef.current=value;setTask(value);};
  const reconcile = useCallback(async (rows:InvoiceRecord[]) => {
    const version=++reconcileVersion.current;
    const response = await api.run<{records:InvoiceRecord[]}>('reconcile',{records:rows},crypto.randomUUID());
    if (version===reconcileVersion.current) update(response.records);
  }, [update]);
  useEffect(() => {
    let active=true;
    api.load().then(async saved => {
      if (!active) return;
      if (saved) {
        update(saved.records);setPerson(saved.person || '');setMonth(saved.month || '');
        setCategories([...new Set([...CATEGORIES,...saved.categories])]);setRestored(saved.records.length>0);
        try {await reconcile(saved.records);} catch(exc){if(active)setError(String(exc));}
      }
      if(active)setLoaded(true);
    }).catch(exc=>{if(active){setError('无法恢复上次会话：'+String(exc));setLoaded(true);}});
    if (window.invoice) api.run<{ocr:boolean;word:boolean}>('doctor',{},crypto.randomUUID()).then(r=>{if(active)setRuntime(r);})
      .catch(exc=>{if(active)setError('启动检查失败：'+String(exc.message || exc));});
    return () => {active=false;};
  }, [update,reconcile]);
  useEffect(()=>api.onEvent(event=>{
    if(event.id!==taskRef.current?.id)return;
    if(event.event==='item'&&event.record)update(rows=>[...rows,event.record!]);
    if(event.event==='progress')setTask(old=>old?{...old,current:event.current||0,total:event.total||0,filename:event.filename||''}:old);
  }),[update]);
  useEffect(()=>{
    if(!loaded)return;
    const state:Session={version:1,records,person,month,categories};
    void api.save(state).catch(exc=>setError('会话保存失败：'+String(exc.message || exc)));
  },[records,person,month,categories,loaded]);

  async function importPaths(paths:string[]) {
    if(!paths.length || taskRef.current)return;
    const id=crypto.randomUUID(), initial=recordRef.current;
    setError('');setMessage('');setBusy({id,kind:'import',current:0,total:0,filename:'正在查找文件…'});
    try{
      const response=await api.run<{records:InvoiceRecord[];skipped:string[]}>('import',{paths,records:initial},id);
      update(response.records);
      setMessage(response.skipped.length ? `导入完成，跳过 ${response.skipped.length} 个内容相同的文件。` : '导入完成，请核对待确认项目。');
    }catch(exc){
      const text=exc instanceof Error?exc.message:String(exc);
      if(text.includes('任务已取消')){setMessage('已取消导入，已识别的项目已保留。');}
      else setError(text);
      try{await reconcile(recordRef.current);}catch{}
    }finally{setBusy(null);}
  }
  async function choose(kind:'files'|'folder') {try{await importPaths(await api.chooseInput(kind));}catch(exc){setError(String(exc instanceof Error?exc.message:exc));}}
  async function remove(id:string) {const next=recordRef.current.filter(r=>r.id!==id);update(next);try{await reconcile(next);}catch(exc){setError(String(exc));}}
  async function saveRecord(row:InvoiceRecord) {
    const next=editing?.isNew ? [...recordRef.current,row] : recordRef.current.map(r=>r.id===row.id?row:r);
    const response=await api.run<{records:InvoiceRecord[]}>('reconcile',{records:next},crypto.randomUUID());
    update(response.records);setEditing(null);
  }
  async function exportFiles() {
    if(taskRef.current)return;
    setError('');setMessage('');
    try {
      const destination=await api.chooseOutput();if(!destination)return;
      const id=crypto.randomUUID();setBusy({id,kind:'export',current:0,total:0,filename:'正在检查票据…'});
      const response=await api.run<ExportResult>('export',{records:recordRef.current,person,month,destination},id);
      setResult(response);setMessage('打印件和统计表已生成。');
    }catch(exc){const text=exc instanceof Error?exc.message:String(exc);if(text.includes('任务已取消'))setMessage('导出已取消。');else setError(text);}
    finally{setBusy(null);}
  }
  async function clear() {
    setClearing(true);setConfirmClear(false);
    try{await api.clearCache();update([]);setMessage('已清空列表与临时副本，原文件未删除。');setError('');setRestored(false);}
    catch(exc){setError(String(exc));}finally{setClearing(false);}
  }
  const pending=records.filter(r=>!r.status||['pending','conflict','error'].includes(r.status)).length;
  const duplicate=records.filter(r=>r.status==='duplicate').length;
  const invoices=records.filter(r=>r.status==='ready'&&r.kind==='invoice');
  const attachments=records.filter(r=>r.status==='ready'&&r.kind==='attachment').length;
  const total=invoices.reduce((sum,r)=>sum+cents(r.amount),0n);
  const visible=records.filter(r=>filter==='all'||(filter==='pending'&&(!r.status||['pending','conflict','error'].includes(r.status)))||(filter==='duplicate'&&r.status==='duplicate'));
  const busy=!!task||clearing||!loaded;
  const canExport=!busy&&invoices.length>0&&pending===0&&!!person.trim()&&/^20\d{2}-(0[1-9]|1[0-2])$/.test(month);

  return <div className="app-shell"><header className="app-header"><div className="brand"><span className="brand-mark"><FileCheck2 size={24}/></span><div><h1>发票整理助手</h1><p>整理票据，轻松准备报销</p></div></div><span className="local-indicator"><span/>本机离线处理</span></header>
    <main><section className="toolbar" aria-label="导入与报销信息"><div className="import-buttons"><button className="button" disabled={busy} onClick={()=>void choose('files')}><FilePlus2 size={17}/>选择文件</button><button className="button" disabled={busy} onClick={()=>void choose('folder')}><FolderOpen size={17}/>选择文件夹</button></div><div className="reimbursement-fields"><label>报销月份<input aria-label="报销月份" type="month" min="2000-01" max="2099-12" value={month} disabled={busy} onChange={e=>{setMonth(e.target.value);setResult(null);}}/></label><label>报销人<input aria-label="报销人" value={person} placeholder="请输入姓名" maxLength={80} disabled={busy} onChange={e=>{setPerson(e.target.value);setResult(null);}}/></label></div></section>
    <section className={`dropzone ${dragging?'drag-active':''} ${records.length?'compact':''}`} aria-label="票据拖放区" onDragOver={event=>{event.preventDefault();if(!busy)setDragging(true);}} onDragLeave={event=>{if(!event.currentTarget.contains(event.relatedTarget as Node))setDragging(false);}} onDrop={event=>{event.preventDefault();setDragging(false);if(busy)return;const paths=Array.from(event.dataTransfer.files).map(api.pathForFile).filter(Boolean);if(!paths.length){setError('请在桌面应用中拖入本机文件或文件夹。');return;}void importPaths(paths);}}>
      <div className="upload-symbol"><Upload size={records.length?23:30} strokeWidth={1.6}/></div><div><h2>{dragging?'松开即可导入':'把发票文件或文件夹拖到这里'}</h2><p>支持 PDF、Word（DOCX / DOC）、JPG、PNG</p>{!records.length&&<span>自动识别类别、开票日期、发票号码和含税金额</span>}</div>
    </section>
    {!window.invoice&&<div className="notice preview-notice">当前为界面预览。请启动桌面应用来导入本机文件并生成打印件。</div>}
    {runtime&&(!runtime.word||!runtime.ocr)&&<div className="notice error-notice" role="alert">{!runtime.word?'Word 转换组件缺失，请使用完整发行包。 ':''}{!runtime.ocr?'离线 OCR 组件缺失。':''}</div>}
    {restored&&<div className="restore-message">已恢复上次整理的列表。请保持原文件在原位置。<button className="icon-button" aria-label="关闭恢复提示" onClick={()=>setRestored(false)}><X size={14}/></button></div>}
    {error&&<div className="notice error-notice" role="alert">{error}<button className="icon-button" aria-label="关闭错误" onClick={()=>setError('')}><X size={16}/></button></div>}
    {message&&<div className="message" role="status"><Check size={16}/>{message}</div>}
    {task&&<div className="task-progress" role="status"><div className="task-label"><LoaderCircle size={17} className="spinning"/><span>{task.filename}</span><span>{task.total?`${task.current} / ${task.total}`:''}</span><button className="text-button" onClick={()=>void api.cancel(task.id).catch(exc=>setError(String(exc)))}>取消任务</button></div><progress max={task.total||1} value={task.current}/></div>}
    <section className="invoice-list" aria-label="票据明细"><div className="list-heading"><div><h2>票据明细 <span>{records.length}</span></h2><p>核对识别结果后，即可生成打印件和统计表</p></div>{records.length>0&&<button className="text-button muted" disabled={busy} onClick={()=>setConfirmClear(true)}><Trash2 size={14}/>清空列表</button>}</div>
      {records.length>0?<><div className="list-tabs" role="group" aria-label="筛选票据">{[['all','全部票据',records.length],['pending','待核对',pending],['duplicate','重复项',duplicate]].map(([key,label,count])=><button key={key} className={filter===key?'active':''} onClick={()=>setFilter(String(key))} aria-pressed={filter===key}>{label}<span>{count}</span></button>)}</div><InvoiceTable records={visible} busy={busy} onEdit={record=>setEditing({record,isNew:false})} onRemove={id=>void remove(id)}/></>:<div className="empty-list"><FileCheck2 size={34} strokeWidth={1.2}/><h3>还没有导入票据</h3><p>从上方选择文件，或直接拖入发票文件夹</p></div>}
    </section>
    {result&&<section className="export-result" role="status"><div className="result-title"><Check size={20}/><h2>文件已生成</h2></div><p>{result.count} 张发票 · ¥ {result.amount} · 合并打印件 {result.pages} 页</p><p className="output-path">{result.directory}</p><div className="result-files">{result.files.map(file=><button key={file} className="text-button" onClick={()=>void api.open(file).catch(exc=>setError(String(exc.message||exc)))}>{file.split(/[\\/]/).pop()}</button>)}</div><button className="button" onClick={()=>void api.open(result.directory).catch(exc=>setError(String(exc.message||exc)))}><FolderOpen size={16}/>打开输出文件夹</button></section>}
    </main><footer className="app-footer"><div className="summary"><div><span>有效发票</span><strong>{invoices.length}<small> 张</small></strong></div><div><span>合计金额</span><strong className="total-amount">¥ {formatMoney(total)}</strong></div><p>{pending?`${pending} 项待核对`:`附件 ${attachments} 项 · 重复排除 ${duplicate} 项`}<br/><span>每页最多两张 · 分类 PDF + 合并 PDF + Excel</span></p></div><div className="export-action">{!person.trim()||!month?<span>请填写报销月份和报销人</span>:pending>0?<button className="text-button" onClick={()=>setFilter('pending')}>先核对 {pending} 项提示</button>:null}<button className="button primary export-button" disabled={!canExport} onClick={()=>void exportFiles()}>{task?.kind==='export'?<LoaderCircle size={18} className="spinning"/>:<FileCheck2 size={18}/>}生成打印件和统计表<ArrowRight size={17}/></button></div></footer>
    {editing&&<ReviewDialog key={editing.record.id} {...editing} categories={categories} onClose={()=>setEditing(null)} onSave={saveRecord} onCategory={value=>setCategories(old=>[...new Set([...old,value])])} onSplit={row=>{void saveRecord({...row,reviewed:true}).then(()=>setEditing({record:{...row,id:crypto.randomUUID(),number:'',date:null,amount:null,reviewed:false,resolution:null,crop:null,warnings:['从同一文件拆分的新票据，请选择不重叠的区域或页范围并逐项核对']},isNew:true})).catch(exc=>setError(String(exc)));}}/>}
    {confirmClear&&<ConfirmClear onClose={()=>setConfirmClear(false)} onConfirm={()=>void clear()}/>}
  </div>;
}
