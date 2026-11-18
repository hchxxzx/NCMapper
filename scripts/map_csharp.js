#!/usr/bin/env node
/**
 * C# Codebase Structure Mapper v3 — Compact + API detail
 * Improvements over v2:
 *   1. Sub-projects matching test/benchmark patterns are summarized even inside detail dirs
 *   2. Core library (src/NCMapper/) gets public API method signatures
 */

const fs = require('fs');
const path = require('path');

const DEFAULT_EXCLUDES = new Set([
  'bin', 'obj', '.git', 'node_modules', '__pycache__',
  '.vs', 'TestResults', 'artifacts', 'packages', '.idea',
  'Generated', 'generated'
]);

const SUMMARY_PATTERNS = /test|benchmark|sample|demo|example|perf/i;

const RE_NAMESPACE = /^\s*namespace\s+([\w.]+)/m;
const RE_TYPE_DECL = /^\s*(?:(?:public|internal|private|protected|sealed|abstract|static|partial|readonly|ref|file)\s+)*(class|interface|enum|record|struct)\s+(\w+)(?:\s*<[^>]*>)?(?:\s*:\s*([^{]+))?/gm;

// Public/protected method with signature capture
const RE_METHOD = /^\s*(?:public|protected)\s+(?:(?:static|virtual|override|abstract|async|sealed|new|partial|unsafe)\s+)*([\w<>\[\]?,\s]+?)\s+(\w+)\s*\(([^)]*)\)/gm;

function findCsFiles(root, excludes) {
  const results = [];
  function walk(dir) {
    let entries;
    try { entries = fs.readdirSync(dir, { withFileTypes: true }); } catch (e) { return; }
    for (const ent of entries) {
      if (excludes.has(ent.name) || ent.name.startsWith('.')) continue;
      const full = path.join(dir, ent.name);
      if (ent.isDirectory()) walk(full);
      else if (ent.name.endsWith('.cs')) results.push(full);
    }
  }
  walk(root);
  return results.sort();
}

function compactParams(params) {
  if (!params || !params.trim()) return '';
  return params.split(',').map(p => {
    p = p.trim().replace(/\s*=\s*.*$/, '').replace(/^(this|ref|out|in|params)\s+/, '');
    const tokens = p.split(/\s+/);
    if (tokens.length >= 2) return tokens[tokens.length - 2] + ' ' + tokens[tokens.length - 1];
    return tokens[tokens.length - 1] || '';
  }).filter(Boolean).join(', ');
}

function parseFile(filepath, root, withMethods) {
  let content;
  try { content = fs.readFileSync(filepath, 'utf-8'); } catch (e) { return null; }
  if (content.charCodeAt(0) === 0xFEFF) content = content.slice(1);

  const rel = path.relative(root, filepath).replace(/\\/g, '/');
  const nsMatch = content.match(RE_NAMESPACE);
  const ns = nsMatch ? nsMatch[1] : '';

  const types = [];
  let tm;
  RE_TYPE_DECL.lastIndex = 0;
  while ((tm = RE_TYPE_DECL.exec(content)) !== null) {
    const kind = tm[1];
    const name = tm[2];
    const inheritsRaw = tm[3];
    const inherits = inheritsRaw
      ? inheritsRaw.split(',').map(h => h.trim()).filter(Boolean).slice(0, 3)
      : [];
    types.push({ kind, name, inherits });
  }

  // Extract public methods if requested (for core API detail)
  let methods = [];
  if (withMethods) {
    let mm;
    RE_METHOD.lastIndex = 0;
    const skip = new Set(['if', 'for', 'foreach', 'while', 'switch', 'catch', 'using', 'return', 'new', 'throw', 'get', 'set']);
    while ((mm = RE_METHOD.exec(content)) !== null) {
      const mname = mm[2];
      if (skip.has(mname)) continue;
      const ret = mm[1].trim();
      const paramCompact = compactParams(mm[3]);
      methods.push(`${ret} ${mname}(${paramCompact})`);
    }
    // Deduplicate and cap
    methods = [...new Set(methods)].slice(0, 20);
  }

  return types.length > 0 ? { path: rel, ns, types, methods } : null;
}

