import type { GeometryResult, StencilResult } from './project-state';
import { installWorkerDomParser } from './worker-dom-parser';
import ManifoldModule from 'manifold-3d';
import manifoldWasmUrl from 'manifold-3d/manifold.wasm?url';
import { parseTemplateBytes, templateFormatFromName } from './template-format-adapter';
import { templateBounds, type TemplateGeometry } from './template-geometry';
import { containPlacement, isContained, placedBounds, workingRectangle } from './artwork-placement';
import { parseSvgArtwork } from './svg-artwork-adapter';
import type { ArtworkResult, ProjectSettings } from './project-state';
import { generateStencil } from './stencil-generation';

installWorkerDomParser();

type WorkerRequest =
  | { type: 'import-template'; version: number; name: string; bytes: ArrayBuffer }
  | { type: 'import-artwork'; version: number; source: string; settings: ProjectSettings; template?: TemplateGeometry; resetToFit: boolean }
  | { type: 'generate-stencil'; version: number; settings: ProjectSettings; template: TemplateGeometry; artwork: NonNullable<ArtworkResult['artwork']>; placement: NonNullable<ArtworkResult['placement']>; rectangle: NonNullable<ArtworkResult['workingRectangle']> };

function loadManifold() {
  return ManifoldModule({ locateFile: () => manifoldWasmUrl });
}

self.addEventListener('message', async (event: MessageEvent<WorkerRequest>) => {
  if (event.data.type === 'generate-stencil') {
    const { version, settings, template, artwork, placement, rectangle } = event.data;
    try {
      const api = await loadManifold(); api.setup();
      const stencil = generateStencil(api, template, artwork, placement, rectangle, settings.bridgeWidth, { text: settings.caption, font: settings.captionFont, x: settings.captionX, y: settings.captionY, size: settings.captionSize, embossHeight: settings.captionEmbossHeight });
      const result: StencilResult = { kind: 'stencil', version, isValid: true, message: `Generated printable stencil with ${stencil.bridges.length} automatic bridge${stencil.bridges.length === 1 ? '' : 's'}.`, stencil };
      const transfers: Transferable[] = [stencil.positions.buffer, stencil.indices.buffer];
      if (stencil.caption) transfers.push(stencil.caption.positions.buffer, stencil.caption.indices.buffer);
      (self as unknown as { postMessage(message: unknown, transfer: Transferable[]): void }).postMessage({ type: 'result', result }, transfers);
    } catch (error) {
      const message = error instanceof Error ? error.message : 'Stencil generation failed.';
      self.postMessage({ type: 'result', result: { kind: 'stencil', version, isValid: false, message: `Stencil generation failed: ${message}` } satisfies StencilResult });
    }
    return;
  }
  if (event.data.type === 'import-artwork') {
    const { version, source, settings, template, resetToFit } = event.data;
    try {
      if (!template) throw new Error('Load a valid template before importing artwork.');
      const artwork = parseSvgArtwork(source);
      const rectangle = workingRectangle(template.bounds, { left: settings.artworkLeft, right: settings.artworkRight, top: settings.artworkTop, bottom: settings.artworkBottom });
      const placement = resetToFit ? containPlacement(artwork.bounds, rectangle) : { x: settings.artworkX, y: settings.artworkY, scale: settings.artworkScale };
      if (!isContained(placedBounds(artwork.bounds, placement), rectangle)) throw new Error('Artwork exceeds the working rectangle. Adjust offsets or scale, or use Reset to fit.');
      const result: ArtworkResult = { kind: 'artwork', version, isValid: true, message: 'Artwork is valid and fits the working rectangle.', artwork, placement, workingRectangle: rectangle };
      self.postMessage({ type: 'result', result });
    } catch (error) {
      const message = error instanceof Error ? error.message : 'SVG import failed.';
      self.postMessage({ type: 'result', result: { kind: 'artwork', version, isValid: false, message: `Artwork import failed: ${message}` } satisfies ArtworkResult });
    }
    return;
  }
  if (event.data.type !== 'import-template') return;
  const { version, name, bytes } = event.data;
  try {
    if (bytes.byteLength === 0) throw new Error('Template file is empty.');
    if (bytes.byteLength > 50 * 1024 * 1024) throw new Error('Template exceeds the 50 MB import limit.');
    const source = parseTemplateBytes(templateFormatFromName(name), new Uint8Array(bytes));
    if (!Array.from(source.positions).every(Number.isFinite)) throw new Error('Template contains non-finite vertex coordinates.');
    if (source.indices.length < 3 || source.indices.length % 3 !== 0 || Array.from(source.indices).some((index) => index >= source.positions.length / 3)) throw new Error('Template has invalid triangle indices.');
    const api = await loadManifold();
    api.setup();
    const { Manifold, Mesh } = api;
    const mesh = new Mesh({ numProp: 3, vertProperties: source.positions, triVerts: source.indices });
    mesh.merge();
    const solid = new Manifold(mesh);
    if (solid.isEmpty() || solid.status() !== 'NoError' || !Number.isFinite(solid.volume()) || solid.volume() <= 0) throw new Error('Template is not a closed manifold printable solid.');
    const normalized = solid.getMesh();
    const positions = new Float32Array(normalized.vertProperties);
    const indices = new Uint32Array(normalized.triVerts);
    const bounds = templateBounds(positions);
    const template: TemplateGeometry = { positions, indices, bounds, topView: { origin: [bounds.min[0], bounds.max[1]], xRight: true, yDown: true } };
    solid.delete();
    const result: GeometryResult = { kind: 'template', version, isValid: true, message: `Imported ${name}: printable manifold solid in millimetres.`, template };
    (self as unknown as { postMessage(message: unknown, transfer: Transferable[]): void }).postMessage({ type: 'result', result }, [positions.buffer, indices.buffer]);
  } catch (error) {
    const message = error instanceof Error ? error.message : 'Template import failed.';
    self.postMessage({ type: 'result', result: { kind: 'template', version, isValid: false, message: `Template import failed: ${message}` } satisfies GeometryResult });
  }
});
