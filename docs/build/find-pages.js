// Find page numbers in LensHH-LT-UserGuide.pdf where specific strings occur.
const fs = require('fs');
const path = require('path');
const { PDFParse } = require('pdf-parse');

const pdfPath = path.join(__dirname, '..', 'LensHH-LT-UserGuide.pdf');
const targets = [
  'Surface sentinel values',
  'PenalizeVignetting',
  'first refractive surface',
  'system-wide off-axis vignetting penalty',
  'Surface Property Operand',
  'Forbes Gauss-Legendre',
  'Boundary Operands',
  'Merit Function Reference',
];

(async () => {
  const data = new Uint8Array(fs.readFileSync(pdfPath));
  const parser = new PDFParse({ data });
  const result = await parser.getText();
  console.log(`Total pages: ${result.pages?.length ?? 'unknown'}`);

  const pages = result.pages || [];
  for (const target of targets) {
    const hits = [];
    for (let i = 0; i < pages.length; i++) {
      const txt = typeof pages[i] === 'string' ? pages[i] : (pages[i].text || JSON.stringify(pages[i]));
      if (txt.includes(target)) hits.push(i + 1);
    }
    if (hits.length) console.log(`  '${target}': page(s) ${hits.join(', ')}`);
    else console.log(`  '${target}': NOT FOUND`);
  }
})().catch(err => { console.error(err.message); process.exit(1); });
