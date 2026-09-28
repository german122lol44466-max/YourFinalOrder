// Your Last Order — логотип, аватарка и иконка.
// Запуск:  NODE_PATH=$(npm root -g) node Tools/Branding/build_logo.mjs
// Нужен Playwright с Chromium. Результат: Branding/*.svg|png и Assets/_Project/Art/Branding/*.png
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const { chromium } = require("playwright");

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const FONTS = path.join(ROOT, "Assets/_Project/Art/Fonts");
const OUT = path.join(ROOT, "Branding");
const UNITY_OUT = path.join(ROOT, "Assets/_Project/Art/Branding");
fs.mkdirSync(OUT, { recursive: true });
fs.mkdirSync(UNITY_OUT, { recursive: true });

// Шрифты встраиваются в SVG (base64): файл самодостаточен и рендерится где угодно
const b64 = (f) => fs.readFileSync(path.join(FONTS, f)).toString("base64");
const FONT_CSS = `
  @font-face { font-family: "BlackOps"; src: url(data:font/ttf;base64,${b64("BlackOpsOne-Regular.ttf")}); }
  @font-face { font-family: "Russo"; src: url(data:font/ttf;base64,${b64("RussoOne-Regular.ttf")}); }`;
const fontFaces = () => FONT_CSS;

// ---------------------------------------------------------------- общие фильтры

const defs = `
  <filter id="grime" x="0" y="0" width="100%" height="100%">
    <feTurbulence type="fractalNoise" baseFrequency="0.45" numOctaves="3" seed="4" result="n"/>
    <feColorMatrix in="n" type="matrix" values="0 0 0 0 0.12  0 0 0 0 0.08  0 0 0 0 0.04  0 0 0 -5 2.2" result="spots"/>
    <feComposite in="spots" in2="SourceGraphic" operator="in" result="spotsIn"/>
    <feTurbulence type="fractalNoise" baseFrequency="0.035" numOctaves="2" seed="9" result="big"/>
    <feColorMatrix in="big" type="matrix" values="0 0 0 0 0.1  0 0 0 0 0.06  0 0 0 0 0.02  0 0 0 -2.6 1.35" result="stains"/>
    <feComposite in="stains" in2="SourceGraphic" operator="in" result="stainsIn"/>
    <feMerge><feMergeNode in="SourceGraphic"/><feMergeNode in="stainsIn"/><feMergeNode in="spotsIn"/></feMerge>
  </filter>
  <filter id="worn" x="-5%" y="-5%" width="110%" height="110%">
    <feTurbulence type="fractalNoise" baseFrequency="0.55" numOctaves="2" seed="2" result="n"/>
    <feColorMatrix in="n" type="matrix" values="0 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 -11 3.9" result="holes"/>
    <feComposite in="SourceGraphic" in2="holes" operator="out" result="cut"/>
    <feTurbulence type="fractalNoise" baseFrequency="0.012 0.4" numOctaves="1" seed="5" result="s"/>
    <feColorMatrix in="s" type="matrix" values="0 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 -20 6.9" result="scratches"/>
    <feComposite in="cut" in2="scratches" operator="out"/>
  </filter>
  <filter id="glow" x="-100%" y="-100%" width="300%" height="300%">
    <feGaussianBlur stdDeviation="5" result="b"/>
    <feMerge><feMergeNode in="b"/><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge>
  </filter>
  <filter id="softglow" x="-50%" y="-50%" width="200%" height="200%">
    <feGaussianBlur stdDeviation="14"/>
  </filter>
  <radialGradient id="eyeGrad">
    <stop offset="0" stop-color="#FAFFB0"/>
    <stop offset="0.45" stop-color="#D8FF3C"/>
    <stop offset="1" stop-color="#6FA000"/>
  </radialGradient>
  <linearGradient id="tentacle" x1="0" y1="1" x2="1" y2="0">
    <stop offset="0" stop-color="#241521"/>
    <stop offset="1" stop-color="#4A2B45"/>
  </linearGradient>`;

// ---------------------------------------------------------------- эмблема (коробка 400×400)

