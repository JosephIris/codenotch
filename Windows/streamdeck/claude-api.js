import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { createHash } from 'node:crypto';
import { createCollector } from './collector.mjs';

const cache = path.join(process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local'), 'ClaudeUsage', 'streamdeck.json');
const collect = createCollector({
  readCredentials() {
    const oauth = JSON.parse(fs.readFileSync(path.join(os.homedir(), '.claude', '.credentials.json'), 'utf8')).claudeAiOauth;
    const token = oauth?.accessToken;
    return { token, hash: token ? createHash('sha256').update(token).digest('hex') : null };
  },
  load() { try { return JSON.parse(fs.readFileSync(cache, 'utf8')); } catch { return null; } },
  save(state) {
    fs.mkdirSync(path.dirname(cache), { recursive: true });
    const temporary = `${cache}.${process.pid}.tmp`;
    fs.writeFileSync(temporary, JSON.stringify(state));
    fs.renameSync(temporary, cache);
  }
});

// Keep the shared reading available even when the deck shows another profile.
const timer = setInterval(() => { collect().catch(() => {}); }, 300000);
timer.unref();
collect().catch(() => {});

export async function fetchUsage() {
  const data = await collect();
  const bucket = key => {
    const value = data[key];
    if (typeof value?.utilization !== 'number' || !Number.isFinite(value.utilization)) throw new Error('Usage window unavailable');
    return { utilization: value.utilization, resetsAt: value.resets_at ?? null };
  };
  return { fiveHour: bucket('five_hour'), sevenDay: bucket('seven_day') };
}

export function formatTimeRemaining(resetsAt) {
  if (!resetsAt) return '';
  const minutes = Math.ceil((Date.parse(resetsAt) - Date.now()) / 60000);
  if (!Number.isFinite(minutes)) return '';
  if (minutes <= 0) return 'now';
  if (minutes >= 1440) return `${Math.floor(minutes / 1440)}d`;
  if (minutes >= 60) return `${Math.floor(minutes / 60)}h`;
  return `${minutes}m`;
}
