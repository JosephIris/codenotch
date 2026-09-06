export function codexReading(archive, settings, now = Date.now()) {
  if (settings?.Disabled?.includes('codex')) return { message: 'Disconnected', windows: [] };
  const reading = archive?.Readings?.codex;
  if (!reading || reading.Source === 'Demo') return { message: 'Open Codenotch', windows: [] };
  const stamp = Date.parse(reading.RecordedAt);
  if (!Number.isFinite(stamp) || stamp > now + 60000) return { message: 'No valid reading', windows: [] };
  const windows = (reading.Windows ?? []).filter(w => typeof w.Percent === 'number' && Number.isFinite(w.Percent) && w.Percent >= 0);
  const stale = now - stamp > 360000 || !reading.Source?.includes('live') || reading.Status?.startsWith('Stale');
  return { windows, stale, message: windows.length ? null : 'No usage reported' };
}
const escape = value => String(value).replace(/[<>&"']/g, ch => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;', "'": '&apos;' }[ch]));
export function renderCodex(model, index = 0, now = Date.now()) {
  const window = model.windows[index % Math.max(1, model.windows.length)];
  const percent = window?.Percent;
  const color = model.stale ? '#737b83' : percent >= 90 ? '#fb923c' : '#84dcc6';
  const radius = 43, circumference = 2 * Math.PI * radius;
  const arc = Math.min(100, percent ?? 0) / 100 * circumference * .75;
  const minutes = Math.ceil((Date.parse(window?.ResetsAt) - now) / 60000);
  const reset = !Number.isFinite(minutes) ? '' : minutes <= 0 ? 'reset due' : minutes >= 1440 ? `${Math.floor(minutes / 1440)}d ${Math.floor(minutes % 1440 / 60)}h` : minutes >= 60 ? `${Math.floor(minutes / 60)}h ${minutes % 60}m` : `${minutes}m`;
  const label = window?.Label?.replace(' limit', '') || model.message;
  const footer = model.stale ? 'STALE · open notch' : reset ? `Resets ${reset}` : 'ChatGPT allowance';
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="144" height="144" viewBox="0 0 144 144">
  <defs><linearGradient id="bg" x2="0" y2="1"><stop stop-color="#101a1b"/><stop offset="1" stop-color="#090e12"/></linearGradient></defs>
  <rect width="144" height="144" rx="12" fill="url(#bg)"/>
  <text x="72" y="15" text-anchor="middle" font-family="Segoe UI,sans-serif" font-size="11" font-weight="700" letter-spacing="2" fill="${color}">CODEX</text>
  <g fill="none" stroke-width="10" stroke-linecap="round" transform="rotate(135 72 69)">
  <circle cx="72" cy="69" r="43" stroke="#1c3534" stroke-dasharray="${circumference * .75} ${circumference}"/>
  <circle cx="72" cy="69" r="43" stroke="${color}" stroke-dasharray="${arc} ${circumference}"/></g>
  <text x="72" y="75" text-anchor="middle" fill="white" font-family="Segoe UI,sans-serif" font-size="27" font-weight="700">${percent == null ? '—' : Math.round(percent) + '%'}</text>
  <text x="72" y="111" text-anchor="middle" fill="#edf4f2" font-family="Segoe UI,sans-serif" font-size="13" font-weight="600">${escape(label)}${window ? ' used' : ''}</text>
  <text x="72" y="132" text-anchor="middle" fill="#9aada9" font-family="Segoe UI,sans-serif" font-size="10">${escape(footer)}</text></svg>`;
  return `data:image/svg+xml;base64,${Buffer.from(svg).toString('base64')}`;
}