function detectProjectKind(projName, namespaces) {
  const lower = projName.toLowerCase();
  if (lower.includes('test')) return 'tests';
  if (lower.includes('sample') || lower.includes('demo')) return 'samples';
  if (lower.includes('sourcegen') || lower.includes('generator')) return 'source-generator';
  if (lower.includes('benchmark') || lower.includes('perf')) return 'benchmarks';
  if (lower.includes('migrate')) return 'migration-tool';
  if (lower.includes('gui') || lower.includes('web') || lower.includes('blazor')) return 'ui-app';
  if (lower.includes('dapper')) return 'orm-extension (Dapper)';
  if (lower.includes('efcore')) return 'orm-extension (EF Core)';
  if (lower.includes('freesql')) return 'orm-extension (FreeSql)';
  if (lower.includes('doctranslate')) return 'doc-tooling';
  if (namespaces.some(n => n.includes('CodeAnalysis'))) return 'roslyn';
  return 'core-library';
}

/**
 * Group files by sub-project (second-level directory under top-level).
 * e.g. tools/NCMapper.Migrate/xxx.cs -> subProject = "NCMapper.Migrate"
 */
function groupBySubProject(files, topProj) {
  const groups = {};
  for (const f of files) {
    const parts = f.path.split('/');
    // parts[0] = topProj, parts[1] = sub-project name (if exists)
    const subProj = parts.length >= 3 ? parts[1] : '(root)';
    if (!groups[subProj]) groups[subProj] = [];
    groups[subProj].push(f);
  }
  return groups;
}

function formatOutput(projects, rootName) {
  const lines = [];
  lines.push('<!-- CODEBASE-MAP:AUTO-START -->');
  lines.push(`## Architecture Map (${rootName})`);
  lines.push('');

  let totalFiles = 0, totalTypes = 0;
  const topProjNames = Object.keys(projects).sort();
  const summaryLines = [];

  for (const topProj of topProjNames) {
    const files = projects[topProj];

    // If the top-level dir itself matches summary patterns, summarize entirely
    if (SUMMARY_PATTERNS.test(topProj)) {
      const typeCount = files.reduce((s, f) => s + f.types.length, 0);
      totalFiles += files.length;
      totalTypes += typeCount;
      summaryLines.push(`- ${topProj}/ — ${detectProjectKind(topProj, [])} [${files.length} files, ${typeCount} types]`);
      continue;
    }

    // Group by sub-project
    const subGroups = groupBySubProject(files, topProj);
    const subProjNames = Object.keys(subGroups).sort();

    // Separate sub-projects into detail vs summary
    const detailSubs = [];
    const summarySubs = [];
    for (const sp of subProjNames) {
      if (SUMMARY_PATTERNS.test(sp)) summarySubs.push(sp);
      else detailSubs.push(sp);
    }

    // Header for this top-level dir
    const allNs = [...new Set(files.map(f => f.ns).filter(Boolean))];
    const totalTypeCount = files.reduce((s, f) => s + f.types.length, 0);
    totalFiles += files.length;
    totalTypes += totalTypeCount;

    lines.push(`### ${topProj}/ [${files.length} files, ${totalTypeCount} types]`);
    lines.push('');

    // Detail sub-projects
    for (const sp of detailSubs) {
      const spFiles = subGroups[sp];
      const spNs = [...new Set(spFiles.map(f => f.ns).filter(Boolean))];
      const kind = detectProjectKind(sp, spNs);
      const isCoreLib = (topProj === 'src' && sp === 'NCMapper');

      lines.push(`  **${sp}** — ${kind}`);

      // Group by subdirectory within sub-project
      const byDir = {};
      for (const f of spFiles) {
        const parts = f.path.split('/');
        const subParts = parts.slice(2, -1); // remove topProj/subProj prefix and filename
        const dir = subParts.length > 0 ? subParts.join('/') : '.';
        if (!byDir[dir]) byDir[dir] = [];
        byDir[dir].push(f);
      }

      for (const dir of Object.keys(byDir).sort()) {
        const dirFiles = byDir[dir];
        if (dir !== '.') lines.push(`    ${dir}/`);
        for (const f of dirFiles) {
          const fname = path.basename(f.path);
          const typeStrs = f.types.map(t => {
            let s = t.name;
            if (t.inherits.length > 0) {
              const interesting = t.inherits.filter(h =>
                !h.startsWith('IEquatable') && !h.startsWith('IComparable') && h !== 'object'
              );
              if (interesting.length > 0) s += `:${interesting[0]}`;
            }
            return s;
          });
          let display = typeStrs.slice(0, 6).join(', ');
          if (typeStrs.length > 6) display += ` +${typeStrs.length - 6}`;
          const prefix = dir === '.' ? '    ' : '      ';
          lines.push(`${prefix}${fname}: ${display}`);

          // API methods for core library
          if (isCoreLib && f.methods.length > 0) {
            const mStr = f.methods.slice(0, 12).join('; ');
            const extra = f.methods.length > 12 ? ` +${f.methods.length - 12}` : '';
            lines.push(`${prefix}  API: ${mStr}${extra}`);
          }
        }
      }
      lines.push('');
    }

    // Summary sub-projects (tests inside tools/, etc.)
    for (const sp of summarySubs) {
      const spFiles = subGroups[sp];
      const typeCount = spFiles.reduce((s, f) => s + f.types.length, 0);
      const kind = detectProjectKind(sp, []);
      lines.push(`  **${sp}** — ${kind} [${spFiles.length} files, ${typeCount} types] (summary)`);
    }
    if (summarySubs.length > 0) lines.push('');
  }

  // Top-level summary projects
  if (summaryLines.length > 0) {
    lines.push('### Auxiliary (summary)');
    lines.push('');
    summaryLines.forEach(l => lines.push(l));
    lines.push('');
  }

  lines.push(`<!-- Total: ${totalFiles} files, ${totalTypes} types -->`);
  lines.push('<!-- CODEBASE-MAP:AUTO-END -->');

  return { text: lines.join('\n'), totalFiles, totalTypes };
}

