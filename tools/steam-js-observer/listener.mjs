#!/usr/bin/env node
// Local research observer for Steam's undocumented Chromium JavaScript bridge.
import { mkdir, open } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { buildProbeSource } from './inject.mjs';

function options(argv) {
  const opts = { port: 8080, traceCalls: false, out: null };
  for (let i = 0; i < argv.length; i++) {
    switch (argv[i]) {
      case '--port': opts.port = Number(argv[++i]); break;
      case '--out': opts.out = argv[++i]; break;
      case '--trace-calls': opts.traceCalls = true; break;
      case '--help': console.log('Usage: node listener.mjs [--port 8080] [--trace-calls] [--out captures/session.jsonl]'); process.exit(0);
      default: throw new Error('Unknown argument: ' + argv[i]);
    }
  }
  if (!Number.isInteger(opts.port) || opts.port < 1 || opts.port > 65535) throw new Error('Invalid port');
  if (!opts.out) opts.out = 'captures/steam-' + new Date().toISOString().replace(/[:.]/g, '-') + '.jsonl';
  return opts;
}

const isLoopback = (rawUrl) => {
  try {
    const u = new URL(rawUrl);
    return ['localhost', '127.0.0.1', '[::1]'].includes(u.hostname);
  } catch { return false; }
};

async function run() {
  const opts = options(process.argv.slice(2));
  const response = await fetch(`http://127.0.0.1:${opts.port}/json/list`, { signal: AbortSignal.timeout(5000) });
  if (!response.ok) throw new Error('Steam DevTools discovery returned HTTP ' + response.status);
  const targets = await response.json();
  if (!Array.isArray(targets)) throw new Error('Steam DevTools target response is not a list');
  const target = targets.find(t => t.title === 'SharedJSContext' && t.webSocketDebuggerUrl)
    ?? targets.find(t => t.title?.includes('SharedJSContext') && t.webSocketDebuggerUrl);
  if (!target) throw new Error('SharedJSContext not found; inspect /json/list and restart Steam with -cef-enable-debugging');
  const socketUrl = new URL(target.webSocketDebuggerUrl);
  if (!['ws:', 'wss:'].includes(socketUrl.protocol) || !isLoopback(socketUrl.href) || Number(socketUrl.port || 80) !== opts.port) {
    throw new Error('Refusing to connect to non-loopback or unexpected DevTools endpoint');
  }

  const output = resolve(opts.out);
  await mkdir(dirname(output), { recursive: true });
  const file = await open(output, 'wx', 0o600);
  let closed = false;
  let writes = Promise.resolve();
  let nextId = 0;
  const pending = new Map();
  const write = (record) => {
    if (closed) return;
    // Only own sanitized metadata crosses the boundary. Never write CDP packets.
    writes = writes.then(() => file.writeFile(JSON.stringify({ at: new Date().toISOString(), target: 'SharedJSContext', ...record }) + '\n')).catch(e => {
      console.error('Log write failed:', e.message);
      ws.close();
    });
  };
  const ws = new WebSocket(socketUrl.href);
  const send = (method, params = {}) => new Promise((ok, reject) => {
    if (ws.readyState !== WebSocket.OPEN) { reject(new Error('WebSocket closed')); return; }
    const id = ++nextId;
    const timeout = setTimeout(() => { pending.delete(id); reject(new Error(method + ' timed out')); }, 10000);
    pending.set(id, { ok, reject, timeout });
    ws.send(JSON.stringify({ id, method, params }));
  });
  ws.addEventListener('message', (event) => {
    let packet;
    try { packet = JSON.parse(event.data); } catch { return; }
    if (packet.id != null) {
      const p = pending.get(packet.id);
      if (!p) return;
      pending.delete(packet.id); clearTimeout(p.timeout);
      if (packet.error) p.reject(new Error('CDP ' + packet.error.code)); else p.ok(packet.result);
      return;
    }
    if (packet.method === 'Runtime.bindingCalled' && packet.params?.name === '__controllerPlaygroundObserverEmit') {
      try {
        const data = JSON.parse(packet.params.payload);
        if (!['surface','unsupported','subscribed','callback','call','ready'].includes(data.kind)) return;
        // Field-level allowlist on the receiver; do not trust injected runtime JSON blindly.
        const d = data.data ?? {};
        if (data.kind === 'surface') write({ kind: data.kind, namespace: String(d.namespace).slice(0,100), methods: Array.isArray(d.methods) ? d.methods.map(x=>String(x).slice(0,100)).slice(0,256):[] });
        else if (data.kind === 'callback') write({ kind:data.kind, method:String(d.method).slice(0,120), argTypes: Array.isArray(d.argTypes)?d.argTypes.slice(0,16):[], appId: /^\d{1,12}$/.test(d.appId)?d.appId:null, task: /^[a-zA-Z][a-zA-Z0-9_]{0,63}$/.test(d.task)?d.task:null });
        else if (data.kind === 'call') write({ kind:data.kind, method:String(d.method).slice(0,120), argTypes:Array.isArray(d.argTypes)?d.argTypes.slice(0,16):[] });
        else write({ kind:data.kind, method: typeof d.method === 'string' ? d.method.slice(0,120) : undefined, traceCalls: data.kind === 'ready' ? Boolean(d.traceCalls) : undefined });
      } catch { /* malformed binding payload */ }
    }
  });

  ws.addEventListener('error', () => console.error('Steam DevTools socket error'));
  ws.addEventListener('close', async () => {
    closed = true;
    for (const p of pending.values()) { clearTimeout(p.timeout); p.reject(new Error('Socket closed')); }
    pending.clear();
    await writes;
    await file.close();
    console.log('Capture closed:', output);
  });

  const close = async () => {
    if (ws.readyState === WebSocket.OPEN) {
      try { await send('Runtime.evaluate', { expression: 'globalThis.__controllerPlaygroundSteamObserver?.stop()' }); } catch {}
    }
    ws.close();
  };
  process.once('SIGINT', close);
  process.once('SIGTERM', close);

  await new Promise((ok, reject) => {
    ws.addEventListener('open', ok, { once: true });
    ws.addEventListener('close', () => reject(new Error('DevTools disconnected before initialization')), { once: true });
    ws.addEventListener('error', () => reject(new Error('Cannot connect to Steam DevTools')), { once: true });
  });
  await send('Runtime.enable');
  await send('Runtime.addBinding', { name: '__controllerPlaygroundObserverEmit' });
  const injected = await send('Runtime.evaluate', { expression: buildProbeSource(opts), returnByValue: true });
  const result = injected.result?.value;
  if (injected.exceptionDetails || !result?.ok) {
    await close();
    throw new Error('Steam observer setup failed: ' + (result?.reason ?? 'Runtime.evaluate exception'));
  }
  console.log('Capturing sanitized Steam JS metadata in:', output);
  console.log('Target: SharedJSContext; trace calls:', opts.traceCalls ? 'ON (experimental)' : 'OFF');
  console.log('Ctrl+C stops and unregisters listeners. Do not leave Steam debugging enabled.');
}

run().catch(e => { console.error(e.message); process.exitCode = 1; });
