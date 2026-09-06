// Preserve the upstream traced outlines verbatim; no replacement logo approximations.
import fs from 'node:fs';
const source = fs.readFileSync(new URL('../Sources/Providers/GlyphOutline.swift', import.meta.url), 'utf8');
const output = {};
for (const match of source.matchAll(/static let (\w+): \[\[CGPoint\]\] = \[([\s\S]*?)(?=\n    static let|\n\})/g)) {
  const loops = [...match[2].matchAll(/\[\s*(CGPoint\([\s\S]*?)\]/g)];
  output[match[1]] = 'F0 ' + loops.map(loop => [...loop[1].matchAll(/CGPoint\(x: ([\d.]+), y: ([\d.]+)\)/g)].map((p, i) => `${i ? 'L' : 'M'}${p[1]},${p[2]}`).join(' ') + ' Z').join(' ');
}
if (!output.claude || !output.openai || !output.cursor || !output.glm) throw new Error('Upstream glyph format changed');
fs.mkdirSync(new URL('Codenotch/Assets/', import.meta.url), { recursive: true });
fs.writeFileSync(new URL('Codenotch/Assets/glyphs.json', import.meta.url), JSON.stringify(output, null, 2) + '\n');
