import { SVGLoader } from 'three/addons/loaders/SVGLoader.js';
import type { ArtworkBounds } from './artwork-placement';

export const svgArtworkLimits = { maxBytes: 2 * 1024 * 1024, maxElements: 500, maxFlattenedPoints: 100_000 } as const;
export interface PlanarContour { readonly points: ReadonlyArray<readonly [number, number]>; readonly hole: boolean; readonly fillRule: 'nonzero' | 'evenodd'; }
export interface PlanarArtwork { readonly contours: ReadonlyArray<PlanarContour>; readonly bounds: ArtworkBounds; }

const supported = new Set(['svg', 'g', 'path', 'rect', 'circle', 'ellipse', 'polygon', 'polyline', 'defs', 'title', 'desc']);
const unsafe = new Set(['script', 'image', 'foreignobject', 'iframe', 'object', 'embed', 'animate', 'animatetransform', 'set', 'style', 'use']);

/** Browser SVG is reduced to filled, flattened contours before it reaches stencil geometry. */
export function parseSvgArtwork(source: string): PlanarArtwork {
  validateSvgSource(source);
  const document = new DOMParser().parseFromString(source, 'image/svg+xml');
  if (document.querySelector('parsererror')) throw new Error('SVG is not valid XML.');
  const elements = Array.from(document.querySelectorAll('*'));
  if (elements.length > svgArtworkLimits.maxElements) throw new Error(`SVG exceeds the ${svgArtworkLimits.maxElements} element limit.`);
  let hasStroke = false;
  for (const element of elements) {
    const tag = element.localName.toLowerCase();
    if (unsafe.has(tag)) throw new Error(`SVG <${tag}> content is not allowed because it can be active or network-backed.`);
    if (!supported.has(tag)) continue;
    for (const attribute of Array.from(element.attributes)) {
      const name = attribute.name.toLowerCase(); const value = attribute.value.trim();
      if (name === 'xmlns' || name.startsWith('xmlns:')) continue;
      if (name.startsWith('on') || name === 'href' || name === 'xlink:href' || /(?:https?:|data:|javascript:|url\s*\()/i.test(value)) throw new Error('SVG external resources and active references are not allowed.');
    }
    if ((element.getAttribute('stroke') && element.getAttribute('stroke') !== 'none') || /(?:^|;)\s*stroke\s*:\s*(?!none\b)/i.test(element.getAttribute('style') ?? '')) hasStroke = true;
  }
  const paths = new SVGLoader().parse(source).paths;
  const contours: PlanarContour[] = [];
  for (const path of paths) {
    const style = path.userData.style as { fill?: string; fillRule?: string } | undefined;
    if (!style?.fill || style.fill === 'none') continue;
    const fillRule = style.fillRule === 'evenodd' ? 'evenodd' : 'nonzero';
    for (const shape of SVGLoader.createShapes(path)) {
      appendContour(contours, shape.getPoints(12), false, fillRule);
      for (const hole of shape.holes) appendContour(contours, hole.getPoints(12), true, fillRule);
    }
  }
  if (!contours.length) throw new Error(hasStroke ? 'SVG contains strokes but no filled regions. Convert strokes to filled paths before importing.' : 'SVG contains no supported filled regions.');
  const points = contours.flatMap((contour) => contour.points);
  const xs = points.map((point) => point[0]); const ys = points.map((point) => point[1]);
  return { contours, bounds: { minX: Math.min(...xs), minY: Math.min(...ys), maxX: Math.max(...xs), maxY: Math.max(...ys) } };
}

/** Cheap checks run before XML parsing; DOM checks below remain authoritative for element/attribute safety. */
export function validateSvgSource(source: string): void {
  if (new TextEncoder().encode(source).byteLength > svgArtworkLimits.maxBytes) throw new Error('SVG exceeds the 2 MB import limit.');
  if (/<!DOCTYPE|<!ENTITY|<\?xml-stylesheet/i.test(source)) throw new Error('SVG DTDs, entities, and external stylesheets are not allowed.');
  const tags = source.match(/<\s*(?!\/)[A-Za-z][\w:-]*/g) ?? [];
  if (tags.length > svgArtworkLimits.maxElements) throw new Error(`SVG exceeds the ${svgArtworkLimits.maxElements} element limit.`);
  if (/<\s*\/?(?:script|image|foreignobject|iframe|object|embed|animate|animatetransform|set|style|use)\b/i.test(source)) throw new Error('SVG active or network-backed content is not allowed.');
}

function appendContour(target: PlanarContour[], points: ArrayLike<{ x: number; y: number }>, hole: boolean, fillRule: 'nonzero' | 'evenodd'): void {
  if (points.length < 3) return;
  if (points.length + target.reduce((sum, contour) => sum + contour.points.length, 0) > svgArtworkLimits.maxFlattenedPoints) throw new Error(`SVG exceeds the ${svgArtworkLimits.maxFlattenedPoints} flattened-curve point limit.`);
  target.push({ points: Array.from(points, (point) => [point.x, point.y] as const), hole, fillRule });
}
