import type { ArtworkPlacement, WorkingRectangle } from './artwork-placement';
import type { PlanarArtwork } from './svg-artwork-adapter';
import type { TemplateGeometry } from './template-geometry';
import { captionOutline, type BundledFontId } from './font-outline-adapter';

export interface Bridge { readonly x: number; readonly y: number; readonly width: number; readonly direction: 'left' | 'right' | 'up' | 'down'; }
export interface BridgeSettings { readonly width: number; readonly count: number; }
export interface GeneratedStencil { readonly positions: Float32Array; readonly indices: Uint32Array; readonly bridges: readonly Bridge[]; readonly bridgeShortfallIslandCount?: number; readonly caption?: { readonly positions: Float32Array; readonly indices: Uint32Array }; }

type ManifoldApi = Awaited<ReturnType<typeof import('manifold-3d').default>>;
type CrossSection = InstanceType<ManifoldApi['CrossSection']>;
type Solid = InstanceType<ManifoldApi['Manifold']>;
type SectionPolygons = ReturnType<CrossSection['toPolygons']>;
type Direction = Bridge['direction'];

interface WorldWorkingBounds { readonly left: number; readonly right: number; readonly top: number; readonly bottom: number; }
interface BridgeCandidate { readonly bridge: Bridge; readonly area: number; readonly span: number; readonly transverse: number; readonly patch: SectionPolygons; }
interface ValidatedBridge { readonly cutterSection: CrossSection; readonly cutter: Solid; readonly result: Solid; }

const directions: readonly Direction[] = ['left', 'right', 'up', 'down'];
const directionRank = (direction: Direction): number => directions.indexOf(direction);
export const MIN_BRIDGE_CENTER_SPACING_FACTOR = 1.5;
const MAX_BRIDGE_COUNT = 8;
const EXTRA_TRANSVERSE_SAMPLE_COUNT = MAX_BRIDGE_COUNT * 2 + 1;

function worldWorkingBounds(rectangle: WorkingRectangle, template: TemplateGeometry): WorldWorkingBounds {
  const left = template.bounds.min[0] + rectangle.x;
  const right = left + rectangle.width;
  const top = template.bounds.max[1] - rectangle.y;
  const bottom = top - rectangle.height;
  return { left, right, top, bottom };
}

function geometryTolerance(bounds: WorldWorkingBounds): number {
  return Math.max(1, bounds.right - bounds.left, bounds.top - bounds.bottom) * 1e-8;
}

function orderBridgeDirections(targetBounds: ReturnType<CrossSection['bounds']>, work: WorldWorkingBounds): Direction[] {
  const distances: Record<Direction, number> = {
    left: targetBounds.min[0] - work.left,
    right: work.right - targetBounds.max[0],
    up: work.top - targetBounds.max[1],
    down: targetBounds.min[1] - work.bottom,
  };
  return [...directions].sort((a, b) => distances[a] - distances[b] || directionRank(a) - directionRank(b));
}

function collectTransverseCandidates(targetSection: CrossSection, direction: Direction, work: WorldWorkingBounds, width: number, requestedCount: number): number[] {
  const bounds = targetSection.bounds();
  const horizontal = direction === 'left' || direction === 'right';
  const values = [horizontal ? (bounds.min[1] + bounds.max[1]) / 2 : (bounds.min[0] + bounds.max[0]) / 2];
  for (const polygon of targetSection.toPolygons()) {
    for (let index = 0; index < polygon.length; index += 1) {
      const point = polygon[index];
      const next = polygon[(index + 1) % polygon.length];
      values.push(horizontal ? point[1] : point[0]);
      values.push(horizontal ? (point[1] + next[1]) / 2 : (point[0] + next[0]) / 2);
    }
  }

  const tolerance = geometryTolerance(work);
  const half = width / 2;
  if (requestedCount > 1) {
    const targetMin = horizontal ? bounds.min[1] : bounds.min[0];
    const targetMax = horizontal ? bounds.max[1] : bounds.max[0];
    const sampleMin = targetMin + half;
    const sampleMax = targetMax - half;
    if (sampleMax >= sampleMin - tolerance) {
      if (Math.abs(sampleMax - sampleMin) <= tolerance) values.push((sampleMin + sampleMax) / 2);
      else {
        for (let index = 0; index < EXTRA_TRANSVERSE_SAMPLE_COUNT; index += 1) {
          values.push(sampleMin + (sampleMax - sampleMin) * index / (EXTRA_TRANSVERSE_SAMPLE_COUNT - 1));
        }
      }
    }
  }

  const min = horizontal ? work.bottom : work.left;
  const max = horizontal ? work.top : work.right;
  const sorted = values.filter(Number.isFinite).sort((a, b) => a - b);
  const unique: number[] = [];
  for (const value of sorted) {
    if (value - half < min - tolerance || value + half > max + tolerance) continue;
    if (!unique.length || Math.abs(value - unique[unique.length - 1]) > tolerance) unique.push(value);
  }
  return unique;
}

