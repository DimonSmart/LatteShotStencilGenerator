import { describe, expect, it } from 'vitest';
import ManifoldModule from 'manifold-3d';
import { bridgeCandidates, generateStencil } from './stencil-generation';
import type { PlanarArtwork } from './svg-artwork-adapter';
import type { TemplateGeometry } from './template-geometry';

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
const oneOpening: PlanarArtwork = { contours: [contour([[3, 3], [17, 3], [17, 17], [3, 17]])], bounds: { minX: 3, minY: 3, maxX: 17, maxY: 17 } };
const ring: PlanarArtwork = { contours: [contour([[3, 3], [17, 3], [17, 17], [3, 17]]), contour([[7, 7], [13, 7], [13, 13], [7, 13]], true)], bounds: { minX: 3, minY: 3, maxX: 17, maxY: 17 } };
const twoRings: PlanarArtwork = { contours: [contour([[2, 2], [9, 2], [9, 9], [2, 9]]), contour([[4, 4], [7, 4], [7, 7], [4, 7]], true), contour([[11, 11], [18, 11], [18, 18], [11, 18]]), contour([[13, 13], [16, 13], [16, 16], [13, 16]], true)], bounds: { minX: 2, minY: 2, maxX: 18, maxY: 18 } };

describe('stencil generation', () => {
  it('uses no bridges for a normal opening and returns finite connected solid geometry', async () => {
    const api = await ManifoldModule(); api.setup();
    const result = generateStencil(api, template, oneOpening, placement, rectangle, 0.8);
    expect(result.bridges).toEqual([]);
    expect(result.positions.every(Number.isFinite)).toBe(true);
    expect(result.indices.length).toBeGreaterThan(0);
  });

  it('connects an enclosed retained island with a deterministic minimum-width bridge', async () => {
    const api = await ManifoldModule(); api.setup();
    const first = generateStencil(api, template, ring, placement, rectangle, 0.8);
    const second = generateStencil(api, template, ring, placement, rectangle, 0.8);
    expect(first.bridges).toHaveLength(1);
    expect(first.bridges).toEqual(second.bridges);
    expect(first.bridges[0].width).toBeGreaterThanOrEqual(0.8);
  });

  it('connects multiple retained islands and rejects invalid generation inputs without preserving a result', async () => {
    const api = await ManifoldModule(); api.setup();
    expect(generateStencil(api, template, twoRings, placement, rectangle, 0.8).bridges).toHaveLength(2);
    expect(() => generateStencil(api, template, ring, placement, rectangle, 0.2)).toThrow(/0.8/);
  });

  it('orders bridge positions deterministically and rejects undersized bridges', () => {
    expect(bridgeCandidates([8, 8, 12, 12], rectangle, 1)).toEqual(bridgeCandidates([8, 8, 12, 12], rectangle, 1));
    expect(() => bridgeCandidates([8, 8, 12, 12], rectangle, 0.79)).toThrow(/0.8/);
  });

  it('omits empty captions and embosses valid closed glyph outlines onto the top surface', async () => {
    const api = await ManifoldModule(); api.setup();
    const empty = generateStencil(api, template, oneOpening, placement, rectangle, 0.8, { text: '', font: 'stencil-block', x: 2, y: 2, size: 4, embossHeight: 0.35 });
    const embossed = generateStencil(api, template, oneOpening, placement, rectangle, 0.8, { text: 'A', font: 'stencil-block', x: 2, y: 2, size: 4, embossHeight: 0.35 });
    expect(empty.caption).toBeUndefined();
    expect(embossed.caption?.indices.length).toBeGreaterThan(0);
    expect(Math.max(...embossed.positions.filter((_, index) => index % 3 === 2))).toBeCloseTo(2.35, 4);
    const captionPositions = embossed.caption!.positions;
    const xs = captionPositions.filter((_, index) => index % 3 === 0);
    const ys = captionPositions.filter((_, index) => index % 3 === 1);
    expect(Math.min(...xs)).toBeCloseTo(2, 4);
    expect(Math.max(...ys)).toBeCloseTo(18, 4); // top-view Y=2 maps down from the template's max Y.
  });

  it('reports caption placements that cannot produce printable geometry', async () => {
    const api = await ManifoldModule(); api.setup();
    expect(() => generateStencil(api, template, oneOpening, placement, rectangle, 0.8, { text: 'A', font: 'stencil-block', x: 30, y: 2, size: 4, embossHeight: 0.35 })).toThrow(/outside/i);
    expect(() => generateStencil(api, template, oneOpening, placement, rectangle, 0.8, { text: 'A', font: 'stencil-block', x: 18, y: 2, size: 4, embossHeight: 0.35 })).toThrow(/fit completely/i);
  });
});
