import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { buildProbeSource } from '../inject.mjs';

function fakeContext(traceCalls = false) {
  const events = [];
  const callbacks = {};
  const calls = [];
  const unsubscribed = [];
  const apps = {
    RegisterForGameActionStart(cb) { callbacks.start = cb; return { unregister() { unsubscribed.push('start'); } }; },
    RegisterForGameActionTaskChange(cb) { callbacks.task = cb; return { unregister() { unsubscribed.push('task'); } }; },
    RegisterForGameActionEnd(cb) { callbacks.end = cb; return { unregister() { unsubscribed.push('end'); } }; },
    GetAppOverview(id) { calls.push(id); return { secret: 'never logged' }; }
  };
  const sandbox = {
    SteamClient: { Apps: apps, Downloads: {}, Friends: {} },
    __controllerPlaygroundObserverEmit(value) { events.push(JSON.parse(value)); }
  };
  vm.createContext(sandbox);
  const result = vm.runInContext(buildProbeSource({ traceCalls }), sandbox);
  return { result, sandbox, callbacks, calls, events, unsubscribed, apps };
}

test('registers safe callbacks and redacts private callback values', () => {
  const x = fakeContext();
  assert.equal(x.result.ok, true);
  x.callbacks.start(2, '3164500', 'LaunchApp', { secret: 'sensitive' });
  x.callbacks.task(3, '3164500', 'LaunchApp', 'CreatingProcess', 'private text');
  x.callbacks.end(2);
  const text = JSON.stringify(x.events);
  assert.ok(text.includes('3164500'));
  assert.ok(text.includes('CreatingProcess'));
  assert.ok(!text.includes('sensitive'));
  assert.ok(!text.includes('private text'));
  assert.ok(!text.includes('secret'));
  assert.equal(x.events.filter(e => e.kind === 'callback').length, 3);
  x.sandbox.__controllerPlaygroundSteamObserver.stop();
  assert.equal(x.unsubscribed.length, 3);
});

test('method tracing is off by default and works only when opted in', () => {
  const x = fakeContext();
  x.apps.GetAppOverview('123');
  assert.equal(x.events.filter(e=>e.kind === 'call').length, 0);
  const y = fakeContext(true);
  y.apps.GetAppOverview('98765');
  assert.equal(y.events.filter(e=>e.kind === 'call').length, 1);
  assert.deepEqual(y.calls, ['98765']);
  assert.equal(y.events.find(e=>e.kind === 'call').data.method, 'Apps.GetAppOverview');
  assert.ok(!JSON.stringify(y.events).includes('98765'));
  y.sandbox.__controllerPlaygroundSteamObserver.stop();
  y.apps.GetAppOverview('100');
  assert.equal(y.events.filter(e=>e.kind === 'call').length, 1);
});

test('does not log unsafe IDs or arbitrary task strings', () => {
  const x = fakeContext();
  x.callbacks.task(3, 'user@example.com', 'anything', 'private message body', 'private');
  const event = x.events.find(e=>e.kind === 'callback');
  assert.equal(event.data.appId, null);
  assert.equal(event.data.task, null);
});