function createProbe(CrossSectionCtor: ManifoldApi['CrossSection'], direction: Direction, transverse: number, width: number, work: WorldWorkingBounds): CrossSection {
  const half = width / 2;
  const polygon: Array<[number, number]> = direction === 'left' || direction === 'right'
    ? [[work.left, transverse - half], [work.right, transverse - half], [work.right, transverse + half], [work.left, transverse + half]]
    : [[transverse - half, work.bottom], [transverse + half, work.bottom], [transverse + half, work.top], [transverse - half, work.top]];
  return new CrossSectionCtor([polygon], 'NonZero');
}

function firstContactCoordinate(targetContact: CrossSection, direction: Direction): number {
  const bounds = targetContact.bounds();
  if (direction === 'left') return bounds.min[0];
  if (direction === 'right') return bounds.max[0];
  if (direction === 'up') return bounds.max[1];
  return bounds.min[1];
}

function transverseOverlap(a: ReturnType<CrossSection['bounds']>, b: ReturnType<CrossSection['bounds']>, direction: Direction): number {
  return direction === 'left' || direction === 'right'
    ? Math.min(a.max[1], b.max[1]) - Math.max(a.min[1], b.min[1])
    : Math.min(a.max[0], b.max[0]) - Math.max(a.min[0], b.min[0]);
}

function distanceBeforeContact(bounds: ReturnType<CrossSection['bounds']>, direction: Direction, firstContact: number): number {
  if (direction === 'left') return firstContact - bounds.max[0];
  if (direction === 'right') return bounds.min[0] - firstContact;
  if (direction === 'up') return bounds.min[1] - firstContact;
  return firstContact - bounds.max[1];
}

function findAdjacentOpeningPart(parts: readonly CrossSection[], targetContact: CrossSection, direction: Direction, firstContact: number, tolerance: number): CrossSection | undefined {
  const targetBounds = targetContact.bounds();
  return parts
    .map((part) => ({ part, bounds: part.bounds() }))
    .map((item) => ({ ...item, distance: distanceBeforeContact(item.bounds, direction, firstContact) }))
    .filter((item) => item.distance >= -tolerance && item.distance <= tolerance && transverseOverlap(item.bounds, targetBounds, direction) > tolerance)
    .sort((a, b) => a.distance - b.distance || b.part.area() - a.part.area() || a.bounds.min[0] - b.bounds.min[0] || a.bounds.min[1] - b.bounds.min[1])[0]?.part;
}

function buildBridgeCandidate(CrossSectionCtor: ManifoldApi['CrossSection'], cutterSection: CrossSection, targetSection: CrossSection, direction: Direction, transverse: number, width: number, work: WorldWorkingBounds): BridgeCandidate | undefined {
  let probe: CrossSection | undefined;
  let targetContact: CrossSection | undefined;
  let insideOpening: CrossSection | undefined;
  let openingParts: CrossSection[] = [];
  try {
    probe = createProbe(CrossSectionCtor, direction, transverse, width, work);
    targetContact = probe.intersect(targetSection);
    if (targetContact.isEmpty()) return undefined;
    const firstContact = firstContactCoordinate(targetContact, direction);
    insideOpening = probe.intersect(cutterSection);
    if (insideOpening.isEmpty()) return undefined;
    openingParts = insideOpening.decompose();
    const bridgePatch = findAdjacentOpeningPart(openingParts, targetContact, direction, firstContact, geometryTolerance(work));
    if (!bridgePatch) return undefined;
    const patchBounds = bridgePatch.bounds();
    const span = direction === 'left' || direction === 'right'
      ? patchBounds.max[0] - patchBounds.min[0]
      : patchBounds.max[1] - patchBounds.min[1];
    const area = bridgePatch.area();
    if (!Number.isFinite(area) || area <= 0 || !Number.isFinite(span) || span <= 0) return undefined;
    const bridge: Bridge = direction === 'left' || direction === 'right'
      ? { x: firstContact, y: transverse, width, direction }
      : { x: transverse, y: firstContact, width, direction };
    return { bridge, area, span, transverse, patch: bridgePatch.toPolygons() };
  } finally {
    for (const part of openingParts) part.delete();
    insideOpening?.delete();
    targetContact?.delete();
    probe?.delete();
  }
}

