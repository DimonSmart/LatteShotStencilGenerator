/** Project-owned, renderer-independent physical template contract (millimetres). */
export interface TemplateBounds {
  readonly min: readonly [number, number, number];
  readonly max: readonly [number, number, number];
}

export interface TemplateGeometry {
  readonly positions: Float32Array;
  readonly indices: Uint32Array;
  readonly bounds: TemplateBounds;
  /** Template UI coordinates: (x, y) = (modelX - minX, maxY - modelY). */
  readonly topView: { readonly origin: readonly [number, number]; readonly xRight: true; readonly yDown: true };
}

export function templateBounds(positions: Float32Array): TemplateBounds {
  if (positions.length < 9 || positions.length % 3 !== 0 || !Array.from(positions).every(Number.isFinite)) {
    throw new Error('Template geometry must contain finite triangle vertices.');
  }
  const min: [number, number, number] = [Infinity, Infinity, Infinity];
  const max: [number, number, number] = [-Infinity, -Infinity, -Infinity];
  for (let index = 0; index < positions.length; index += 3) {
    for (let axis = 0; axis < 3; axis += 1) {
      min[axis] = Math.min(min[axis], positions[index + axis]);
      max[axis] = Math.max(max[axis], positions[index + axis]);
    }
  }
  if (min.some((value, axis) => !(max[axis] > value))) throw new Error('Template must have non-zero width, height, and thickness.');
  return { min, max };
}

export function toTemplateTopView(bounds: TemplateBounds, modelX: number, modelY: number): readonly [number, number] {
  return [modelX - bounds.min[0], bounds.max[1] - modelY];
}
