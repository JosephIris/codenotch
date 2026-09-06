import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { SingletonAction } from '@elgato/streamdeck';
import { codexReading, renderCodex } from './codex-display.mjs';

const directory = path.join(process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local'), 'Codenotch');
export class CodexUsageAction extends SingletonAction {
  manifestId = 'com.anthropic.claude-usage.codex';
  visible = new Map();
  timer = null;
  async onWillAppear(ev) {
    this.visible.set(ev.action.id, { action: ev.action, index: 0 });
    await this.update();
    this.timer ??= setInterval(() => { this.update().catch(() => {}); }, 15000);
  }
  onWillDisappear(ev) {
    this.visible.delete(ev.action.id);
    if (!this.visible.size) { clearInterval(this.timer); this.timer = null; }
  }
  async onKeyDown(ev) {
    const item = this.visible.get(ev.action.id);
    if (item) item.index++;
    await this.update();
  }
  async update() {
    let model;
    try {
      const settings = JSON.parse(fs.readFileSync(path.join(directory, 'settings.json'), 'utf8'));
      const archive = JSON.parse(fs.readFileSync(path.join(directory, 'usage.json'), 'utf8'));
      model = codexReading(archive, settings);
    } catch { model = { message: 'Open Codenotch', windows: [] }; }
    await Promise.allSettled([...this.visible.values()].map(item => item.action.setImage(renderCodex(model, item.index))));
  }
}
