const { contextBridge, ipcRenderer, webUtils } = require('electron');
contextBridge.exposeInMainWorld('invoice', {
  chooseInput: kind => ipcRenderer.invoke('choose-input', kind),
  chooseOutput: () => ipcRenderer.invoke('choose-output'),
  run: (op, args, id) => ipcRenderer.invoke('run-core', op, args, id),
  cancel: id => ipcRenderer.invoke('cancel-core', id),
  load: () => ipcRenderer.invoke('load-session'),
  save: state => ipcRenderer.invoke('save-session', state),
  clearCache: () => ipcRenderer.invoke('clear-cache'),
  open: target => ipcRenderer.invoke('open-output', target),
  pathForFile: file => webUtils.getPathForFile(file),
  onEvent: callback => {
    const handler = (_event, data) => callback(data);
    ipcRenderer.on('core-event', handler);
    return () => ipcRenderer.removeListener('core-event', handler);
  },
});
