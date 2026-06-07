// Build a single-file, searchable HTML help bundle from docs/*.md.
// Output: docs/html/LensHH-LT-Help.html (self-contained — no external
// fetches, so it works from file:// on any OS).
//
// Usage: node build-help.js <docsDir> <outputFile> [version-string]
//
// Structure of the output page:
//   - Top bar: search input + title.
//   - Left sidebar: outline of all section headings (h1 + h2 anchors).
//   - Right pane: all chapters concatenated, each as a <section id=...>.
//   - Embedded lunr index + script that filters the sidebar on type,
//     highlights matches, and scrolls to clicked results.

const fs     = require('fs');
const path   = require('path');
const { marked } = require('marked');
const lunr   = require('lunr');

const docsDir    = process.argv[2];
const outputFile = process.argv[3];
const version    = process.argv[4] || 'dev';

const order = [
  { file: 'README.md',          title: 'Overview' },
  { file: 'getting-started.md', title: 'Getting Started' },
  { file: 'analyses.md',        title: 'Analyses Reference' },
  { file: 'merit-function.md',  title: 'Merit Function Reference' },
  { file: 'optimization.md',    title: 'Optimization' },
  { file: 'glass-catalogs.md',  title: 'Glass Catalogs' },
  { file: 'api-cli-mcp.md',     title: 'API, CLI, and MCP' },
];

marked.setOptions({ gfm: true, headerIds: false, mangle: false });

// Slugify a heading text into a URL-safe id. Collisions are avoided by
// appending "-N" suffixes while walking the document.
function slugify(text, used) {
  let base = text
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '') || 'section';
  let slug = base;
  let n = 2;
  while (used.has(slug)) slug = `${base}-${n++}`;
  used.add(slug);
  return slug;
}

// Render one file; return { html, sections } where sections is a list of
// { id, level, title, text } for every h1/h2 so the search index and
// sidebar outline can reference them. Headings in the rendered HTML
// are rewritten to carry stable `id="<slug>"` attributes via a
// post-pass (simpler than hooking into marked's renderer, which has
// changed API shape between marked versions).
function stripTags(s) {
  return s.replace(/<[^>]+>/g, '');
}

// Rewrite inter-file links (e.g. `[Getting Started](getting-started.md)`)
// into anchor references into the same consolidated page, so clicks
// inside the help bundle jump to the right chapter instead of asking the
// browser to load a nonexistent sibling .md file. Takes a map from
// lowercase filename to the chapter id that file became.
function rewriteMdLinks(mdText, fileToAnchor) {
  // [text](foo.md)            ->  [text](#anchor)
  // [text](foo.md#sub)         ->  [text](#sub)
  return mdText.replace(
    /(\]\()([^)\s]+?\.md)(#[^)]*)?(\))/gi,
    (_full, open, target, frag, close) => {
      const fname = target.toLowerCase();
      const anchor = fileToAnchor.get(fname);
      if (!anchor) return _full; // unknown link — leave as-is
      return open + (frag ? frag : '#' + anchor) + close;
    }
  );
}

