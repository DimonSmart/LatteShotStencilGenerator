import * as opentype from 'opentype.js';
import neuchaBase64 from './assets/fonts/Neucha.ttf.b64?raw';
import poiretOneBase64 from './assets/fonts/PoiretOne-Regular.ttf.b64?raw';
import ptMonoBase64 from './assets/fonts/PTM55FT.ttf.b64?raw';
import russoOneBase64 from './assets/fonts/RussoOne-Regular.ttf.b64?raw';
import type { BundledFontId } from './font-catalog';

export type { BundledFontId } from './font-catalog';

export interface GlyphOutline {
  readonly contours: ReadonlyArray<ReadonlyArray<readonly [number, number]>>;
  readonly bounds: { readonly minX: number; readonly minY: number; readonly maxX: number; readonly maxY: number };
}

const glyphs: Record<string, readonly string[]> = {
  'A': ['01110','10001','10001','11111','10001','10001','10001'], 'B': ['11110','10001','10001','11110','10001','10001','11110'], 'C': ['01111','10000','10000','10000','10000','10000','01111'], 'D': ['11110','10001','10001','10001','10001','10001','11110'], 'E': ['11111','10000','10000','11110','10000','10000','11111'], 'F': ['11111','10000','10000','11110','10000','10000','10000'], 'G': ['01111','10000','10000','10111','10001','10001','01111'], 'H': ['10001','10001','10001','11111','10001','10001','10001'], 'I': ['11111','00100','00100','00100','00100','00100','11111'], 'J': ['00111','00010','00010','00010','10010','10010','01100'], 'K': ['10001','10010','10100','11000','10100','10010','10001'], 'L': ['10000','10000','10000','10000','10000','10000','11111'], 'M': ['10001','11011','10101','10101','10001','10001','10001'], 'N': ['10001','11001','10101','10011','10001','10001','10001'], 'O': ['01110','10001','10001','10001','10001','10001','01110'], 'P': ['11110','10001','10001','11110','10000','10000','10000'], 'Q': ['01110','10001','10001','10001','10101','10010','01101'], 'R': ['11110','10001','10001','11110','10100','10010','10001'], 'S': ['01111','10000','10000','01110','00001','00001','11110'], 'T': ['11111','00100','00100','00100','00100','00100','00100'], 'U': ['10001','10001','10001','10001','10001','10001','01110'], 'V': ['10001','10001','10001','10001','10001','01010','00100'], 'W': ['10001','10001','10001','10101','10101','10101','01010'], 'X': ['10001','10001','01010','00100','01010','10001','10001'], 'Y': ['10001','10001','01010','00100','00100','00100','00100'], 'Z': ['11111','00001','00010','00100','01000','10000','11111'],
  '0': ['01110','10001','10011','10101','11001','10001','01110'], '1': ['00100','01100','00100','00100','00100','00100','01110'], '2': ['01110','10001','00001','00010','00100','01000','11111'], '3': ['11110','00001','00001','01110','00001','00001','11110'], '4': ['00010','00110','01010','10010','11111','00010','00010'], '5': ['11111','10000','10000','11110','00001','00001','11110'], '6': ['01110','10000','10000','11110','10001','10001','01110'], '7': ['11111','00001','00010','00100','01000','01000','01000'], '8': ['01110','10001','10001','01110','10001','10001','01110'], '9': ['01110','10001','10001','01111','00001','00001','01110'],
  '?': ['01110','10001','00001','00010','00100','00000','00100'], '-': ['00000','00000','00000','11111','00000','00000','00000'], ' ': ['00000','00000','00000','00000','00000','00000','00000'], '.': ['00000','00000','00000','00000','00000','00110','00110'], '!': ['00100','00100','00100','00100','00100','00000','00100'],
};

function makeBlockFont(): any {
  const api = opentype as any;
  const fontGlyphs = Object.entries(glyphs).map(([character, rows]) => {
    const path = new api.Path();
    rows.forEach((row, y) => [...row].forEach((cell, x) => {
      if (cell !== '1') return;
      path.moveTo(x, 7 - y); path.lineTo(x + 1, 7 - y); path.lineTo(x + 1, 6 - y); path.lineTo(x, 6 - y); path.close();
    }));
    return new api.Glyph({ name: character, unicode: character.codePointAt(0), advanceWidth: 6, path });
  });
  const font = new api.Font({ familyName: 'Stencil Block', styleName: 'Regular', unitsPerEm: 7, ascender: 7, descender: 0, glyphs: fontGlyphs });
  font.kerningPairs = {};
  return font;
}

