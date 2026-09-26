import { describe, expect, it } from 'vitest';
import { strToU8, zipSync } from 'fflate';
import ManifoldModule from 'manifold-3d';
import { parseTemplateBytes } from './template-format-adapter';
import { templateBounds, toTemplateTopView } from './template-geometry';

describe('template geometry contracts', () => {
  it('initializes the Manifold mesh constructor before template normalization', async () => {
    const api = await ManifoldModule();
    api.setup();
    expect(api.Mesh).toEqual(expect.any(Function));
  });

  it('keeps the reference card bounds in millimetres', () => {
    const bounds = templateBounds(new Float32Array([0, 0, 0, 88.345, 0, 0, 0, 113.882, 0, 0, 0, 1.198]));
    expect(bounds.max[0] - bounds.min[0]).toBeCloseTo(88.345, 3);
    expect(bounds.max[1] - bounds.min[1]).toBeCloseTo(113.882, 3);
  });

  it('uses the upper-left top-view mapping without mirroring asymmetric geometry', () => {
    const bounds = templateBounds(new Float32Array([10, 20, 0, 40, 20, 0, 10, 80, 0, 10, 20, 5]));
    expect(toTemplateTopView(bounds, 13, 75)).toEqual([3, 5]);
    expect(toTemplateTopView(bounds, 37, 25)).toEqual([27, 55]);
  });

  it('rejects empty or non-finite template geometry before solid creation', () => {
    expect(() => templateBounds(new Float32Array())).toThrow(/finite triangle vertices/i);
    expect(() => templateBounds(new Float32Array([0, 0, 0, Infinity, 0, 0, 0, 1, 0]))).toThrow(/finite triangle vertices/i);
  });

  it('converts binary STL triangles into indexed millimetre geometry', () => {
    const bytes = new Uint8Array(134);
    const view = new DataView(bytes.buffer);
    view.setUint32(80, 1, true);
    [0, 0, 0, 10, 0, 0, 0, 10, 0].forEach((value, index) => view.setFloat32(96 + index * 4, value, true));
    const mesh = parseTemplateBytes('stl', bytes);
    expect(mesh.positions).toEqual(new Float32Array([0, 0, 0, 10, 0, 0, 0, 10, 0]));
    expect(mesh.indices).toEqual(new Uint32Array([0, 1, 2]));
  });

  it('converts declared 3MF units to millimetres', () => {
    const model = '<model unit="inch"><resources><object><mesh><vertices><vertex x="1" y="0" z="0"/><vertex x="0" y="1" z="0"/><vertex x="0" y="0" z="1"/></vertices><triangles><triangle v1="0" v2="1" v3="2"/></triangles></mesh></object></resources></model>';
    const mesh = parseTemplateBytes('3mf', zipSync({ '3D/3dmodel.model': strToU8(model) }));
    expect(mesh.positions[0]).toBeCloseTo(25.4, 5);
  });
});
