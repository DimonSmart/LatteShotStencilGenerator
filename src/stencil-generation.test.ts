import topologyContoursSvg from '../tests/LatteShotStencilGenerator.Tests/Fixtures/topology-contours.svg?raw';
import { describe, expect, it } from 'vitest';
import ManifoldModule from 'manifold-3d';
import { containPlacement } from './artwork-placement';
import { generateStencil, MIN_BRIDGE_CENTER_SPACING_FACTOR, placedArtworkPolygons, type GeneratedStencil } from './stencil-generation';
import { parseSvgArtwork, type PlanarArtwork } from './svg-artwork-adapter';
import type { TemplateGeometry } from './template-geometry';
import { installWorkerDomParser } from './worker-dom-parser';

type ManifoldApi = Awaited<ReturnType<typeof ManifoldModule>>;
type Solid = InstanceType<ManifoldApi['Manifold']>;

const template: TemplateGeometry = {
  positions: new Float32Array([
    0, 0, 0, 20, 0, 0, 20, 20, 0, 0, 20, 0, 0, 0, 2, 20, 0, 2, 20, 20, 2, 0, 20, 2,
  ]),
  indices: new Uint32Array([0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7]),
  bounds: { min: [0, 0, 0], max: [20, 20, 2] }, topView: { origin: [0, 20], xRight: true, yDown: true },
};
const rectangle = { x: 0, y: 0, width: 20, height: 20 };
const placement = { x: 0, y: 0, scale: 1 };
const contour = (points: Array<readonly [number, number]>, hole = false) => ({ points, hole, fillRule: 'evenodd' as const });
const rectangleContour = (left: number, top: number, right: number, bottom: number, hole = false) => contour([[left, top], [right, top], [right, bottom], [left, bottom]], hole);
const oneOpening: PlanarArtwork = { contours: [rectangleContour(3, 3, 17, 17)], bounds: { minX: 3, minY: 3, maxX: 17, maxY: 17 } };
const ring: PlanarArtwork = { contours: [rectangleContour(3, 3, 17, 17), rectangleContour(7, 7, 13, 13, true)], bounds: { minX: 3, minY: 3, maxX: 17, maxY: 17 } };
const smallRing: PlanarArtwork = { contours: [rectangleContour(3, 3, 17, 17), rectangleContour(9.5, 9.5, 10.5, 10.5, true)], bounds: { minX: 3, minY: 3, maxX: 17, maxY: 17 } };
const twoRings: PlanarArtwork = { contours: [rectangleContour(2, 2, 9, 9), rectangleContour(4, 4, 7, 7, true), rectangleContour(11, 11, 18, 18), rectangleContour(13, 13, 16, 16, true)], bounds: { minX: 2, minY: 2, maxX: 18, maxY: 18 } };
const nestedHole: PlanarArtwork = { contours: [rectangleContour(3, 3, 17, 17), rectangleContour(7, 7, 13, 13, true), rectangleContour(9, 9, 11, 11)], bounds: { minX: 3, minY: 3, maxX: 17, maxY: 17 } };
const concaveIsland: PlanarArtwork = {
  contours: [
    rectangleContour(2, 2, 18, 18),
    contour([[6, 5], [14, 5], [14, 7], [8, 7], [8, 13], [14, 13], [14, 15], [6, 15]], true),
  ],
  bounds: { minX: 2, minY: 2, maxX: 18, maxY: 18 },
};
const multipleOpenings: PlanarArtwork = {
  contours: [rectangleContour(2, 2, 18, 18), rectangleContour(8, 4, 9, 16, true), rectangleContour(10, 4, 12, 16, true)],
  bounds: { minX: 2, minY: 2, maxX: 18, maxY: 18 },
};

function resultSolid(api: ManifoldApi, generated: GeneratedStencil): Solid {
  const mesh = new api.Mesh({ numProp: 3, vertProperties: generated.positions, triVerts: generated.indices });
  mesh.merge();
  return new api.Manifold(mesh);
}

function expectConnected(api: ManifoldApi, generated: GeneratedStencil): void {
  const solid = resultSolid(api, generated);
  try {
    const pieces = solid.decompose();
    try { expect(pieces).toHaveLength(1); }
    finally { for (const piece of pieces) piece.delete(); }
  } finally { solid.delete(); }
}

function expectArtworkRegionOpen(api: ManifoldApi, generated: GeneratedStencil, artworkPlacement: { x: number; y: number; scale: number }, point: readonly [number, number], halfSizeInArtworkUnits: number): void {
  const worldX = template.bounds.min[0] + point[0] * artworkPlacement.scale + artworkPlacement.x;
  const worldY = template.bounds.max[1] - (point[1] * artworkPlacement.scale + artworkPlacement.y);
  const half = halfSizeInArtworkUnits * artworkPlacement.scale;
  const section = new api.CrossSection([[[worldX - half, worldY - half], [worldX + half, worldY - half], [worldX + half, worldY + half], [worldX - half, worldY + half]]], 'NonZero');
  const rawProbe = section.extrude(template.bounds.max[2] - template.bounds.min[2] + 2);
  const probe = rawProbe.translate([0, 0, template.bounds.min[2] - 1]);
  const solid = resultSolid(api, generated);
  let intersection: Solid | undefined;
  try {
    intersection = solid.intersect(probe);
    expect(intersection.isEmpty()).toBe(true);
  } finally {
    intersection?.delete();
    solid.delete();
    probe.delete();
    rawProbe.delete();
    section.delete();
  }
}