function renderFile(mdText, chapterId, chapterTitle, usedSlugs) {
  const tokens = marked.lexer(mdText);
  const sections = [];
  let currentText = chapterTitle + ' ';

  for (const tok of tokens) {
    if (tok.type === 'heading' && (tok.depth === 1 || tok.depth === 2)) {
      if (sections.length > 0) {
        sections[sections.length - 1].text = currentText.trim();
      }
      const plain = stripTags(tok.text);
      const slug = slugify(plain, usedSlugs);
      sections.push({
        id: slug,
        level: tok.depth,
        title: plain,
        chapter: chapterTitle,
        text: ''
      });
      currentText = plain + ' ';
    } else if (tok.raw) {
      currentText += tok.raw + ' ';
    }
  }
  if (sections.length > 0) {
    sections[sections.length - 1].text = currentText.trim();
  } else {
    sections.push({
      id: chapterId,
      level: 1,
      title: chapterTitle,
      chapter: chapterTitle,
      text: currentText.trim()
    });
  }

  // Render the raw markdown, then walk the HTML and inject id attrs
  // onto h1 and h2 elements in order. Slugs are already assigned above
  // in the same document-order walk, so matching is just "kth heading
  // gets kth slug of that level".
  let html = marked.parse(mdText);
  // Help bundle is written to docs/html/LensHH-LT-Help.html, but image
  // assets sit at docs/images/. Rewrite relative image paths so the
  // help viewer finds them. Skips absolute URLs and data: URIs.
  html = html.replace(
    /<img(\s[^>]*?)src=(["'])(?!https?:|data:|\/|\.\.\/)([^"']*?)\2/g,
    '<img$1src=$2../$3$2'
  );
  let h1Idx = 0, h2Idx = 0;
  const h1Slugs = sections.filter(s => s.level === 1).map(s => s.id);
  const h2Slugs = sections.filter(s => s.level === 2).map(s => s.id);
  html = html.replace(/<h([12])(?![^>]*id=)>([^<]*)<\/h\1>/g, (_m, level, inner) => {
    const lvl = Number(level);
    const id = lvl === 1 ? (h1Slugs[h1Idx++] || '') : (h2Slugs[h2Idx++] || '');
    return id ? `<h${lvl} id="${id}">${inner}</h${lvl}>` : `<h${lvl}>${inner}</h${lvl}>`;
  });

  return { html, sections };
}

// Assign a chapter id to each source file up front so link-rewriting in
// the render pass has a full fileName -> anchor map available.
const usedSlugs = new Set();
const fileToAnchor = new Map();
const chapterIds = new Map();
for (const entry of order) {
  const id = slugify(entry.title, usedSlugs);
  chapterIds.set(entry.file, id);
  fileToAnchor.set(entry.file.toLowerCase(), id);
}

// Read all files.
const chapters = [];
for (const entry of order) {
  const p = path.join(docsDir, entry.file);
  if (!fs.existsSync(p)) continue;
  let md = fs.readFileSync(p, 'utf-8');
  md = rewriteMdLinks(md, fileToAnchor);
  // Strip the first H1 of each source file — we wrap the file in a
  // chapter-titled h1 of our own so every document starts at the same
  // level regardless of source markdown structure.
  const stripped = md.replace(/^#\s+.+\n+/, '');
  const chapterId = chapterIds.get(entry.file);
  const rendered = renderFile(stripped, chapterId, entry.title, usedSlugs);
  rendered.sections.unshift({
    id: chapterId, level: 1, title: entry.title,
    chapter: entry.title, text: entry.title
  });
  chapters.push({
    id: chapterId,
    title: entry.title,
    html: rendered.html,
    sections: rendered.sections,
  });
}

// Build the lunr index.
const allDocs = [];
for (const chap of chapters) {
  // Chapter-level bucket so a search like "glass" matches the chapter
  // title even when a child section is a better hit.
  allDocs.push({
    id: chap.id,
    title: chap.title,
    chapter: chap.title,
    level: 1,
    body: chap.sections.map(s => s.text).join(' '),
  });
  for (const s of chap.sections) {
    allDocs.push({
      id: s.id,
      title: s.title,
      chapter: chap.title,
      level: s.level,
      body: s.text,
    });
  }
}

const idx = lunr(function () {
  this.ref('id');
  this.field('title', { boost: 10 });
  this.field('body');
  for (const d of allDocs) this.add(d);
});

// Meta used by the search UI: title / chapter lookup by id.
const meta = {};
for (const d of allDocs) {
  meta[d.id] = { title: d.title, chapter: d.chapter, level: d.level };
}

// Sidebar outline.
function sidebarHtml() {
  let out = '<nav class="sidebar"><h2>Contents</h2>';
  for (const chap of chapters) {
    out += `<div class="chap"><a href="#${chap.id}" class="chap-link">${chap.title}</a>`;
    const subs = chap.sections.filter(s => s.level === 2);
    if (subs.length > 0) {
      out += '<ul>';
      for (const s of subs) out += `<li><a href="#${s.id}">${s.title}</a></li>`;
      out += '</ul>';
    }
    out += '</div>';
  }
  out += '</nav>';
  return out;
}

// Main content.
function contentHtml() {
  let out = '<main class="content">';
  for (const chap of chapters) {
    out += `<section class="chapter">
  <h1 id="${chap.id}">${chap.title}</h1>
  ${chap.html}
</section>`;
  }
  out += '</main>';
  return out;
}

const css = `
* { box-sizing: border-box; }
body { margin: 0; font-family: "Segoe UI", Arial, sans-serif; font-size: 14px; line-height: 1.55; color: #222; }
header { position: sticky; top: 0; z-index: 10; background: #2b3a48; color: #fff; padding: 10px 18px; display: flex; align-items: center; gap: 16px; box-shadow: 0 1px 4px rgba(0,0,0,0.25); }
header h1 { margin: 0; font-size: 18px; font-weight: 600; }
header .version { font-size: 12px; opacity: 0.7; margin-left: auto; }
#q { flex: 1; max-width: 420px; padding: 6px 10px; font-size: 14px; border: 1px solid #556; border-radius: 3px; background: #fff; color: #222; }
.layout { display: grid; grid-template-columns: 280px 1fr; min-height: calc(100vh - 50px); }
.sidebar { background: #f3f4f6; border-right: 1px solid #dcdde0; padding: 16px 14px; overflow-y: auto; max-height: calc(100vh - 50px); position: sticky; top: 50px; }
.sidebar h2 { font-size: 13px; text-transform: uppercase; letter-spacing: 0.05em; color: #666; margin: 0 0 8px; }
.sidebar .chap { margin-bottom: 10px; }
.sidebar .chap-link { font-weight: 600; color: #2b3a48; text-decoration: none; }
.sidebar ul { list-style: none; padding-left: 12px; margin: 4px 0 0; }
.sidebar li { margin: 2px 0; }
.sidebar li a, .sidebar a { color: #2b3a48; text-decoration: none; font-size: 13px; }
.sidebar li a:hover, .sidebar a:hover { text-decoration: underline; }
.sidebar .results { margin-top: 0; }
.sidebar .result { padding: 8px 10px; margin: 2px 0; background: #fff; border: 1px solid #e0e2e5; border-radius: 3px; }
.sidebar .result .r-chap { font-size: 11px; color: #888; text-transform: uppercase; letter-spacing: 0.04em; }
.sidebar .result .r-title { font-weight: 600; color: #2b3a48; }
.sidebar .empty { color: #888; font-style: italic; }
.content { padding: 24px 34px 80px; max-width: 1100px; overflow-x: auto; }
.content img { max-width: 100%; height: auto; display: block; margin: 1em 0; }
.content td img { margin: 0; }
.content h1 { font-size: 26px; border-bottom: 2px solid #2b3a48; padding-bottom: 6px; margin-top: 32px; }
.content h1:first-child { margin-top: 0; }
.content h2 { font-size: 19px; margin-top: 1.6em; border-bottom: 1px solid #d0d2d6; padding-bottom: 3px; }
.content h3 { font-size: 15px; margin-top: 1.2em; }
.content code { font-family: Consolas, "Courier New", monospace; background: #f3f3f3; padding: 1px 5px; border-radius: 3px; font-size: 0.92em; }
.content pre { background: #f3f3f3; padding: 10px 14px; border-radius: 4px; overflow-x: auto; font-size: 0.88em; }
.content pre code { background: none; padding: 0; }
.content table { border-collapse: collapse; width: 100%; margin: 1em 0; font-size: 0.95em; }
.content th, .content td { border: 1px solid #b5b7ba; padding: 6px 10px; text-align: left; vertical-align: top; }
.content th { background: #eef0f2; font-weight: 600; }
.content a { color: #0a4d8c; text-decoration: none; }
.content a:hover { text-decoration: underline; }
.content blockquote { margin: 1em 0; padding: 0 1em; border-left: 4px solid #d0d2d6; color: #555; }
mark { background: #ffe88a; padding: 0 2px; border-radius: 2px; }
@media (max-width: 800px) { .layout { grid-template-columns: 1fr; } .sidebar { position: static; max-height: none; } }
`;

// The embedded lunr runtime + our tiny search UI.
// We read lunr.min.js from the installed module so the page is truly
// self-contained.
const lunrLibPath = require.resolve('lunr');
// lunr doesn't ship a prebuilt minified standalone; use the plain .js
// and rely on gzip (Setup.exe compression handles it).
const lunrSource = fs.readFileSync(lunrLibPath, 'utf-8');

const searchJs = `
const IDX = lunr.Index.load(${JSON.stringify(idx.toJSON())});
const META = ${JSON.stringify(meta)};
const q = document.getElementById('q');
const sidebarDefault = document.getElementById('sidebar-default').innerHTML;
const sidebar = document.querySelector('.sidebar');

function clearHighlights(root) {
  root.querySelectorAll('mark.__hit').forEach(m => {
    const p = m.parentNode; p.replaceChild(document.createTextNode(m.textContent), m); p.normalize();
  });
}

function highlight(terms) {
  const content = document.querySelector('.content');
  clearHighlights(content);
  if (!terms.length) return;
  const rx = new RegExp('(' + terms.map(t => t.replace(/[.*+?^\${}()|[\\]\\\\]/g, '\\\\$&')).join('|') + ')', 'gi');
  const walker = document.createTreeWalker(content, NodeFilter.SHOW_TEXT, {
    acceptNode: n => n.parentElement.closest('pre, code, mark, script, style') ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT
  });
  const nodes = []; let n; while (n = walker.nextNode()) nodes.push(n);
  for (const node of nodes) {
    const text = node.nodeValue;
    if (!rx.test(text)) continue;
    const html = text.replace(rx, '<mark class="__hit">$1</mark>');
    const span = document.createElement('span');
    span.innerHTML = html;
    node.parentNode.replaceChild(span, node);
  }
}

function showResults(results, terms) {
  sidebar.innerHTML = '<h2>Results (' + results.length + ')</h2><div class="results"></div>';
  const box = sidebar.querySelector('.results');
  if (results.length === 0) {
    box.innerHTML = '<div class="empty">No matches. Try a different term.</div>';
    return;
  }
  for (const r of results.slice(0, 40)) {
    const m = META[r.ref] || { title: r.ref, chapter: '', level: 2 };
    const a = document.createElement('a');
    a.href = '#' + r.ref;
    a.className = 'result';
    a.innerHTML = '<div class="r-chap">' + m.chapter + '</div><div class="r-title">' + m.title + '</div>';
    box.appendChild(a);
  }
}

let debounceTimer = null;
q.addEventListener('input', () => {
  clearTimeout(debounceTimer);
  debounceTimer = setTimeout(() => {
    const query = q.value.trim();
    if (!query) {
      sidebar.innerHTML = sidebarDefault;
      clearHighlights(document.querySelector('.content'));
      return;
    }
    let results = [];
    try {
      // Wildcard the user's terms so partial matches work.
      const expanded = query.split(/\\s+/).map(t => t.endsWith('*') ? t : t + '*').join(' ');
      results = IDX.search(expanded);
    } catch (e) { results = []; }
    const terms = query.split(/\\s+/).filter(Boolean);
    showResults(results, terms);
    highlight(terms);
  }, 120);
});

// ctrl/cmd+K focuses the search box.
document.addEventListener('keydown', e => {
  if ((e.ctrlKey || e.metaKey) && (e.key === 'k' || e.key === 'K')) {
    e.preventDefault(); q.focus(); q.select();
  }
});
`;

const html = `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>LensHH-LT Help</title>
<style>${css}</style>
</head>
<body>
<header>
  <h1>LensHH-LT Help</h1>
  <input id="q" type="search" placeholder="Search... (Ctrl+K)" autocomplete="off">
  <span class="version">v${version}</span>
</header>
<div class="layout">
  <div id="sidebar-default" style="display:none">${sidebarHtml().replace(/<nav class="sidebar">|<\/nav>/g, '')}</div>
  ${sidebarHtml()}
  ${contentHtml()}
</div>
<script>${lunrSource}</script>
<script>${searchJs}</script>
</body>
</html>`;

fs.mkdirSync(path.dirname(outputFile), { recursive: true });
fs.writeFileSync(outputFile, html);
console.log(`Wrote ${outputFile} (${html.length} bytes, ${allDocs.length} indexed sections)`);
