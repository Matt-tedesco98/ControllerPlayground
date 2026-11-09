// Evaluated only inside the user-selected Steam Chromium debugging target.
// No SteamClient action methods are called; subscriptions are observational.
export function buildProbeSource({ traceCalls = false } = {}) {
  return `(() => {
    'use strict';
    const binding = globalThis.__controllerPlaygroundObserverEmit;
    const client = globalThis.SteamClient;
    if (typeof binding !== 'function') return { ok: false, reason: 'CDP binding missing' };
    if (!client || typeof client !== 'object') return { ok: false, reason: 'SteamClient not found in this context' };
    try { globalThis.__controllerPlaygroundSteamObserver?.stop(); } catch {}

    const cleanup = [];
    const state = { stopped: false, stop() {
      if (state.stopped) return;
      state.stopped = true;
      for (const f of cleanup.reverse()) { try { f(); } catch {} }
      if (globalThis.__controllerPlaygroundSteamObserver === state) {
        delete globalThis.__controllerPlaygroundSteamObserver;
      }
    }};
    globalThis.__controllerPlaygroundSteamObserver = state;
    const emit = (kind, data) => {
      if (state.stopped) return;
      try { binding(JSON.stringify({ kind, data })); } catch {}
    };
    const type = value => value === null ? 'null' : Array.isArray(value) ? 'array' : typeof value;
    const safeAppId = value => {
      const s = String(value);
      return /^\\d{1,12}$/.test(s) ? s : null;
    };
    const safeTask = value => typeof value === 'string' && /^[a-zA-Z][a-zA-Z0-9_]{0,63}$/.test(value) ? value : null;
    const safeArgTypes = args => Array.from(args).slice(0, 16).map(type);

    // Record method names only, never method results, argument values, user IDs, or tokens.
    for (const nsName of Object.keys(client).slice(0, 128)) {
      try {
        const descriptor = Object.getOwnPropertyDescriptor(client, nsName);
        if (!descriptor || !('value' in descriptor) || !descriptor.value || typeof descriptor.value !== 'object') continue;
        const names = Object.getOwnPropertyNames(descriptor.value).slice(0, 256)
          .filter(name => typeof Object.getOwnPropertyDescriptor(descriptor.value, name)?.value === 'function');
        emit('surface', { namespace: nsName, methods: names });
      } catch { /* inaccessible namespace */ }
    }

    const subscriptions = [
      'RegisterForGameActionStart',
      'RegisterForGameActionTaskChange',
      'RegisterForGameActionEnd',
    ];
    for (const name of subscriptions) {
      try {
        const fn = client.Apps?.[name];
        if (typeof fn !== 'function') { emit('unsupported', { method: 'Apps.' + name }); continue; }
        const subscription = fn.call(client.Apps, (...args) => {
          const data = { method: 'Apps.' + name, argTypes: safeArgTypes(args) };
          // Verified callback format: Start/TaskChange second argument = appid.
          if (name !== 'RegisterForGameActionEnd') data.appId = safeAppId(args[1]);
          if (name === 'RegisterForGameActionTaskChange') data.task = safeTask(args[3]);
          emit('callback', data);
        });
        if (subscription && typeof subscription.unregister === 'function') {
          cleanup.push(() => subscription.unregister());
        }
        emit('subscribed', { method: 'Apps.' + name });
      } catch (e) { emit('unsupported', { method: 'Apps.' + name, reason: 'registration failed' }); }
    }

    // Experimental. Wrapping calls can alter function identity. Opt-in only.
    // This observes only calls through this object's properties in this JS context,
    // not all native Steam IPC nor calls via previously cached function references.
    if (${Boolean(traceCalls)}) {
      for (const nsName of ['Apps', 'Downloads', 'GameSessions']) {
        const ns = client[nsName];
        if (!ns || typeof ns !== 'object') continue;
        for (const name of Object.getOwnPropertyNames(ns)) {
          if (!/^[A-Za-z][A-Za-z0-9_]{0,99}$/.test(name)) continue;
          const desc = Object.getOwnPropertyDescriptor(ns, name);
          if (!desc || typeof desc.value !== 'function' || (!desc.writable && !desc.configurable)) continue;
          const original = desc.value;
          // Skip observer-created methods; no need to record our own subscriptions.
          if (subscriptions.includes(name)) continue;
          const wrapped = function(...args) {
            emit('call', { method: nsName + '.' + name, argTypes: safeArgTypes(args) });
            return Reflect.apply(original, this, args);
          };
          try {
            Object.defineProperty(ns, name, { ...desc, value: wrapped });
            cleanup.push(() => {
              if (Object.getOwnPropertyDescriptor(ns, name)?.value === wrapped)
                Object.defineProperty(ns, name, desc);
            });
          } catch { /* non-writable Steam bridge member */ }
        }
      }
    }
    emit('ready', { traceCalls: ${Boolean(traceCalls)} });
    return { ok: true, traceCalls: ${Boolean(traceCalls)} };
  })()`;
}
