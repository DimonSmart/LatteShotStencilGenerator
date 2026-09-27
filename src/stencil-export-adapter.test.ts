import { describe, expect, it } from 'vitest';
import { strFromU8, unzipSync } from 'fflate';
import { serializeStencil } from './stencil-export-adapter';
import type { GeneratedStencil } from './stencil-generation';

const cube = new Float32Array([
  0, 0, 0, 2, 0, 0, 2, 2, 0, 0, 2, 0, 0, 0, 2, 2, 0, 2, 2, 2, 2, 0, 2, 2,
]);
const cubeTriangles = new Uint32Array([0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7]);
const stencil = (caption?: GeneratedStencil['caption']): GeneratedStencil => ({ positions: cube, indices: cubeTriangles, bridges: [], caption });

describe('stencil export adapter', () => {
  it('writes a binary STL containing every printable triangle in millimetre coordinates', () => {
    const bytes = serializeStencil({ stencil: stencil(), baseColor: '#f4ede4', captionColor: '#6a3a22' }, 'stl');
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    expect(view.getUint32(80, true)).toBe(cubeTriangles.length / 3);
    expect(bytes.byteLength).toBe(84 + cubeTriangles.length / 3 * 50);
    expect(view.getFloat32(96, true)).toBe(0); // first triangle's first vertex x
  });

  it('writes a millimetre 3MF package with the base material when there is no caption', () => {
    const files = unzipSync(serializeStencil({ stencil: stencil(), baseColor: '#f4ede4', captionColor: '#6a3a22' }, '3mf'));
    const model = strFromU8(files['3D/3dmodel.model']);
    expect(strFromU8(files['[Content_Types].xml'])).toContain('3dmanufacturing-3dmodel+xml');
    expect(model).toContain('unit="millimeter"');
    expect(model).toContain('displaycolor="#F4EDE4FF"');
    expect(model).not.toContain('name="Caption"');
  });

  it('exports base and caption as distinct material-assigned 3MF parts', () => {
    const captionPositions = new Float32Array([
      0.5, 0.5, 2, 1.5, 0.5, 2, 1.5, 1.5, 2, 0.5, 1.5, 2,
      0.5, 0.5, 2.35, 1.5, 0.5, 2.35, 1.5, 1.5, 2.35, 0.5, 1.5, 2.35,
    ]);
    const captionIndices = new Uint32Array([0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7]);
    const raisedStencil: GeneratedStencil = {
      positions: cube,
      indices: cubeTriangles,
      bridges: [],
      base: { positions: cube, indices: cubeTriangles },
      caption: { positions: captionPositions, indices: captionIndices },
    };
    const model = strFromU8(unzipSync(serializeStencil({ stencil: raisedStencil, baseColor: '#f4ede4', captionColor: '#6a3a22' }, '3mf'))['3D/3dmodel.model']);
    expect(model).toContain('name="Base" displaycolor="#F4EDE4FF"');
    expect(model).toContain('name="Caption" displaycolor="#6A3A22FF"');
    expect(model).toContain('<object id="2" type="model" name="Base" pid="1" pindex="0">');
    expect(model).toContain('<object id="3" type="model" name="Caption" pid="1" pindex="1">');
    expect(model).toContain('<object id="4" type="model" name="Latte Shot Stencil"><components><component objectid="2"/><component objectid="3"/></components></object>');
    expect(model).toContain('<build><item objectid="4"/></build>');
    expect(model).not.toContain('p1="1"');
  });

  it('rejects non-watertight geometry before either format is offered', () => {
    expect(() => serializeStencil({ stencil: { positions: cube, indices: new Uint32Array([0, 1, 2]), bridges: [] }, baseColor: '#f4ede4', captionColor: '#6a3a22' }, 'stl')).toThrow(/watertight|volume/i);
  });
});
