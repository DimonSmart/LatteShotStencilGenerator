import type { ArtworkPlacement, WorkingRectangle } from './artwork-placement';
import type { PlanarArtwork } from './svg-artwork-adapter';
import type { TemplateGeometry } from './template-geometry';
import { captionOutline, type BundledFontId } from './font-outline-adapter';

export interface Bridge { readonly x: number; readonly y: number; readonly width: number; readonly direction: 'left' | 'right' | 'up' | 'down'; }
export interface GeneratedStencil { readonly positions: Float32Array; readonly indices: Uint32Array; readonly bridges: readonly Bridge[]; readonly caption?: { readonly positions: Float32Array; readonly indices: Uint32Array }; }

type ManifoldApi = Awaited<ReturnType<typeof import('manifold-3d').default>>;
type CrossSection = InstanceType<ManifoldApi['CrossSection']>;
type Solid = InstanceType<ManifoldApi['Manifold']>;

const directions: readonly Bridge['direction'][] = ['left', 'right', 'up', 'down'];

/** Deterministic candidate order: the nearest artwork edge, then left/right/up/down. */
export function bridgeCandidates(bounds: readonly [number, number, number, number], artwork: WorkingRectangle, width: number): Bridge[] {
  if (!Number.isFinite(width) || width < 0.8) throw new Error('Bridge width must be at least 0.8 mm.');
  const [minX, minY, maxX, maxY] = bounds;
  const x = (minX + maxX) / 2; const y = (minY + maxY) / 2;
  const distances: Record<Bridge['direction'], number> = { left: x - artwork.x, right: artwork.x + artwork.width - x, up: y - artwork.y, down: artwork.y + artwork.height - y };
  return [...directions].sort((a, b) => distances[a] - distances[b] || directions.indexOf(a) - directions.indexOf(b)).map((direction) => ({ x, y, width, direction }));
}

export function placedArtworkPolygons(artwork: PlanarArtwork, placement: ArtworkPlacement, template: TemplateGeometry): Array<Array<[number, number]>> {
  return artwork.contours.map((contour) => contour.points.map(([x, y]) => [
    template.bounds.min[0] + x * placement.scale + placement.x,
    template.bounds.max[1] - (y * placement.scale + placement.y),
  ]));
}

function rectangleForBridge(bridge: Bridge, artwork: WorkingRectangle, template: TemplateGeometry): Array<[number, number]> {
  const { min } = template.bounds; const maxY = template.bounds.max[1];
  const left = min[0] + artwork.x; const right = left + artwork.width;
  const top = maxY - artwork.y; const bottom = top - artwork.height;
  const half = bridge.width / 2;
  if (bridge.direction === 'left') return [[left, bridge.y - half], [bridge.x, bridge.y - half], [bridge.x, bridge.y + half], [left, bridge.y + half]];
  if (bridge.direction === 'right') return [[bridge.x, bridge.y - half], [right, bridge.y - half], [right, bridge.y + half], [bridge.x, bridge.y + half]];
  if (bridge.direction === 'up') return [[bridge.x - half, bridge.y], [bridge.x + half, bridge.y], [bridge.x + half, top], [bridge.x - half, top]];
  return [[bridge.x - half, bottom], [bridge.x + half, bottom], [bridge.x + half, bridge.y], [bridge.x - half, bridge.y]];
}

function solidBounds(solid: Solid): readonly [number, number, number, number] {
  const bounds = solid.boundingBox();
  return [bounds.min[0], bounds.min[1], bounds.max[0], bounds.max[1]];
}

function assertPrintable(solid: Solid): void {
  const mesh = solid.getMesh();
  if (solid.isEmpty() || solid.status() !== 'NoError' || !Number.isFinite(solid.volume()) || solid.volume() <= 0 || mesh.triVerts.length < 3 || !Array.from(mesh.vertProperties).every(Number.isFinite)) {
    throw new Error('Stencil boolean did not produce a finite, non-empty manifold solid.');
  }
}

/** Uses Manifold for every planar/solid boolean; bridges are kept by removing strips from the cutter. */
export interface CaptionSettings { readonly text: string; readonly font: BundledFontId; readonly x: number; readonly y: number; readonly size: number; readonly embossHeight: number; }

