import { describe, expect, it } from 'vitest';
import { installWorkerDomParser } from './worker-dom-parser';
import { parseSvgArtwork, svgArtworkLimits, validateSvgSource } from './svg-artwork-adapter';

describe('SVG artwork import guardrails', () => {
  it('accepts the supported path vocabulary before worker flattening', () => {
    expect(() => validateSvgSource('<svg viewBox="0 0 100 100"><g transform="translate(2 3)"><path fill-rule="evenodd" d="M0 0 C5 10 20 10 30 0 A10 10 0 0 1 40 20 Z"/><rect fill="black" width="2" height="2"/></g></svg>')).not.toThrow();
  });

  it('accepts standard XML namespace declarations without allowing external references', () => {
    expect(() => validateSvgSource('<svg xmlns="http://www.w3.org/2000/svg" xmlns:inkscape="http://www.inkscape.org/namespaces/inkscape"><path fill="black" d="M0 0H1V1Z"/></svg>')).not.toThrow();
  });

  it('parses filled SVG content with namespaces in a worker-compatible DOM', () => {
    installWorkerDomParser();
    const artwork = parseSvgArtwork('<svg xmlns="http://www.w3.org/2000/svg"><path fill="black" d="M0 0H10V10Z"/></svg>');
    expect(artwork.contours).toHaveLength(1);
  });

  it('rejects active and network-backed SVG constructs', () => {
    expect(() => validateSvgSource('<svg><script>fetch("https://example.test")</script></svg>')).toThrow(/active|network/i);
    expect(() => validateSvgSource('<!DOCTYPE svg><svg/>')).toThrow(/DTD/i);
  });

  it('enforces the source element limit before parsing', () => {
    expect(() => validateSvgSource(`<svg>${'<rect/>'.repeat(svgArtworkLimits.maxElements + 1)}</svg>`)).toThrow(/element limit/i);
  });
});
