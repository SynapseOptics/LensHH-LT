// One-off: generate grouped-bar SVG charts for the Asphere case study
// (RMS spot radius + polychromatic RMS wavefront error vs field, 3 stages).
// Data from MeritEvalBench --mode analyze on the three sample designs.
// Output: docs/images/AsphereExploration/AsphereSpotVsField.svg + AsphereWfeVsField.svg
const fs = require('fs');
const path = require('path');

const fields = [0, 5, 8, 11, 14, 17, 20];
const series = [
  { key: 'Before (spherical)',        color: '#c0504d' },
  { key: 'After asphere',             color: '#4f81bd' },
  { key: 'After asphere + BH',        color: '#9bbb59' },
];
// [before, afterAsphere, afterBH] per field
const spot = {  // RMS spot radius, µm (polychromatic)
  0:  [3.147, 3.342, 4.489],
  5:  [3.021, 2.867, 4.157],
  8:  [2.922, 2.333, 3.636],
  11: [3.222, 2.328, 2.918],
  14: [4.601, 3.628, 2.561],
  17: [7.397, 6.035, 4.030],
  20: [11.594, 9.214, 7.207],
};
const wfe = {   // RMS wavefront error, waves (polychromatic weighted-RMS over 0.48/0.55/0.65 µm)
  0:  [0.086, 0.067, 0.045],
  5:  [0.152, 0.086, 0.052],
  8:  [0.195, 0.091, 0.056],
  11: [0.209, 0.087, 0.069],
  14: [0.187, 0.087, 0.064],
  17: [0.137, 0.105, 0.077],
  20: [0.147, 0.132, 0.126],
};

function chart(data, { title, yLabel, yMax, yTicks }) {
  const W = 720, H = 380;
  const m = { l: 64, r: 16, t: 44, b: 76 };
  const pw = W - m.l - m.r, ph = H - m.t - m.b;
  const groups = fields.length, bars = series.length;
  const groupW = pw / groups;
  const barGap = 4, groupPad = 0.18 * groupW;
  const barW = (groupW - 2 * groupPad - (bars - 1) * barGap) / bars;
  const y = v => m.t + ph - (v / yMax) * ph;
  let s = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W} ${H}" font-family="Segoe UI, Arial, sans-serif">\n`;
  s += `<rect width="${W}" height="${H}" fill="white"/>\n`;
  s += `<text x="${W / 2}" y="24" text-anchor="middle" font-size="16" font-weight="600">${title}</text>\n`;
  // y gridlines + ticks
  for (let i = 0; i <= yTicks; i++) {
    const v = (yMax * i) / yTicks, yy = y(v);
    s += `<line x1="${m.l}" y1="${yy.toFixed(1)}" x2="${W - m.r}" y2="${yy.toFixed(1)}" stroke="#e6e6e6"/>\n`;
    s += `<text x="${m.l - 8}" y="${(yy + 4).toFixed(1)}" text-anchor="end" font-size="11" fill="#555">${v.toFixed(v < 1 ? 2 : 0)}</text>\n`;
  }
  s += `<text x="16" y="${m.t + ph / 2}" text-anchor="middle" font-size="12" fill="#333" transform="rotate(-90 16 ${m.t + ph / 2})">${yLabel}</text>\n`;
  // axis
  s += `<line x1="${m.l}" y1="${m.t + ph}" x2="${W - m.r}" y2="${m.t + ph}" stroke="#888"/>\n`;
  // bars
  fields.forEach((f, gi) => {
    const gx = m.l + gi * groupW + groupPad;
    data[f].forEach((v, si) => {
      const x = gx + si * (barW + barGap);
      const yy = y(v), hh = m.t + ph - yy;
      s += `<rect x="${x.toFixed(1)}" y="${yy.toFixed(1)}" width="${barW.toFixed(1)}" height="${hh.toFixed(1)}" fill="${series[si].color}"/>\n`;
      s += `<text x="${(x + barW / 2).toFixed(1)}" y="${(yy - 3).toFixed(1)}" text-anchor="middle" font-size="9" fill="#333">${v.toFixed(v < 1 ? 2 : 1)}</text>\n`;
    });
    s += `<text x="${(m.l + gi * groupW + groupW / 2).toFixed(1)}" y="${m.t + ph + 18}" text-anchor="middle" font-size="12">${f}°</text>\n`;
  });
  s += `<text x="${W / 2}" y="${H - 40}" text-anchor="middle" font-size="12" fill="#333">Field angle</text>\n`;
  // legend
  const lgY = H - 20, lgTot = series.length * 190;
  let lx = (W - lgTot) / 2 + 20;
  series.forEach(se => {
    s += `<rect x="${lx}" y="${lgY - 10}" width="13" height="13" fill="${se.color}"/>\n`;
    s += `<text x="${lx + 18}" y="${lgY + 1}" font-size="12" fill="#333">${se.key}</text>\n`;
    lx += 190;
  });
  s += `</svg>\n`;
  return s;
}

const outDir = path.join(__dirname, '..', 'images', 'AsphereExploration');
fs.writeFileSync(path.join(outDir, 'AsphereSpotVsField.svg'),
  chart(spot, { title: 'RMS spot radius vs field', yLabel: 'RMS spot radius (µm)', yMax: 12, yTicks: 6 }));
fs.writeFileSync(path.join(outDir, 'AsphereWfeVsField.svg'),
  chart(wfe, { title: 'RMS wavefront error vs field (polychromatic)', yLabel: 'RMS WFE (waves)', yMax: 0.24, yTicks: 6 }));
console.log('Wrote AsphereSpotVsField.svg + AsphereWfeVsField.svg to', outDir);