describe('stencil generation', () => {
  it('uses no bridges for a normal opening and returns finite connected solid geometry', async () => {
    const api = await ManifoldModule(); api.setup();
    const result = generateStencil(api, template, oneOpening, placement, rectangle, { width: 0.8, count: 1 });
    expect(result.bridges).toEqual([]);
    expect(result.positions.every(Number.isFinite)).toBe(true);
    expect(result.indices.length).toBeGreaterThan(0);
    expectConnected(api, result);
  });

  it('connects an enclosed retained island with a deterministic minimum-width bridge', async () => {
    const api = await ManifoldModule(); api.setup();
    const first = generateStencil(api, template, ring, placement, rectangle, { width: 0.8, count: 1 });
    const second = generateStencil(api, template, ring, placement, rectangle, { width: 0.8, count: 1 });
    expect(first.bridges).toHaveLength(1);
    expect(first.bridges).toEqual(second.bridges);
    expect(first.bridges[0].width).toBe(0.8);
    expectConnected(api, first);
  });

  it('connects multiple retained islands and rejects invalid generation inputs without preserving a result', async () => {
    const api = await ManifoldModule(); api.setup();
    expect(generateStencil(api, template, twoRings, placement, rectangle, { width: 0.8, count: 1 }).bridges).toHaveLength(2);
    expect(() => generateStencil(api, template, ring, placement, rectangle, { width: 0.2, count: 1 })).toThrow(/0.8/);
  });

  it('stops at the first retained-island contact and leaves a nested opening unchanged', async () => {
    const api = await ManifoldModule(); api.setup();
    const result = generateStencil(api, template, nestedHole, placement, rectangle, { width: 0.8, count: 1 });
    expect(result.bridges).toHaveLength(1);
    expect(result.bridges[0].direction).toBe('left');
    expect(result.bridges[0].x).toBeCloseTo(7, 6);
    expect(result.bridges[0].width).toBe(0.8);
    expectConnected(api, result);
    expectArtworkRegionOpen(api, result, placement, [10, 10], 0.2);
  });

  it('uses actual concave target geometry when the bounding-box centre is not retained material', async () => {
    const api = await ManifoldModule(); api.setup();
    const target = new api.CrossSection([placedArtworkPolygons({ contours: [concaveIsland.contours[1]], bounds: concaveIsland.bounds }, placement, template)[0]], 'EvenOdd');
    const centreProbe = new api.CrossSection([[[9.9, 9.9], [10.1, 9.9], [10.1, 10.1], [9.9, 10.1]]], 'NonZero');
    const centreIntersection = target.intersect(centreProbe);
    try { expect(centreIntersection.isEmpty()).toBe(true); }
    finally { centreIntersection.delete(); centreProbe.delete(); target.delete(); }

    const first = generateStencil(api, template, concaveIsland, placement, rectangle, { width: 0.8, count: 1 });
    const second = generateStencil(api, template, concaveIsland, placement, rectangle, { width: 0.8, count: 1 });
    expect(first.bridges.length).toBeGreaterThan(0);
    expect(first.bridges).toEqual(second.bridges);
    expectConnected(api, first);
  });

  it('restores only the opening immediately before a target when one probe crosses multiple openings', async () => {
    const api = await ManifoldModule(); api.setup();
    const result = generateStencil(api, template, multipleOpenings, placement, rectangle, { width: 0.8, count: 1 });
    expect(result.bridges).toHaveLength(2);
    expect(result.bridges[0].direction).toBe('left');
    expect(result.bridges[0].x).toBeCloseTo(10, 6);
    expect(result.bridges[1]).not.toEqual(result.bridges[0]);
    expectConnected(api, result);
  });

  it('preserves the nested hole in the topology-contours SVG through the real import and placement pipeline', async () => {
    const api = await ManifoldModule(); api.setup();
    installWorkerDomParser();
    const artwork = parseSvgArtwork(topologyContoursSvg);
    const fittedPlacement = containPlacement(artwork.bounds, rectangle);
    const result = generateStencil(api, template, artwork, fittedPlacement, rectangle, { width: 0.8, count: 1 });

    expect(result.bridges.length).toBeGreaterThan(0);
    expectConnected(api, result);
    // M35..45 is nested inside M25..55; the fixture transforms its centre (40,40) to (40,47).
    expectArtworkRegionOpen(api, result, fittedPlacement, [40, 47], 0.5);
  });

  it.each([1, 2, 3, 4, 5, 6, 7, 8])('creates %i spatially distributed supports for a sufficiently large island', async (count) => {
    const api = await ManifoldModule(); api.setup();
    const first = generateStencil(api, template, ring, placement, rectangle, { width: 0.8, count });
    const second = generateStencil(api, template, ring, placement, rectangle, { width: 0.8, count });
    expect(first.bridges).toHaveLength(count);
    expect(first.bridges).toEqual(second.bridges);
    expect(first.bridgeShortfallIslandCount).toBe(0);
    for (let left = 0; left < first.bridges.length; left += 1) {
      for (let right = left + 1; right < first.bridges.length; right += 1) {
        const distance = Math.hypot(first.bridges[left].x - first.bridges[right].x, first.bridges[left].y - first.bridges[right].y);
        expect(distance).toBeGreaterThanOrEqual(0.8 * MIN_BRIDGE_CENTER_SPACING_FACTOR - 1e-8);
      }
    }
    expectConnected(api, first);
  });

  it('uses fewer supports without failing when an island cannot fit the requested count', async () => {
    const api = await ManifoldModule(); api.setup();
    const result = generateStencil(api, template, smallRing, placement, rectangle, { width: 0.8, count: 8 });
    expect(result.bridges.length).toBeGreaterThanOrEqual(1);
    expect(result.bridges.length).toBeLessThan(8);
    expect(result.bridgeShortfallIslandCount).toBe(1);
    expectConnected(api, result);
  });

  it('preserves a nested opening when several supports connect the surrounding retained island', async () => {
    const api = await ManifoldModule(); api.setup();
    const result = generateStencil(api, template, nestedHole, placement, rectangle, { width: 0.8, count: 4 });
    expect(result.bridges).toHaveLength(4);
    expectConnected(api, result);
    expectArtworkRegionOpen(api, result, placement, [10, 10], 0.2);
  });

  it('uses bridgeCount per island rather than as a stencil-wide limit', async () => {
    const api = await ManifoldModule(); api.setup();
    const result = generateStencil(api, template, twoRings, placement, rectangle, { width: 0.8, count: 2 });
    expect(result.bridges).toHaveLength(4);
    expect(result.bridgeShortfallIslandCount).toBe(0);
    expectConnected(api, result);
  });

  it('keeps multi-support placement deterministic for concave retained geometry', async () => {
    const api = await ManifoldModule(); api.setup();
    const first = generateStencil(api, template, concaveIsland, placement, rectangle, { width: 0.8, count: 4 });
    const second = generateStencil(api, template, concaveIsland, placement, rectangle, { width: 0.8, count: 4 });
    expect(first.bridges.length).toBeGreaterThan(1);
    expect(first.bridges.length).toBeLessThanOrEqual(4);
    expect(first.bridges).toEqual(second.bridges);
    expectConnected(api, first);
  });

  it.each([0, 9, -1, 1.5, Number.NaN, Number.POSITIVE_INFINITY])('rejects invalid bridgeCount %s', async (count) => {
    const api = await ManifoldModule(); api.setup();
    expect(() => generateStencil(api, template, ring, placement, rectangle, { width: 0.8, count })).toThrow(/integer between 1 and 8/i);
  });

  it('omits empty captions and embosses valid closed glyph outlines onto the top surface', async () => {
    const api = await ManifoldModule(); api.setup();
    const empty = generateStencil(api, template, oneOpening, placement, rectangle, { width: 0.8, count: 1 }, { text: '', font: 'stencil-block', x: 2, y: 2, size: 4, embossHeight: 0.35 });
    const embossed = generateStencil(api, template, oneOpening, placement, rectangle, { width: 0.8, count: 1 }, { text: 'A', font: 'stencil-block', x: 2, y: 2, size: 4, embossHeight: 0.35 });
    expect(empty.caption).toBeUndefined();
    expect(embossed.caption?.indices.length).toBeGreaterThan(0);
    expect(Math.max(...embossed.positions.filter((_, index) => index % 3 === 2))).toBeCloseTo(2.35, 4);
    const captionPositions = embossed.caption!.positions;
    const xs = captionPositions.filter((_, index) => index % 3 === 0);
    const ys = captionPositions.filter((_, index) => index % 3 === 1);
    expect(Math.min(...xs)).toBeCloseTo(2, 4);
    expect(Math.max(...ys)).toBeCloseTo(18, 4);
  });

  it('reports caption placements that cannot produce printable geometry', async () => {
    const api = await ManifoldModule(); api.setup();
    expect(() => generateStencil(api, template, oneOpening, placement, rectangle, { width: 0.8, count: 1 }, { text: 'A', font: 'stencil-block', x: 30, y: 2, size: 4, embossHeight: 0.35 })).toThrow(/outside/i);
    expect(() => generateStencil(api, template, oneOpening, placement, rectangle, { width: 0.8, count: 1 }, { text: 'A', font: 'stencil-block', x: 18, y: 2, size: 4, embossHeight: 0.35 })).toThrow(/fit completely/i);
  });
});
