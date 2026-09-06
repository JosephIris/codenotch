// One collector shared by both Stream Deck actions. No credentials are persisted.
export function createCollector({ readCredentials, load, save, request = fetch, now = Date.now }) {
  let state = load() ?? { version: 1 };
  let pending;
  async function collect() {
    const saved = load();
    if (saved?.version === 1) state = saved;
    const { token, hash } = readCredentials();
    if (!token) throw new Error('No Claude login');
    if (state.credentialHash !== hash) {
      // Discard another account's numbers, but preserve the server's cooldown.
      state = { version: 1, credentialHash: hash, retryAt: state.retryAt };
    }
    if (now() < (state.retryAt ?? 0)) throw new Error('Claude cooldown active');
    if (state.data && now() - Date.parse(state.fetchedAt) < 300000 && !state.error) return state.data;
    try {
      const response = await request('https://api.anthropic.com/api/oauth/usage', {
        headers: { Authorization: `Bearer ${token}`, 'anthropic-beta': 'oauth-2025-04-20', Accept: 'application/json' },
        signal: AbortSignal.timeout(15000), redirect: 'error'
      });
      if (!response.ok) {
        const header = response.headers.get('retry-after');
        const retryMs = header && /^\d+(\.\d+)?$/.test(header) ? Number(header) * 1000 : Date.parse(header) - now();
        state.failures = Math.min((state.failures ?? 0) + 1, 8);
        state.retryAt = now() + Math.max(300000, Number.isFinite(retryMs) ? retryMs : 0, Math.min(3600000, 300000 * 2 ** (state.failures - 1)));
        state.error = response.status === 401 || response.status === 403 ? 'auth' : `HTTP ${response.status}`;
        if (state.error === 'auth') { delete state.data; delete state.fetchedAt; }
        throw new Error(state.error);
      }
      const data = await response.json();
      if (![data.five_hour, data.seven_day].some(w => typeof w?.utilization === 'number' && Number.isFinite(w.utilization) && w.utilization >= 0)) throw new Error('Invalid usage response');
      state = { version: 1, credentialHash: hash, fetchedAt: new Date(now()).toISOString(), data, retryAt: 0, failures: 0 };
      save(state);
      return data;
    } catch (error) {
      state.error ??= 'Usage unavailable';
      state.retryAt = Math.max(state.retryAt ?? 0, now() + 300000);
      save(state);
      throw error;
    }
  }
  return () => {
    if (!pending) pending = collect().finally(() => { pending = null; });
    return pending;
  };
}