function compareBridgeCandidates(a: BridgeCandidate, b: BridgeCandidate): number {
  return a.area - b.area
    || a.span - b.span
    || directionRank(a.bridge.direction) - directionRank(b.bridge.direction)
    || a.transverse - b.transverse
    || a.bridge.x - b.bridge.x
    || a.bridge.y - b.bridge.y;
}

function collectBridgeCandidates(CrossSectionCtor: ManifoldApi['CrossSection'], cutterSection: CrossSection, targetSection: CrossSection, width: number, work: WorldWorkingBounds, requestedCount: number): BridgeCandidate[] {
  const candidates: BridgeCandidate[] = [];
  for (const direction of orderBridgeDirections(targetSection.bounds(), work)) {
    for (const transverse of collectTransverseCandidates(targetSection, direction, work, width, requestedCount)) {
      const candidate = buildBridgeCandidate(CrossSectionCtor, cutterSection, targetSection, direction, transverse, width, work);
      if (candidate) candidates.push(candidate);
    }
  }
  return candidates.sort(compareBridgeCandidates);
}

function bridgeContactDistance(a: BridgeCandidate, b: BridgeCandidate): number {
  return Math.hypot(a.bridge.x - b.bridge.x, a.bridge.y - b.bridge.y);
}

function selectBridgeCandidates(candidates: readonly BridgeCandidate[], requestedCount: number, width: number): BridgeCandidate[] {
  if (!candidates.length || requestedCount <= 0) return [];
  const selected = [candidates[0]];
  const remaining = candidates.slice(1);
  const minimumSpacing = width * MIN_BRIDGE_CENTER_SPACING_FACTOR;

  while (selected.length < requestedCount && remaining.length) {
    let bestIndex = -1;
    let bestDistance = -Infinity;
    for (let index = 0; index < remaining.length; index += 1) {
      const candidate = remaining[index];
      const minimumDistance = Math.min(...selected.map((existing) => bridgeContactDistance(candidate, existing)));
      if (minimumDistance < minimumSpacing) continue;
      if (minimumDistance > bestDistance || (minimumDistance === bestDistance && (bestIndex < 0 || compareBridgeCandidates(candidate, remaining[bestIndex]) < 0))) {
        bestIndex = index;
        bestDistance = minimumDistance;
      }
    }
    if (bestIndex < 0) break;
    selected.push(remaining[bestIndex]);
    remaining.splice(bestIndex, 1);
  }

  return selected;
}

function extrudeSection(section: CrossSection, height: number, z0: number): Solid {
  const extruded = section.extrude(height);
  try { return extruded.translate([0, 0, z0]); }
  finally { extruded.delete(); }
}

