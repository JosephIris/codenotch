import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createCollector } from './collector.mjs';
const data = { five_hour: { utilization: 21 }, seven_day: { utilization: 7 } };
function fixture(request, saved) {
  let time = 1800000000000, state = saved, hash = 'account-a', calls = 0;
  const options = { readCredentials: () => ({ token: 'test-only', hash }), load: () => state, save: s => { state = structuredClone(s); }, now: () => time,
    request: async (...args) => { calls++; return request(...args); } };
  return { fetch: createCollector(options), restart: () => createCollector(options), advance: ms => { time += ms; }, account: () => { hash = 'account-b'; }, state: () => state, calls: () => calls };
}
test('both buttons and repeated presses share one request for five minutes', async () => {
  const f = fixture(async () => Response.json(data));
  await Promise.all([f.fetch(), f.fetch(), f.fetch()]); await f.fetch();
  assert.equal(f.calls(), 1); f.advance(300001); await f.fetch(); assert.equal(f.calls(), 2);
});
test('429 Retry-After survives restart and account changes', async () => {
  const f = fixture(async () => new Response('', { status: 429, headers: { 'Retry-After': '1800' } }));
  await assert.rejects(f.fetch()); await assert.rejects(f.restart()()); f.account(); await assert.rejects(f.fetch());
  f.advance(1799000); await assert.rejects(f.fetch()); assert.equal(f.calls(), 1);
  f.advance(2000); await assert.rejects(f.fetch()); assert.equal(f.calls(), 2);
});
test('failed refresh keeps original timestamp; auth rejection clears data', async () => {
  let status = 200;
  const f = fixture(async () => status === 200 ? Response.json(data) : new Response('', { status }));
  await f.fetch(); const stamp = f.state().fetchedAt; f.advance(300001); status = 429;
  await assert.rejects(f.fetch()); assert.equal(f.state().fetchedAt, stamp);
  f.advance(600001); status = 401; await assert.rejects(f.fetch()); assert.equal(f.state().data, undefined);
});
test('account switch does not serve previous usage', async () => {
  const f = fixture(async () => Response.json(data)); await f.fetch(); f.account(); await f.fetch(); assert.equal(f.calls(), 2);
});
test('network errors and malformed responses back off without inventing zeroes', async () => {
  const f = fixture(async () => Response.json({})); await assert.rejects(f.fetch()); await assert.rejects(f.fetch());
  assert.equal(f.calls(), 1); assert.equal(f.state().data, undefined);
});
