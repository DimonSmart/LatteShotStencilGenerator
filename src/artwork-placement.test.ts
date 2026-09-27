import { describe, expect, it } from 'vitest';
import { containPlacement, defaultArtworkMargins, isContained, placedBounds, workingRectangle } from './artwork-placement';
import { templateBounds } from './template-geometry';

describe('artwork placement', () => {
  const referenceBounds = templateBounds(new Float32Array([0, 0, 0, 87.947, 0, 0, 0, 113.883, 0, 0, 0, 1]));

  it('derives the bundled blank-card top artwork rectangle from margins', () => {
    const rectangle = workingRectangle(referenceBounds, defaultArtworkMargins);
    expect(rectangle.x).toBeCloseTo(6.479, 3);
    expect(rectangle.y).toBeCloseTo(5.864, 3);
    expect(rectangle.width).toBeCloseTo(74.989, 2);
    expect(rectangle.height).toBeCloseTo(74.989, 2);
  });

  it('centers contain placement while preserving aspect ratio', () => {
    const rectangle = { x: 10, y: 20, width: 80, height: 50 };
    const placement = containPlacement({ minX: 0, minY: 0, maxX: 40, maxY: 10 }, rectangle);
    expect(placement).toEqual({ x: 10, y: 35, scale: 2 });
    expect(isContained(placedBounds({ minX: 0, minY: 0, maxX: 40, maxY: 10 }, placement), rectangle)).toBe(true);
  });

  it('rejects artwork moved beyond an asymmetric top-view rectangle', () => {
    const rectangle = { x: 4, y: 11, width: 70, height: 30 };
    expect(isContained({ minX: 4, minY: 11, maxX: 75, maxY: 20 }, rectangle)).toBe(false);
  });
});