function validateBridgeCandidates(CrossSectionCtor: ManifoldApi['CrossSection'], base: Solid, cutterSection: CrossSection, candidates: readonly BridgeCandidate[], height: number, z0: number, currentPieceCount: number): ValidatedBridge | undefined {
  if (!candidates.length) return undefined;
  let workingSection = cutterSection;
  let ownsWorkingSection = false;
  let nextCutter: Solid | undefined;
  let nextResult: Solid | undefined;
  try {
    for (const candidate of candidates) {
      const patch = new CrossSectionCtor(candidate.patch, 'NonZero');
      let updatedSection: CrossSection;
      try {
        updatedSection = workingSection.subtract(patch);
      } finally {
        patch.delete();
      }
      if (ownsWorkingSection) workingSection.delete();
      workingSection = updatedSection;
      ownsWorkingSection = true;
    }

    nextCutter = extrudeSection(workingSection, height, z0);
    nextResult = base.subtract(nextCutter);
    assertPrintable(nextResult);
    const nextPieces = nextResult.decompose();
    try {
      if (nextPieces.length >= currentPieceCount) return undefined;
    } finally {
      for (const piece of nextPieces) piece.delete();
    }

    const validated = { cutterSection: workingSection, cutter: nextCutter, result: nextResult };
    ownsWorkingSection = false;
    nextCutter = undefined;
    nextResult = undefined;
    return validated;
  } finally {
    nextResult?.delete();
    nextCutter?.delete();
    if (ownsWorkingSection) workingSection.delete();
  }
}

export function placedArtworkPolygons(artwork: PlanarArtwork, placement: ArtworkPlacement, template: TemplateGeometry): Array<Array<[number, number]>> {
  return artwork.contours.map((contour) => contour.points.map(([x, y]) => [
    template.bounds.min[0] + x * placement.scale + placement.x,
    template.bounds.max[1] - (y * placement.scale + placement.y),
  ]));
}

function assertPrintable(solid: Solid): void {
  const mesh = solid.getMesh();
  if (solid.isEmpty() || solid.status() !== 'NoError' || !Number.isFinite(solid.volume()) || solid.volume() <= 0 || mesh.triVerts.length < 3 || !Array.from(mesh.vertProperties).every(Number.isFinite)) {
    throw new Error('Stencil boolean did not produce a finite, non-empty manifold solid.');
  }
}

/** Uses Manifold for every planar/solid boolean; bridges are kept by removing only the adjacent cutter component needed for each connection. */
export interface CaptionSettings { readonly text: string; readonly font: BundledFontId; readonly x: number; readonly y: number; readonly size: number; readonly embossHeight: number; }

function captionPolygons(template: TemplateGeometry, caption: CaptionSettings): Array<Array<[number, number]>> {
  if (!caption.text) return [];
  if (!Number.isFinite(caption.embossHeight) || caption.embossHeight <= 0) throw new Error('Caption emboss height must be positive.');
  const outline = captionOutline(caption.text, caption.font, caption.size);
  const topWidth = template.bounds.max[0] - template.bounds.min[0]; const topHeight = template.bounds.max[1] - template.bounds.min[1];
  if (caption.x + outline.bounds.maxX <= 0 || caption.y + outline.bounds.maxY <= 0 || caption.x >= topWidth || caption.y >= topHeight) throw new Error('Caption is completely outside the template.');
  if (caption.x < 0 || caption.y < 0 || caption.x + outline.bounds.maxX > topWidth || caption.y + outline.bounds.maxY > topHeight) throw new Error('Caption must fit completely on the template top surface.');
  return outline.contours.map((contour) => contour.map(([x, y]) => [template.bounds.min[0] + caption.x + x, template.bounds.max[1] - (caption.y + y)]));
}

function generatedMesh(result: Solid, bridges: readonly Bridge[], bridgeShortfallIslandCount: number): GeneratedStencil {
  const mesh = result.getMesh();
  return { positions: new Float32Array(mesh.vertProperties), indices: new Uint32Array(mesh.triVerts), bridges, bridgeShortfallIslandCount };
}

function generatedMeshWithCaption(api: ManifoldApi, result: Solid, bridges: readonly Bridge[], bridgeShortfallIslandCount: number, template: TemplateGeometry, caption: CaptionSettings): GeneratedStencil {
  const { CrossSection, Manifold } = api;
  const section = new CrossSection(captionPolygons(template, caption), 'NonZero');
  let raised: Solid | undefined;
  let complete: Solid | undefined;
  try {
    if (section.isEmpty()) throw new Error('Caption does not produce printable closed glyph outlines.');
    raised = extrudeSection(section, caption.embossHeight, template.bounds.max[2]);
    complete = Manifold.union(result, raised);
    assertPrintable(complete);
    const completeMesh = complete.getMesh();
    const captionMesh = raised.getMesh();
    return {
      positions: new Float32Array(completeMesh.vertProperties),
      indices: new Uint32Array(completeMesh.triVerts),
      bridges,
      bridgeShortfallIslandCount,
      caption: { positions: new Float32Array(captionMesh.vertProperties), indices: new Uint32Array(captionMesh.triVerts) },
    };
  } finally {
    complete?.delete();
    raised?.delete();
    section.delete();
  }
}