function captionPolygons(template: TemplateGeometry, caption: CaptionSettings): Array<Array<[number, number]>> {
  if (!caption.text) return [];
  if (!Number.isFinite(caption.embossHeight) || caption.embossHeight <= 0) throw new Error('Caption emboss height must be positive.');
  const outline = captionOutline(caption.text, caption.font, caption.size);
  const topWidth = template.bounds.max[0] - template.bounds.min[0]; const topHeight = template.bounds.max[1] - template.bounds.min[1];
  if (caption.x + outline.bounds.maxX <= 0 || caption.y + outline.bounds.maxY <= 0 || caption.x >= topWidth || caption.y >= topHeight) throw new Error('Caption is completely outside the template.');
  if (caption.x < 0 || caption.y < 0 || caption.x + outline.bounds.maxX > topWidth || caption.y + outline.bounds.maxY > topHeight) throw new Error('Caption must fit completely on the template top surface.');
  return outline.contours.map((contour) => contour.map(([x, y]) => [template.bounds.min[0] + caption.x + x, template.bounds.max[1] - (caption.y + y)]));
}

export function generateStencil(api: ManifoldApi, template: TemplateGeometry, artwork: PlanarArtwork, placement: ArtworkPlacement, rectangle: WorkingRectangle, bridgeWidth: number, caption?: CaptionSettings): GeneratedStencil {
  if (!Number.isFinite(bridgeWidth) || bridgeWidth < 0.8) throw new Error('Minimum bridge width must be at least 0.8 mm.');
  const { Mesh, Manifold, CrossSection } = api;
  const templateMesh = new Mesh({ numProp: 3, vertProperties: template.positions, triVerts: template.indices });
  templateMesh.merge();
  const base = new Manifold(templateMesh);
  const temporary: Array<{ delete(): void }> = [base];
  try {
    assertPrintable(base);
    const contours = placedArtworkPolygons(artwork, placement, template);
    const fillRule = artwork.contours.some((contour) => contour.fillRule === 'evenodd') ? 'EvenOdd' : 'NonZero';
    const opening = new CrossSection(contours, fillRule); temporary.push(opening);
    if (opening.isEmpty()) throw new Error('Artwork does not produce a valid filled opening.');
    const z0 = template.bounds.min[2] - 1; const height = template.bounds.max[2] - template.bounds.min[2] + 2;
    const bridges: Bridge[] = [];
    let cutterSection = opening;
    let cutter = opening.extrude(height).translate([0, 0, z0]); temporary.push(cutter);
    let result = base.subtract(cutter); temporary.push(result);
    for (let attempt = 0; attempt < 32; attempt += 1) {
      assertPrintable(result);
      const pieces = result.decompose(); temporary.push(...pieces);
      if (pieces.length === 1) {
        if (!caption?.text) {
          const mesh = result.getMesh();
          return { positions: new Float32Array(mesh.vertProperties), indices: new Uint32Array(mesh.triVerts), bridges };
        }
        const section = new CrossSection(captionPolygons(template, caption), 'NonZero'); temporary.push(section);
        if (section.isEmpty()) throw new Error('Caption does not produce printable closed glyph outlines.');
        const raised = section.extrude(caption.embossHeight).translate([0, 0, template.bounds.max[2]]); temporary.push(raised);
        const complete = Manifold.union(result, raised); temporary.push(complete); assertPrintable(complete);
        const completeMesh = complete.getMesh(); const captionMesh = raised.getMesh();
        return { positions: new Float32Array(completeMesh.vertProperties), indices: new Uint32Array(completeMesh.triVerts), bridges, caption: { positions: new Float32Array(captionMesh.vertProperties), indices: new Uint32Array(captionMesh.triVerts) } };
      }
      // Keep the largest component as the template body; every other component needs a bridge.
      pieces.sort((a, b) => b.volume() - a.volume());
      let bridged = false;
      for (const detached of pieces.slice(1)) {
        for (const candidate of bridgeCandidates(solidBounds(detached), rectangle, bridgeWidth)) {
          const strip = new CrossSection(rectangleForBridge(candidate, rectangle, template)); temporary.push(strip);
          const insideOpening = strip.intersect(cutterSection); temporary.push(insideOpening);
          if (insideOpening.isEmpty()) continue;
          const nextCutterSection = cutterSection.subtract(insideOpening); temporary.push(nextCutterSection);
          const nextCutter = nextCutterSection.extrude(height).translate([0, 0, z0]); temporary.push(nextCutter);
          const nextResult = base.subtract(nextCutter); temporary.push(nextResult);
          assertPrintable(nextResult);
          if (nextResult.decompose().length < pieces.length) { bridges.push(candidate); cutterSection = nextCutterSection; cutter = nextCutter; result = nextResult; bridged = true; break; }
        }
        if (bridged) break;
      }
      if (!bridged) throw new Error('Unable to create a manufacturable bridge inside the artwork area for a detached island.');
    }
    throw new Error('Bridge generation did not converge on a connected printable stencil.');
  } finally { for (const item of temporary.reverse()) item.delete(); }
}