type ExternalFontId = Exclude<BundledFontId, 'stencil-block'>;
const externalFontSources: Record<ExternalFontId, string> = {
  'poiret-one': poiretOneBase64,
  'russo-one': russoOneBase64,
  'neucha': neuchaBase64,
  'pt-mono': ptMonoBase64,
};
const parsedFonts = new Map<BundledFontId, any>([['stencil-block', makeBlockFont()]]);

function parseBase64Font(source: string): any {
  const binary = globalThis.atob(source.trim());
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
  return (opentype as any).parse(bytes.buffer);
}

function bundledFont(fontId: BundledFontId): any {
  const cached = parsedFonts.get(fontId);
  if (cached) return cached;
  const source = externalFontSources[fontId as ExternalFontId];
  if (!source) throw new Error('Choose a bundled caption font.');
  const parsed = parseBase64Font(source);
  parsedFonts.set(fontId, parsed);
  return parsed;
}

type Point = readonly [number, number];
type PathCommand = { readonly type: string; readonly x?: number; readonly y?: number; readonly x1?: number; readonly y1?: number; readonly x2?: number; readonly y2?: number };
const CURVE_SEGMENTS = 16;
const POINT_EPSILON = 1e-10;

function appendPoint(contour: Point[], point: Point): void {
  const previous = contour[contour.length - 1];
  if (!previous || Math.abs(previous[0] - point[0]) > POINT_EPSILON || Math.abs(previous[1] - point[1]) > POINT_EPSILON) contour.push(point);
}

function flattenPath(commands: readonly PathCommand[]): Point[][] {
  const contours: Point[][] = [];
  let contour: Point[] | undefined;
  let current: Point = [0, 0];
  for (const command of commands) {
    if (command.type === 'M') {
      current = [command.x!, command.y!];
      contour = [current];
      contours.push(contour);
    } else if (command.type === 'L' && contour) {
      current = [command.x!, command.y!];
      appendPoint(contour, current);
    } else if (command.type === 'Q' && contour) {
      const start = current;
      const control: Point = [command.x1!, command.y1!];
      const end: Point = [command.x!, command.y!];
      for (let index = 1; index <= CURVE_SEGMENTS; index += 1) {
        const t = index / CURVE_SEGMENTS, u = 1 - t;
        appendPoint(contour, [u * u * start[0] + 2 * u * t * control[0] + t * t * end[0], u * u * start[1] + 2 * u * t * control[1] + t * t * end[1]]);
      }
      current = end;
    } else if (command.type === 'C' && contour) {
      const start = current;
      const c1: Point = [command.x1!, command.y1!];
      const c2: Point = [command.x2!, command.y2!];
      const end: Point = [command.x!, command.y!];
      for (let index = 1; index <= CURVE_SEGMENTS; index += 1) {
        const t = index / CURVE_SEGMENTS, u = 1 - t;
        appendPoint(contour, [u ** 3 * start[0] + 3 * u ** 2 * t * c1[0] + 3 * u * t ** 2 * c2[0] + t ** 3 * end[0], u ** 3 * start[1] + 3 * u ** 2 * t * c1[1] + 3 * u * t ** 2 * c2[1] + t ** 3 * end[1]]);
      }
      current = end;
    } else if (command.type === 'Z' && contour) {
      current = contour[0];
      contour = undefined;
    }
  }
  return contours.filter((candidate) => candidate.length >= 3);
}

/** Converts bundled OpenType glyph paths into deterministic closed template-local millimetre outlines. */
export function captionOutline(text: string, fontId: BundledFontId, size: number): GlyphOutline {
  if (!Number.isFinite(size) || size <= 0) throw new Error('Caption size must be a positive number of millimetres.');
  if (!text) return { contours: [], bounds: { minX: 0, minY: 0, maxX: 0, maxY: 0 } };
  const font = bundledFont(fontId);
  const path = font.getPath(fontId === 'stencil-block' ? text.toUpperCase() : text, 0, 0, size);
  const contours = flattenPath(path.commands as PathCommand[]);
  const points = contours.flat();
  if (!points.length) return { contours: [], bounds: { minX: 0, minY: 0, maxX: 0, maxY: 0 } };
  const minX = Math.min(...points.map(([x]) => x));
  const minY = Math.min(...points.map(([, y]) => y));
  const normalized = contours.map((line) => line.map(([x, y]) => [x - minX, y - minY] as const));
  const normalizedPoints = normalized.flat();
  return { contours: normalized, bounds: { minX: 0, minY: 0, maxX: Math.max(...normalizedPoints.map(([x]) => x)), maxY: Math.max(...normalizedPoints.map(([, y]) => y)) } };
}
