import type { TemplateBounds } from './template-geometry';

export interface ArtworkMargins { left: number; right: number; top: number; bottom: number; }
export interface WorkingRectangle { x: number; y: number; width: number; height: number; }
export interface ArtworkBounds { minX: number; minY: number; maxX: number; maxY: number; }
export interface ArtworkPlacement { x: number; y: number; scale: number; }

/** Reference-card margins are defaults; every rectangle is calculated from the imported template. */
export const referenceArtworkMargins: ArtworkMargins = { left: 6.678, right: 6.678, top: 33.029, bottom: 5.864 };

export function workingRectangle(bounds: TemplateBounds, margins: ArtworkMargins): WorkingRectangle {
  const width = bounds.max[0] - bounds.min[0] - margins.left - margins.right;
  const height = bounds.max[1] - bounds.min[1] - margins.top - margins.bottom;
  if (![margins.left, margins.right, margins.top, margins.bottom].every(Number.isFinite) || width <= 0 || height <= 0) {
    throw new Error('Artwork margins leave no usable working rectangle. Reduce the margins.');
  }
  return { x: margins.left, y: margins.top, width, height };
}

export function containPlacement(bounds: ArtworkBounds, rectangle: WorkingRectangle): ArtworkPlacement {
  const width = bounds.maxX - bounds.minX;
  const height = bounds.maxY - bounds.minY;
  if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0) throw new Error('Artwork has no filled area to place.');
  const scale = Math.min(rectangle.width / width, rectangle.height / height);
  return {
    scale,
    x: rectangle.x + (rectangle.width - width * scale) / 2 - bounds.minX * scale,
    y: rectangle.y + (rectangle.height - height * scale) / 2 - bounds.minY * scale,
  };
}

export function placedBounds(bounds: ArtworkBounds, placement: ArtworkPlacement): ArtworkBounds {
  return { minX: bounds.minX * placement.scale + placement.x, minY: bounds.minY * placement.scale + placement.y, maxX: bounds.maxX * placement.scale + placement.x, maxY: bounds.maxY * placement.scale + placement.y };
}

export function isContained(bounds: ArtworkBounds, rectangle: WorkingRectangle): boolean {
  const epsilon = 1e-6;
  return bounds.minX >= rectangle.x - epsilon && bounds.maxX <= rectangle.x + rectangle.width + epsilon && bounds.minY >= rectangle.y - epsilon && bounds.maxY <= rectangle.y + rectangle.height + epsilon;
}
