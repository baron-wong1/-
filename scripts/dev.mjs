import { spawn } from 'node:child_process';
import net from 'node:net';
const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm';
const server = spawn(npm, ['exec', 'vite'], { stdio: 'inherit', shell: process.platform === 'win32' });
let electron;
const check = setInterval(() => {
  const socket = net.connect(5173, '127.0.0.1');
  socket.once('connect', () => {
    socket.destroy(); clearInterval(check);
    electron = spawn(npm, ['exec', 'electron', '.'], { stdio: 'inherit', shell: process.platform === 'win32', env: { ...process.env, INVOICE_DEV: '1' } });
    electron.on('exit', code => { server.kill(); process.exit(code ?? 0); });
  });
  socket.on('error', () => socket.destroy());
}, 300);
server.on('exit', () => { clearInterval(check); electron?.kill(); });
process.on('SIGINT', () => { electron?.kill(); server.kill(); });
