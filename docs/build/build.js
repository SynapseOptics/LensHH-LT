// Concatenates the LensHH-LT markdown docs in a fixed order and emits a
// single styled HTML file suitable for print-to-PDF via Chromium-based
// headless browsers. Usage: node build.js <docsDir> <outputHtml>.
const fs   = require('fs');
const path = require('path');
const { marked } = require('marked');

const docsDir    = process.argv[2];
const outputHtml = process.argv[3];
const version    = process.argv[4] || 'dev';

const order = [
  { file: 'README.md',          title: 'Overview' },
  { file: 'getting-started.md', title: 'Getting Started' },
  { file: 'analyses.md',        title: 'Analyses Reference' },
  { file: 'merit-function.md',  title: 'Merit Function Reference' },
  { file: 'optimization.md',    title: 'Optimization' },
  { file: 'glass-catalogs.md',  title: 'Glass Catalogs' },
  { file: 'api-cli-mcp.md',     title: 'API, CLI, and MCP' },
  { file: 'building.md',        title: 'Building from Source' },
];

const css = `
  @page { size: Letter; margin: 0.8in 0.75in; }
  body { font-family: "Segoe UI", Arial, sans-serif; font-size: 10.5pt; line-height: 1.45; color: #222; }
  h1 { font-size: 22pt; border-bottom: 2px solid #333; padding-bottom: 6px; page-break-before: always; margin-top: 0; }
  h1.first { page-break-before: avoid; }
  h2 { font-size: 14.5pt; margin-top: 1.5em; border-bottom: 1px solid #bbb; padding-bottom: 3px; }
  h3 { font-size: 12pt; margin-top: 1.2em; }
  h4 { font-size: 11pt; margin-top: 1em; }
  p, li { orphans: 3; widows: 3; }
  code { font-family: Consolas, "Courier New", monospace; font-size: 0.92em; background: #f3f3f3; padding: 1px 4px; border-radius: 3px; }
  pre { background: #f3f3f3; padding: 10px 12px; border-radius: 4px; overflow-x: auto; font-size: 0.88em; page-break-inside: avoid; }
  pre code { background: none; padding: 0; }
  table { border-collapse: collapse; width: 100%; margin: 0.8em 0; font-size: 0.95em; page-break-inside: auto; }
  thead { display: table-header-group; }
  tr { page-break-inside: avoid; }
  th, td { border: 1px solid #b0b0b0; padding: 5px 8px; text-align: left; vertical-align: top; }
  th { background: #e8e8e8; font-weight: 600; }
  blockquote { margin: 1em 0; padding: 0 1em; border-left: 4px solid #ccc; color: #555; font-style: italic; }
  a { color: #0a4d8c; text-decoration: none; }
  /* max-height keeps a tall flowchart from being cut by a page break.
     Letter content area is 7.0in × 9.4in after the 0.8/0.75in margins;
     8.5in leaves room for a caption above and continuing prose below. */
  img { display: block; max-width: 100%; max-height: 8.5in; height: auto; margin: 0.6em auto; page-break-inside: avoid; border: 1px solid #ddd; box-shadow: 0 1px 3px rgba(0,0,0,0.08); object-fit: contain; }
  hr.page { border: none; page-break-after: always; height: 0; margin: 0; }
  .cover { text-align: center; padding-top: 2in; page-break-after: always; }
  .cover h1 { font-size: 34pt; border: none; margin-bottom: 12pt; }
  .cover .sub { font-size: 14pt; color: #555; }
  .cover .version { font-size: 11pt; color: #777; margin-top: 0.8in; }
  .toc { page-break-after: always; }
  .toc h2 { border: none; }
  .toc ol { font-size: 11pt; line-height: 2; padding-left: 0; list-style-position: inside; }
`;

marked.setOptions({ gfm: true, headerIds: false, mangle: false });

let html = `<!DOCTYPE html>
<html><head>
<meta charset="utf-8">
<title>LensHH-LT User Guide</title>
<style>${css}</style>
</head><body>
<div class="cover">
  <h1>LensHH-LT</h1>
  <div class="sub">User Guide</div>
  <div class="version">Version ${version}</div>
</div>
<div class="toc">
  <h2>Contents</h2>
  <ol>
    ${order.map(s => `<li>${s.title}</li>`).join('\n    ')}
  </ol>
</div>`;

let first = true;
for (const section of order) {
  const p = path.join(docsDir, section.file);
  if (!fs.existsSync(p)) continue;
  const md = fs.readFileSync(p, 'utf-8');
  // Strip the first H1 of each section so we insert our own with
  // consistent page-break behavior.
  const stripped = md.replace(/^#\s+.+\n+/, '');
  html += `\n<h1${first ? ' class="first"' : ''}>${section.title}</h1>\n`;
  html += marked.parse(stripped);
  first = false;
}

// Output HTML lives in docs/build/, but image assets sit at docs/images/.
// Rewrite relative image paths to climb one directory so the headless-
// browser PDF renderer finds them. Skips absolute URLs and data: URIs.
html = html.replace(
  /<img(\s[^>]*?)src=(["'])(?!https?:|data:|\/|\.\.\/)([^"']*?)\2/g,
  '<img$1src=$2../$3$2'
);

html += `</body></html>`;
fs.writeFileSync(outputHtml, html);
console.log(`Wrote ${outputHtml} (${html.length} bytes)`);
