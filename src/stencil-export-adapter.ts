import { strToU8, zipSync } from 'fflate';
import type { GeneratedStencil } from './stencil-generation';

export type StencilExportFormat = 'stl' | '3mf';

export interface StencilExportModel {
  readonly stencil: GeneratedStencil;
  readonly baseColor: string;
  readonly captionColor: string;
}

interface Triangle { readonly a: number; readonly b: number; readonly c: number; }

/** Serializes the project-owned printable mesh; render scene objects never enter this boundary. */
export function serializeStencil(model: StencilExportModel, format: StencilExportFormat): Uint8Array {
  const triangles = validatePrintableMesh(model.stencil.positions, model.stencil.indices);
  const baseColor = color(model.baseColor, 'base');
  const captionColor = color(model.captionColor, 'caption');
  return format === 'stl'
    ? binaryStl(model.stencil.positions, triangles)
    : threeMf(model.stencil, triangles, baseColor, captionColor);
}

function validatePrintableMesh(positions: Float32Array, indices: Uint32Array): Triangle[] {
  if (positions.length === 0 || positions.length % 3 !== 0 || indices.length === 0 || indices.length % 3 !== 0) throw new Error('Export failed: the generated stencil has no complete triangle geometry.');
  if (!Array.from(positions).every(Number.isFinite)) throw new Error('Export failed: the generated stencil contains non-finite coordinates.');
  const vertexCount = positions.length / 3;
  const edgeCounts = new Map<string, number>();
  const triangles: Triangle[] = [];
  let signedVolume = 0;
  for (let offset = 0; offset < indices.length; offset += 3) {
    const [a, b, c] = [indices[offset], indices[offset + 1], indices[offset + 2]];
    if (a >= vertexCount || b >= vertexCount || c >= vertexCount || a === b || b === c || c === a) throw new Error('Export failed: the generated stencil has invalid triangle indices.');
    const ax = positions[a * 3]; const ay = positions[a * 3 + 1]; const az = positions[a * 3 + 2];
    const bx = positions[b * 3]; const by = positions[b * 3 + 1]; const bz = positions[b * 3 + 2];
    const cx = positions[c * 3]; const cy = positions[c * 3 + 1]; const cz = positions[c * 3 + 2];
    const crossX = by * cz - bz * cy; const crossY = bz * cx - bx * cz; const crossZ = bx * cy - by * cx;
    const ux = bx - ax; const uy = by - ay; const uz = bz - az;
    const vx = cx - ax; const vy = cy - ay; const vz = cz - az;
    const areaSquared = (uy * vz - uz * vy) ** 2 + (uz * vx - ux * vz) ** 2 + (ux * vy - uy * vx) ** 2;
    if (!Number.isFinite(areaSquared) || areaSquared <= Number.EPSILON) throw new Error('Export failed: the generated stencil contains degenerate triangles.');
    signedVolume += ax * crossX + ay * crossY + az * crossZ;
    for (const [first, second] of [[a, b], [b, c], [c, a]] as const) {
      const key = first < second ? `${first}:${second}` : `${second}:${first}`;
      edgeCounts.set(key, (edgeCounts.get(key) ?? 0) + 1);
    }
    triangles.push({ a, b, c });
  }
  if (!Number.isFinite(signedVolume) || Math.abs(signedVolume) <= Number.EPSILON) throw new Error('Export failed: the generated stencil has no printable volume.');
  if (Array.from(edgeCounts.values()).some((count) => count !== 2)) throw new Error('Export failed: the generated stencil is not a watertight manifold mesh.');
  return triangles;
}

