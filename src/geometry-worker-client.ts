import type { ArtworkResult, GeometryResult, ProjectState, StencilResult } from './project-state';

interface ImportTemplateRequest { type: 'import-template'; version: number; name: string; bytes: ArrayBuffer; }
interface ImportArtworkRequest { type: 'import-artwork'; version: number; source: string; settings: ProjectState['settings']; template?: NonNullable<ProjectState['geometry']>['template']; resetToFit: boolean; }
interface GenerateStencilRequest { type: 'generate-stencil'; version: number; settings: ProjectState['settings']; template: NonNullable<ProjectState['geometry']>['template']; artwork: NonNullable<ProjectState['artwork']>['artwork']; placement: NonNullable<ProjectState['artwork']>['placement']; rectangle: NonNullable<ProjectState['artwork']>['workingRectangle']; }
interface GenerateResponse { type: 'result'; result: GeometryResult | ArtworkResult | StencilResult; }

/** Keeps worker messages versioned; a response may only be applied to its source edit. */
export class GeometryWorkerClient {
  private readonly worker = new Worker(new URL('./geometry.worker.ts', import.meta.url), { type: 'module' });

  constructor(private readonly apply: (result: GeometryResult | ArtworkResult | StencilResult) => boolean) {
    this.worker.addEventListener('message', (event: MessageEvent<GenerateResponse>) => {
      if (event.data.type === 'result') this.apply(event.data.result);
    });
  }

  generateStencil(state: ProjectState): void {
    if (!state.geometry?.template || !state.artwork?.artwork || !state.artwork.placement || !state.artwork.workingRectangle) return;
    const request: GenerateStencilRequest = { type: 'generate-stencil', version: state.version, settings: state.settings, template: state.geometry.template, artwork: state.artwork.artwork, placement: state.artwork.placement, rectangle: state.artwork.workingRectangle };
    this.worker.postMessage(request);
  }

  importArtwork(state: ProjectState, source: string, resetToFit = false): void {
    const request: ImportArtworkRequest = { type: 'import-artwork', version: state.version, source, settings: state.settings, template: state.geometry?.template, resetToFit };
    this.worker.postMessage(request);
  }

  importTemplate(state: ProjectState, name: string, bytes: ArrayBuffer): void {
    const request: ImportTemplateRequest = { type: 'import-template', version: state.version, name, bytes };
    this.worker.postMessage(request, [bytes]);
  }

  dispose(): void { this.worker.terminate(); }
}
