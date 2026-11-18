const fs = require('fs');

function extractMeans(filePath) {
  const text = fs.readFileSync(filePath, 'utf8');
  const lines = text.split('\n');
  const result = {};
  let currentClass = null;
  for (const line of lines) {
    const runMatch = line.match(/^\s*Running\s+(\S+)\.\.\./);
    if (runMatch) {
      currentClass = runMatch[1];
      continue;
    }
    const meanMatch = line.match(/^Mean = ([\d.]+)\s+(ns|us|ms|s)\b/);
    if (meanMatch && currentClass && !(currentClass in result)) {
      let val = parseFloat(meanMatch[1]);
      const unit = meanMatch[2];
      // 统一转 ns
      if (unit === 'us') val *= 1000;
      else if (unit === 'ms') val *= 1000000;
      else if (unit === 's') val *= 1000000000;
      result[currentClass] = val;
    }
  }
  return result;
}

const args = process.argv.slice(2);
if (args.length < 2) {
  console.error('Usage: node compare_runs.js <baseline_dir> <today_dir> [framework]');
  process.exit(1);
}

const baseDir = args[0];
const todayDir = args[1];
const framework = args[2] || 'net8.0';

const baseFile = `${baseDir}/bench_${framework}/bench_log.txt`;
const todayFile = `${todayDir}/bench_${framework}/bench_log.txt`;

const base = extractMeans(baseFile);
const today = extractMeans(todayFile);

const allClasses = [...new Set([...Object.keys(base), ...Object.keys(today)])];
allClasses.sort();

console.log(`Framework: ${framework}  Baseline classes: ${Object.keys(base).length}  Today classes: ${Object.keys(today).length}`);
console.log('Class'.padEnd(35) + 'Baseline(ns)'.padStart(14) + 'Today(ns)'.padStart(14) + 'Change%'.padStart(10) + '  Status');
console.log('-'.repeat(80));

let regressions = 0, improvements = 0, stable = 0, missing = 0;
const changes = [];

for (const cls of allClasses) {
  const b = base[cls];
  const t = today[cls];
  if (b == null || t == null) {
    const bv = b != null ? String(b) : '-';
    const tv = t != null ? String(t) : '-';
    console.log(cls.padEnd(35) + bv.padStart(14) + tv.padStart(14) + '        -'.padStart(10) + '  MISSING');
    missing++;
    continue;
  }
  const change = ((t - b) / b) * 100;
  changes.push(change);
  let status = '  ok';
  if (change > 5) { status = '  REGRESS'; regressions++; }
  else if (change < -5) { status = '  improve'; improvements++; }
  else { stable++; }
  console.log(cls.padEnd(35) + b.toFixed(1).padStart(14) + t.toFixed(1).padStart(14) + (change >= 0 ? '+' : '') + change.toFixed(1) + '%'.padStart(9) + status);
}

console.log('-'.repeat(80));
const avgChange = changes.reduce((a, b) => a + b, 0) / changes.length;
const median = [...changes].sort((a, b) => a - b)[Math.floor(changes.length / 2)];
console.log(`\nTotal: ${allClasses.length}  Regressions(>5%): ${regressions}  Improvements(<-5%): ${improvements}  Stable: ${stable}  Missing: ${missing}`);
console.log(`Average change: ${avgChange >= 0 ? '+' : ''}${avgChange.toFixed(1)}%  Median: ${median >= 0 ? '+' : ''}${median.toFixed(1)}%`);

// 统一变慢检查
const allSlower = changes.filter(c => c > 0).length;
const allFaster = changes.filter(c => c < 0).length;
console.log(`Slower count: ${allSlower}/${changes.length}  Faster count: ${allFaster}/${changes.length}`);

// top 变慢和变快
const sorted = [...allClasses].filter(c => base[c] != null && today[c] != null).map(c => ({cls: c, change: ((today[c]-base[c])/base[c])*100}));
sorted.sort((a, b) => b.change - a.change);
console.log('\nTop 5 slowest:');
sorted.slice(0, 5).forEach(s => console.log(`  ${s.cls.padEnd(33)} ${s.change >= 0 ? '+' : ''}${s.change.toFixed(1)}%`));
console.log('Top 5 fastest:');
sorted.slice(-5).reverse().forEach(s => console.log(`  ${s.cls.padEnd(33)} ${s.change >= 0 ? '+' : ''}${s.change.toFixed(1)}%`));