function emblem() {
  // лицевые плоскости изометрической коробки
  const L = "matrix(1,0.5,0,1,60,130)";   // левая грань, u∈[0,140], v∈[0,160]
  const R = "matrix(1,-0.5,0,1,200,200)"; // правая грань
  const T = "matrix(1,-0.5,1,0.5,60,130)"; // верх, a,b∈[0,140]
  const hole = "38,40 55,27 72,37 90,24 105,41 99,60 109,79 92,97 76,89 60,105 44,90 33,70 43,57";
  const bars = [0, 3, 5, 9, 11, 14, 18, 20, 23, 27, 29, 32, 36, 38, 41, 44]
    .map((x, i) => `<rect x="${20 + x}" y="76" width="${i % 3 ? 1.2 : 2.2}" height="18" fill="#1b1712"/>`).join("");
  return `
  <g class="emblem">
    <ellipse cx="200" cy="372" rx="150" ry="26" fill="#000" opacity="0.45" filter="url(#softglow)"/>
    <g filter="url(#grime)">
      <polygon points="60,130 200,200 200,360 60,290" fill="#8A6A40"/>
      <polygon points="200,200 340,130 340,290 200,360" fill="#6A4F2E"/>
      <polygon points="200,60 340,130 200,200 60,130" fill="#B08A56"/>
      <g transform="${T}">
        <rect x="60" y="0" width="20" height="140" fill="#D6B35C" opacity="0.92"/>
        <line x1="0" y1="70" x2="140" y2="70" stroke="#5B4326" stroke-width="1.2" opacity="0.7"/>
      </g>
      <g transform="${L}">
        <rect x="16" y="36" width="106" height="72" fill="#E6DECB"/>
        <rect x="16" y="36" width="106" height="13" fill="#E8772E"/>
        <text x="20" y="46" font-family="Russo" font-size="8" textLength="98" lengthAdjust="spacingAndGlyphs" fill="#17140F">YOU CAN BUY EVERYTHING</text>
        <text x="20" y="60" font-family="Russo" font-size="8" fill="#17140F">ЗАКАЗ № 0000-LAST</text>
        <text x="20" y="70" font-family="Russo" font-size="6.5" fill="#5A544A">ПОЛУЧАТЕЛЬ: ???</text>
        ${bars}
        <text x="62" y="101" font-family="Russo" font-size="9" fill="#B8231C" transform="rotate(-8 82 97)">ХРУПКОЕ</text>
      </g>
      <g transform="${R}">
        <polygon points="60,0 80,0 80,22 74,26 68,21 60,25" fill="#D6B35C" opacity="0.92"/>
        <polygon points="${hole}" fill="#0B0806"/>
        <polygon points="${hole}" fill="none" stroke="#3B2A18" stroke-width="3"/>
        <path d="M105,41 L128,30 M109,79 L134,92 M60,105 L52,132 M33,70 L10,76 M92,97 L100,124" stroke="#2A1D10" stroke-width="1.6" fill="none"/>
      </g>
    </g>
    <g transform="${R}">
      <ellipse cx="58" cy="63" rx="8" ry="7" fill="url(#eyeGrad)" filter="url(#glow)"/>
      <ellipse cx="86" cy="63" rx="8" ry="7" fill="url(#eyeGrad)" filter="url(#glow)"/>
      <ellipse cx="58" cy="63" rx="1.6" ry="5" fill="#1a1a05"/>
      <ellipse cx="86" cy="63" rx="1.6" ry="5" fill="#1a1a05"/>
    </g>
    <!-- щупальце -->
    <path d="M286,182 C312,160 318,120 352,104 C380,92 392,62 372,44 C356,31 334,40 340,58 C344,70 358,66 356,58"
          fill="none" stroke="url(#tentacle)" stroke-width="17" stroke-linecap="round"/>
    <path d="M286,182 C312,160 318,120 352,104 C380,92 392,62 372,44 C356,31 334,40 340,58"
          fill="none" stroke="#7A4E72" stroke-width="3" stroke-linecap="round" opacity="0.7" transform="translate(-3,-3)"/>
    ${[[300, 164], [314, 140], [330, 116], [352, 101], [372, 86], [383, 64]].map(([x, y], i) =>
      `<circle cx="${x + 5}" cy="${y + 4}" r="${3.4 - i * 0.35}" fill="#C79BB6" opacity="0.8"/>`).join("")}
  </g>`;
}

// ---------------------------------------------------------------- полный логотип 1600×900

