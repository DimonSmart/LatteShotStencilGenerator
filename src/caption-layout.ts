export type CaptionHorizontalAlignment = 'left' | 'center' | 'right';
export type CaptionVerticalAlignment = 'top' | 'center' | 'bottom';
export interface CaptionRectangle { readonly left: number; readonly top: number; readonly right: number; readonly bottom: number; }

const EPSILON = 1e-9;

export function captionOrigin(rectangle: CaptionRectangle, captionWidth: number, captionHeight: number, horizontalAlignment: CaptionHorizontalAlignment, verticalAlignment: CaptionVerticalAlignment): readonly [number, number] {
  if (![rectangle.left, rectangle.top, rectangle.right, rectangle.bottom, captionWidth, captionHeight].every(Number.isFinite)) throw new Error('Caption rectangle and text bounds must be finite.');
  const width = rectangle.right - rectangle.left;
  const height = rectangle.bottom - rectangle.top;
  if (width <= 0 || height <= 0) throw new Error('Caption rectangle must have positive width and height.');
  if (captionWidth < 0 || captionHeight < 0 || captionWidth > width + EPSILON || captionHeight > height + EPSILON) throw new Error('Caption must fit completely inside the caption rectangle.');

  const x = horizontalAlignment === 'left' ? rectangle.left
    : horizontalAlignment === 'center' ? rectangle.left + (width - captionWidth) / 2
      : horizontalAlignment === 'right' ? rectangle.right - captionWidth
        : undefined;
  const y = verticalAlignment === 'top' ? rectangle.top
    : verticalAlignment === 'center' ? rectangle.top + (height - captionHeight) / 2
      : verticalAlignment === 'bottom' ? rectangle.bottom - captionHeight
        : undefined;
  if (x === undefined || y === undefined) throw new Error('Choose valid caption alignment.');
  return [x, y];
}