function binaryStl(positions: Float32Array, triangles: readonly Triangle[]): Uint8Array {
  const bytes = new Uint8Array(84 + triangles.length * 50);
  const view = new DataView(bytes.buffer);
  new TextEncoder().encodeInto('Latte Shot Stencil Generator binary STL', bytes.subarray(0, 80));
  view.setUint32(80, triangles.length, true);
  triangles.forEach((triangle, index) => {
    const offset = 84 + index * 50;
    const normal = triangleNormal(positions, triangle);
    normal.forEach((value, axis) => view.setFloat32(offset + axis * 4, value, true));
    [triangle.a, triangle.b, triangle.c].forEach((vertex, vertexOffset) => {
      for (let axis = 0; axis < 3; axis += 1) view.setFloat32(offset + 12 + vertexOffset * 12 + axis * 4, positions[vertex * 3 + axis], true);
    });
  });
  return bytes;
}

function triangleNormal(positions: Float32Array, triangle: Triangle): readonly [number, number, number] {
  const ax = positions[triangle.a * 3]; const ay = positions[triangle.a * 3 + 1]; const az = positions[triangle.a * 3 + 2];
  const ux = positions[triangle.b * 3] - ax; const uy = positions[triangle.b * 3 + 1] - ay; const uz = positions[triangle.b * 3 + 2] - az;
  const vx = positions[triangle.c * 3] - ax; const vy = positions[triangle.c * 3 + 1] - ay; const vz = positions[triangle.c * 3 + 2] - az;
  const x = uy * vz - uz * vy; const y = uz * vx - ux * vz; const z = ux * vy - uy * vx; const length = Math.hypot(x, y, z);
  return [x / length, y / length, z / length];
}

function threeMf(stencil: GeneratedStencil, triangles: readonly Triangle[], baseColor: string, captionColor: string): Uint8Array {
  const hasCaption = stencil.caption !== undefined;
  const captionFloor = hasCaption ? minimumZ(stencil.caption!.positions) : Infinity;
  const vertices = Array.from({ length: stencil.positions.length / 3 }, (_, index) => `<vertex x="${stencil.positions[index * 3]}" y="${stencil.positions[index * 3 + 1]}" z="${stencil.positions[index * 3 + 2]}"/>`).join('');
  const triangleXml = triangles.map(({ a, b, c }) => {
    const z = (stencil.positions[a * 3 + 2] + stencil.positions[b * 3 + 2] + stencil.positions[c * 3 + 2]) / 3;
    return hasCaption && z > captionFloor + 1e-5 ? `<triangle v1="${a}" v2="${b}" v3="${c}" pid="1" p1="1"/>` : `<triangle v1="${a}" v2="${b}" v3="${c}"/>`;
  }).join('');
  const materials = hasCaption ? `<base name="Base" displaycolor="${baseColor}"/><base name="Caption" displaycolor="${captionColor}"/>` : `<base name="Base" displaycolor="${baseColor}"/>`;
  const model = `<?xml version="1.0" encoding="UTF-8"?><model unit="millimeter" xml:lang="en-US" xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02"><resources><basematerials id="1">${materials}</basematerials><object id="2" type="model" pid="1" pindex="0"><mesh><vertices>${vertices}</vertices><triangles>${triangleXml}</triangles></mesh></object></resources><build><item objectid="2"/></build></model>`;
  return zipSync({
    '[Content_Types].xml': strToU8('<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="model" ContentType="application/vnd.ms-package.3dmanufacturing-3dmodel+xml"/></Types>'),
    '_rels/.rels': strToU8('<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Target="/3D/3dmodel.model" Id="rel0" Type="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel"/></Relationships>'),
    '3D/3dmodel.model': strToU8(model),
  });
}

function minimumZ(positions: Float32Array): number {
  let minimum = Infinity;
  for (let index = 2; index < positions.length; index += 3) minimum = Math.min(minimum, positions[index]);
  return minimum;
}

function color(value: string, region: string): string {
  if (!/^#[0-9a-f]{6}$/i.test(value)) throw new Error(`Export failed: the ${region} color must be a six-digit hexadecimal color.`);
  return `${value.toUpperCase()}FF`;
}
