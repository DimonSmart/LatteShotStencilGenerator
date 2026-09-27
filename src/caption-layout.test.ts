import { describe, expect, it } from 'vitest';
import { captionOrigin } from './caption-layout';

describe('caption rectangle alignment', () => {
  const rectangle = { left: 6, top: 2, right: 82, bottom: 18 };

  it('aligns text horizontally inside the rectangle', () => {
    expect(captionOrigin(rectangle, 20, 5, 'left', 'top')[0]).toBeCloseTo(6);
    expect(captionOrigin(rectangle, 20, 5, 'center', 'top')[0]).toBeCloseTo(34);
    expect(captionOrigin(rectangle, 20, 5, 'right', 'top')[0]).toBeCloseTo(62);
  });

  it('aligns text vertically inside the rectangle', () => {
    expect(captionOrigin(rectangle, 20, 5, 'left', 'top')[1]).toBeCloseTo(2);
    expect(captionOrigin(rectangle, 20, 5, 'left', 'center')[1]).toBeCloseTo(7.5);
    expect(captionOrigin(rectangle, 20, 5, 'left', 'bottom')[1]).toBeCloseTo(13);
  });

  it('rejects invalid or too-small rectangles', () => {
    expect(() => captionOrigin({ left: 6, top: 2, right: 6, bottom: 18 }, 4, 4, 'center', 'center')).toThrow(/positive/i);
    expect(() => captionOrigin(rectangle, 77, 5, 'center', 'center')).toThrow(/fit completely/i);
  });
});
