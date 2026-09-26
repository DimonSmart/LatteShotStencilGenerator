import { unzipSync, strFromU8 } from 'fflate';

export type TemplateFormat = 'stl' | '3mf';
export interface ImportedTriangleMesh { readonly positions: Float32Array; readonly indices: Uint32Array; }

export function templateFormatFromName(name: string): TemplateFormat {
  const extension = name.toLowerCase().split('.').pop();
  if (extension === 'stl' || extension === '3mf') return extension;
  throw new Error('Choose an STL or 3MF template.');
}

export function parseTemplateBytes(format: TemplateFormat, bytes: Uint8Array): ImportedTriangleMesh {
  if (format === 'stl') return parseStl(bytes);
  return parse3mf(bytes);
}

function parseStl(bytes: Uint8Array): ImportedTriangleMesh {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  if (bytes.length >= 84 && 84 + view.getUint32(80, true) * 50 === bytes.length) {
    const count = view.getUint32(80, true);
    const positions = new Float32Array(count * 9);
    for (let triangle = 0; triangle < count; triangle += 1) for (let vertex = 0; vertex < 3; vertex += 1) for (let axis = 0; axis < 3; axis += 1) {
      positions[triangle * 9 + vertex * 3 + axis] = view.getFloat32(84 + triangle * 50 + 12 + vertex * 12 + axis * 4, true);
    }
    return indexed(positions);
  }
  const values = Array.from(new TextDecoder().decode(bytes).matchAll(/vertex\s+([^\s]+)\s+([^\s]+)\s+([^\s]+)/gi), (match) => match.slice(1).map(Number));
  if (!values.length || values.length % 3 !== 0) throw new Error('STL does not contain complete triangle vertices.');
  return indexed(new Float32Array(values.flat()));
}

function parse3mf(bytes: Uint8Array): ImportedTriangleMesh {
  let files: Record<string, Uint8Array>;
  try { files = unzipSync(bytes); } catch { throw new Error('3MF package is not a readable ZIP archive.'); }
  const model = Object.entries(files).find(([name]) => /3d\/3dmodel\.model$/i.test(name))?.[1];
  if (!model) throw new Error('3MF package does not contain 3D/3dmodel.model.');
  const xml = strFromU8(model);
  const unit = /<model\b[^>]*\bunit="([^"]+)"/i.exec(xml)?.[1]?.toLowerCase() ?? 'millimeter';
  const scale: Record<string, number> = { micron: 0.001, millimeter: 1, centimeter: 10, inch: 25.4, foot: 304.8, meter: 1000 };
  if (!(unit in scale)) throw new Error(`3MF declares unsupported unit “${unit}”.`);
  const positions = Array.from(xml.matchAll(/<vertex\b[^>]*\bx="([^"]+)"[^>]*\by="([^"]+)"[^>]*\bz="([^"]+)"[^>]*\/?\s*>/gi), (match) => match.slice(1).map(Number));
  const triangles = Array.from(xml.matchAll(/<triangle\b[^>]*\bv1="(\d+)"[^>]*\bv2="(\d+)"[^>]*\bv3="(\d+)"[^>]*\/?\s*>/gi), (match) => match.slice(1).map(Number));
  if (!positions.length || !triangles.length) throw new Error('3MF does not contain mesh vertices and triangles.');
  return { positions: new Float32Array(positions.flat().map((value) => value * scale[unit])), indices: new Uint32Array(triangles.flat()) };
}

function indexed(trianglePositions: Float32Array): ImportedTriangleMesh {
  const vertices: number[] = [];
  const indices: number[] = [];
  const known = new Map<string, number>();
  for (let offset = 0; offset < trianglePositions.length; offset += 3) {
    const point = [trianglePositions[offset], trianglePositions[offset + 1], trianglePositions[offset + 2]];
    const key = point.join(',');
    let index = known.get(key);
    if (index === undefined) { index = vertices.length / 3; known.set(key, index); vertices.push(...point); }
    indices.push(index);
  }
  return { positions: new Float32Array(vertices), indices: new Uint32Array(indices) };
}
