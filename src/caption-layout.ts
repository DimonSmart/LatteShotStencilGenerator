export type CaptionAlignment = 'left' | 'center' | 'right';

const EPSILON = 1e-9;

export function captionHorizontalOrigin(templateWidth: number, captionWidth: number, sideInset: number, alignment: CaptionAlignment): number {
  if (!Number.isFinite(templateWidth) || templateWidth <= 0) throw new Error('Template width must be positive.');
  if (!Number.isFinite(captionWidth) || captionWidth < 0) throw new Error('Caption width must be finite and non-negative.');
  if (!Number.isFinite(sideInset) || sideInset < 0) throw new Error('Caption side inset must be a non-negative number.');

  const usableWidth = templateWidth - sideInset * 2;
  if (usableWidth < -EPSILON) throw new Error('Caption horizontal inset places the caption area outside the template.');
  if (captionWidth > usableWidth + EPSILON) throw new Error('Caption must fit completely inside the horizontal caption area.');

  if (alignment === 'left') return sideInset;
  if (alignment === 'center') return sideInset + (usableWidth - captionWidth) / 2;
  if (alignment === 'right') return templateWidth - sideInset - captionWidth;
  throw new Error('Choose a valid caption alignment.');
}
