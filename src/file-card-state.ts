export type FileKind = 'template' | 'artwork';
export type FilePhase = 'empty' | 'loading' | 'ready' | 'error';

export interface FileCardPresentation {
  readonly detail: string;
  readonly action: string;
  readonly actionLabel: string;
}

export function getFileCardPresentation(kind: FileKind, phase: FilePhase, name?: string): FileCardPresentation {
  const title = kind === 'template' ? '3D template' : 'SVG artwork';
  const action = phase === 'ready' || phase === 'error' ? 'Replace' : kind === 'template' ? 'Choose' : 'Upload';
  const actionLabel = `${action} ${title}${action === 'Choose' || action === 'Upload' ? ' file' : ''}`;
  if (phase === 'empty') {
    return { detail: kind === 'template' ? 'No template selected' : 'No artwork selected', action, actionLabel };
  }
  const filename = name || title;
  const suffix = phase === 'loading' ? 'Loading…' : phase === 'ready' ? 'Loaded' : 'Failed';
  return { detail: `${filename} · ${suffix}`, action, actionLabel };
}