export function generateStencil(api: ManifoldApi, template: TemplateGeometry, artwork: PlanarArtwork, placement: ArtworkPlacement, rectangle: WorkingRectangle, bridgeSettings: BridgeSettings, caption?: CaptionSettings): GeneratedStencil {
  const bridgeWidth = bridgeSettings.width;
  const bridgeCount = bridgeSettings.count;
  if (!Number.isFinite(bridgeWidth) || bridgeWidth < 0.8) throw new Error('Minimum bridge width must be at least 0.8 mm.');
  if (!Number.isInteger(bridgeCount) || bridgeCount < 1 || bridgeCount > MAX_BRIDGE_COUNT) throw new Error('Bridge count must be an integer between 1 and 8.');

  const { Mesh, Manifold, CrossSection } = api;
  const templateMesh = new Mesh({ numProp: 3, vertProperties: template.positions, triVerts: template.indices });
  templateMesh.merge();
  const base = new Manifold(templateMesh);
  let cutterSection: CrossSection | undefined;
  let cutter: Solid | undefined;
  let result: Solid | undefined;
  try {
    assertPrintable(base);
    const contours = placedArtworkPolygons(artwork, placement, template);
    const fillRule = artwork.contours.some((contour) => contour.fillRule === 'evenodd') ? 'EvenOdd' : 'NonZero';
    cutterSection = new CrossSection(contours, fillRule);
    if (cutterSection.isEmpty()) throw new Error('Artwork does not produce a valid filled opening.');
    const z0 = template.bounds.min[2] - 1;
    const height = template.bounds.max[2] - template.bounds.min[2] + 2;
    const work = worldWorkingBounds(rectangle, template);
    const bridges: Bridge[] = [];
    let bridgeShortfallIslandCount = 0;
    cutter = extrudeSection(cutterSection, height, z0);
    result = base.subtract(cutter);

    for (let attempt = 0; attempt < 32; attempt += 1) {
      assertPrintable(result);
      const pieces = result.decompose();
      try {
        if (pieces.length === 1) {
          if (!caption?.text) return generatedMesh(result, bridges, bridgeShortfallIslandCount);
          return generatedMeshWithCaption(api, result, bridges, bridgeShortfallIslandCount, template, caption);
        }

        pieces.sort((a, b) => b.volume() - a.volume());
        let bridged = false;
        for (const detached of pieces.slice(1)) {
          const targetSection = detached.project();
          try {
            const candidates = collectBridgeCandidates(CrossSection, cutterSection, targetSection, bridgeWidth, work, bridgeCount);
            const selected = selectBridgeCandidates(candidates, bridgeCount, bridgeWidth);
            for (let selectedCount = selected.length; selectedCount >= 1; selectedCount -= 1) {
              const acceptedCandidates = selected.slice(0, selectedCount);
              const validated = validateBridgeCandidates(CrossSection, base, cutterSection, acceptedCandidates, height, z0, pieces.length);
              if (!validated) continue;

              const previousSection = cutterSection;
              const previousCutter = cutter;
              const previousResult = result;
              cutterSection = validated.cutterSection;
              cutter = validated.cutter;
              result = validated.result;
              bridges.push(...acceptedCandidates.map((candidate) => candidate.bridge));
              if (acceptedCandidates.length < bridgeCount) bridgeShortfallIslandCount += 1;
              previousResult.delete();
              previousCutter.delete();
              previousSection.delete();
              bridged = true;
              break;
            }
          } finally {
            targetSection.delete();
          }
          if (bridged) break;
        }
        if (!bridged) throw new Error('Unable to create a manufacturable bridge inside the artwork area for a detached island.');
      } finally {
        for (const piece of pieces) piece.delete();
      }
    }
    throw new Error('Bridge generation did not converge on a connected printable stencil.');
  } finally {
    result?.delete();
    cutter?.delete();
    cutterSection?.delete();
    base.delete();
  }
}
