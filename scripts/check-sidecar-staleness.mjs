// sidecar-audit.mjs -- read-only staleness audit for demo-comment sidecars.
// Scanner semantics are a faithful port of inject-demo-comments.ps1 (string-literal
// skipping, line/block comment spans, leading-slash trim, .Trim() compare).
// Usage: node scripts/../temp/sidecar-audit.mjs [--json]
// Exit: 0 = all fresh, 1 = stale entries found, 2 = internal error.
import fs from 'node:fs';
import path from 'node:path';

const repo = process.cwd();

function getCommentSpans(text) {
  const spans = [];
  const n = text.length;
  let i = 0;
  while (i < n) {
    const c = text[i];
    if (c === '"' || c === "'") {
      const q = c; i++;
      while (i < n) {
        if (text[i] === '\\') { i += 2; continue; }
        if (text[i] === q) { i++; break; }
        i++;
      }
    } else if (c === '/' && i + 1 < n && text[i + 1] === '/') {
      const start = i; i += 2;
      while (i < n && text[i] !== '\n') i++;
      spans.push({ start, end: i, type: 'line' });
    } else if (c === '/' && i + 1 < n && text[i + 1] === '*') {
      const start = i; i += 2;
      while (i < n && !(text[i] === '*' && i + 1 < n && text[i + 1] === '/')) i++;
      i += 2;
      spans.push({ start, end: i, type: 'block' });
    } else {
      i++;
    }
  }
  return spans;
}

function getCommentText(text, span) {
  if (span.type === 'line') {
    let slash = 2;
    while (slash < span.end - span.start && text[span.start + slash] === '/') slash++;
    return text.slice(span.start + slash, span.end);
  }
  return text.slice(span.start + 2, span.end - 2);
}

const i18nDir = path.join(repo, 'docs', 'i18n');
const langs = fs.readdirSync(i18nDir, { withFileTypes: true })
  .filter(d => d.isDirectory()).map(d => d.name).sort();
const srcCache = new Map();
const stale = [];
let filesChecked = 0;

for (const lang of langs) {
  const langDir = path.join(i18nDir, lang);
  const jsons = [];
  (function walk(dir) {
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      const p = path.join(dir, e.name);
      if (e.isDirectory()) walk(p);
      else if (e.name.endsWith('.cs.json')) jsons.push(p);
    }
  })(langDir);
  for (const jf of jsons) {
    const relJson = path.relative(langDir, jf).split(path.sep).join('/');
    const relSrc = relJson.replace(/\.json$/, '');
    const srcPath = path.join(repo, relSrc);
    if (!fs.existsSync(srcPath)) { stale.push({ lang, file: relSrc, i: -1, problem: 'MISSING-SOURCE', sidecarZh: '', current: '' }); continue; }
    filesChecked++;
    if (!srcCache.has(relSrc)) {
      const src = fs.readFileSync(srcPath, 'utf8');
      srcCache.set(relSrc, { src, spans: getCommentSpans(src) });
    }
    const { src, spans } = srcCache.get(relSrc);
    const side = JSON.parse(fs.readFileSync(jf, 'utf8'));
    for (const c of side.comments) {
      if (c.i >= spans.length) {
        stale.push({ lang, file: relSrc, i: c.i, problem: `INDEX-OUT-OF-RANGE(total=${spans.length})`, sidecarZh: c.zh, current: '' });
        continue;
      }
      const cur = getCommentText(src, spans[c.i]).trim();
      if (cur !== c.zh.trim()) {
        stale.push({ lang, file: relSrc, i: c.i, problem: 'ZH-MISMATCH', sidecarZh: c.zh, current: cur });
      }
    }
  }
}

if (process.argv.includes('--json')) {
  console.log(JSON.stringify(stale, null, 1));
} else {
  for (const s of stale) {
    console.log(`[${s.lang}] STALE ${s.file} #${s.i} [${s.problem}]`);
    if (s.sidecarZh) console.log(`    sidecar-zh: ${s.sidecarZh.replace(/\r/g, ' ').replace(/\n/g, ' | ')}`);
    if (s.current) console.log(`    current   : ${s.current.replace(/\r/g, ' ').replace(/\n/g, ' | ')}`);
  }
  console.log('====');
  console.log(`files checked: ${filesChecked}; stale entries: ${stale.length}`);
}
process.exit(stale.length ? 1 : 0);