function logoSvg(fontBase, withBackground) {
  const title = "YOUR LAST ORDER";
  const titleAttrs = `x="800" y="590" text-anchor="middle" font-family="BlackOps" font-size="136" textLength="1380" lengthAdjust="spacingAndGlyphs"`;
  const bg = withBackground ? `
    <radialGradient id="bg" cx="0.5" cy="0.42" r="0.75">
      <stop offset="0" stop-color="#1E1A14"/><stop offset="1" stop-color="#050505"/>
    </radialGradient>` : "";
  return `<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="900" viewBox="0 0 1600 900">
  <style>${fontFaces(fontBase)}</style>
  <defs>${defs}${bg}
    <clipPath id="slice"><rect x="0" y="518" width="1600" height="22"/></clipPath>
    <clipPath id="noslice"><rect x="0" y="0" width="1600" height="518"/><rect x="0" y="540" width="1600" height="400"/></clipPath>
  </defs>
  ${withBackground ? `<rect width="1600" height="900" fill="url(#bg)"/>` : ""}
  <g transform="translate(565,8) scale(1.175)">${emblem()}</g>

  <g filter="url(#worn)">
    <text ${titleAttrs} fill="#FF2A46" opacity="0.75" transform="translate(-6,1)">${title}</text>
    <text ${titleAttrs} fill="#22E6FF" opacity="0.65" transform="translate(6,-1)">${title}</text>
    <g clip-path="url(#noslice)"><text ${titleAttrs} fill="#EDE6D3">${title}</text></g>
    <g clip-path="url(#slice)" transform="translate(22,0)"><text ${titleAttrs} fill="#EDE6D3">${title}</text></g>
  </g>

  <g transform="rotate(-1.6 800 650)" filter="url(#grime)">
    <polygon points="380,618 392,612 404,620 1196,612 1208,620 1222,614 1216,684 1204,690 1192,682 400,690 388,684 376,690" fill="#E2B232"/>
    <text x="800" y="664" text-anchor="middle" font-family="Russo" font-size="40" letter-spacing="7" fill="#17140F">ТВОЙ ПОСЛЕДНИЙ ЗАКАЗ</text>
  </g>
  <text x="800" y="748" text-anchor="middle" font-family="Russo" font-size="19" letter-spacing="5" fill="#8C877A">КУРЬЕРСКАЯ СЛУЖБА «YOU CAN BUY EVERYTHING» · ВОЗВРАТОВ НЕТ</text>
</svg>`;
}

// ---------------------------------------------------------------- аватарка / иконка (квадрат)

function avatarSvg(fontBase) {
  return `<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024">
  <style>${fontFaces(fontBase)}</style>
  <defs>${defs}
    <radialGradient id="abg" cx="0.5" cy="0.45" r="0.72">
      <stop offset="0" stop-color="#2A2218"/><stop offset="0.65" stop-color="#0E0C0A"/><stop offset="1" stop-color="#030303"/>
    </radialGradient>
    <radialGradient id="eyehalo" cx="0.5" cy="0.5" r="0.5">
      <stop offset="0" stop-color="#D8FF3C" stop-opacity="0.35"/><stop offset="1" stop-color="#D8FF3C" stop-opacity="0"/>
    </radialGradient>
  </defs>
  <rect width="1024" height="1024" fill="url(#abg)"/>
  <circle cx="610" cy="470" r="260" fill="url(#eyehalo)"/>
  <g transform="translate(92,70) scale(2.1)">${emblem()}</g>
  <rect x="24" y="24" width="976" height="976" rx="120" fill="none" stroke="#E8772E" stroke-width="10" opacity="0.55"/>
</svg>`;
}

// ---------------------------------------------------------------- рендер

async function render(page, svg, w, h, file, transparent) {
  await page.setViewportSize({ width: w, height: h });
  await page.setContent(`<!doctype html><html><body style="margin:0;background:transparent">${svg}</body></html>`);
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(150);
  await page.locator("svg").screenshot({ path: file, omitBackground: transparent });
  console.log("→", path.relative(ROOT, file));
}

const fileFonts = "file://" + FONTS;
const browser = await chromium.launch();
const page = await browser.newPage();

// исходники SVG (шрифты по относительному пути)
fs.writeFileSync(path.join(OUT, "logo.svg"), logoSvg("../Assets/_Project/Art/Fonts", false));
fs.writeFileSync(path.join(OUT, "avatar.svg"), avatarSvg("../Assets/_Project/Art/Fonts"));

await render(page, logoSvg(fileFonts, false), 1600, 900, path.join(OUT, "logo.png"), true);
await render(page, logoSvg(fileFonts, true), 1600, 900, path.join(OUT, "logo_dark.png"), false);
await render(page, avatarSvg(fileFonts), 1024, 1024, path.join(OUT, "avatar.png"), false);
fs.copyFileSync(path.join(OUT, "logo.png"), path.join(UNITY_OUT, "Logo.png"));
fs.copyFileSync(path.join(OUT, "avatar.png"), path.join(UNITY_OUT, "Icon.png"));

// Steam-аватар 184×184
await page.setViewportSize({ width: 184, height: 184 });
await page.setContent(`<!doctype html><html><body style="margin:0"><img src="file://${path.join(OUT, "avatar.png")}" width="184" height="184"></body></html>`);
await page.waitForTimeout(100);
await page.locator("img").screenshot({ path: path.join(OUT, "avatar_184.png") });
console.log("→ Branding/avatar_184.png");

await browser.close();