// --- Main ---
const args = process.argv.slice(2);
let root = '', output = null, excludeExtra = '';

for (let i = 0; i < args.length; i++) {
  if (args[i] === '--root' && args[i + 1]) root = args[++i];
  else if (args[i] === '--output' && args[i + 1]) output = args[++i];
  else if (args[i] === '--exclude' && args[i + 1]) excludeExtra = args[++i];
}

if (!root) {
  console.error('Usage: node map_csharp.js --root <dir> [--output <file>] [--exclude dir1,dir2]'); // 文件名与脚本实际名一致
  process.exit(1);
}

root = path.resolve(root);
const excludes = new Set([...DEFAULT_EXCLUDES, ...excludeExtra.split(',').filter(Boolean)]);

const csFiles = findCsFiles(root, excludes);
if (csFiles.length === 0) { console.error('No .cs files found.'); process.exit(1); }

// Determine which files get method extraction (core library only)
const results = [];
for (const f of csFiles) {
  const rel = path.relative(root, f).replace(/\\/g, '/');
  const isCore = rel.startsWith('src/NCMapper/');
  const r = parseFile(f, root, isCore);
  if (r) results.push(r);
}

// Group by top-level directory
const projects = {};
for (const r of results) {
  const parts = r.path.split('/');
  const proj = parts.length >= 2 ? parts[0] : '(root)';
  if (!projects[proj]) projects[proj] = [];
  projects[proj].push(r);
}

const { text, totalFiles, totalTypes } = formatOutput(projects, path.basename(root));

if (output) {
  fs.writeFileSync(output, text, 'utf-8');
  console.log(`Map written to ${output}`);
} else {
  console.log(text);
}

const tokenEst = Math.round(text.length / 4);
console.error(`\n--- Summary ---`);
console.error(`Files: ${totalFiles}, Types: ${totalTypes}`);
console.error(`Output size: ${text.length} chars, ~${tokenEst} tokens`);
