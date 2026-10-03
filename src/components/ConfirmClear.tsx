import { useEffect, useRef } from 'react';
export default function ConfirmClear({onClose,onConfirm}:{onClose():void;onConfirm():void}) {
  const dialog=useRef<HTMLDialogElement>(null);
  useEffect(()=>{dialog.current?.showModal();},[]);
  return <dialog ref={dialog} className="confirm-dialog" aria-labelledby="clear-title" onCancel={event=>{event.preventDefault();onClose();}}><h2 id="clear-title">清空本次整理？</h2><p>将移除列表和临时副本，原发票文件不会删除。报销人和月份会保留。</p><div><button className="button" autoFocus onClick={onClose}>取消</button><button className="button primary" onClick={onConfirm}>清空列表</button></div></dialog>;
}
