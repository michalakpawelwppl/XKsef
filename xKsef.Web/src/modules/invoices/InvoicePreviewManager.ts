import type { RequestContext } from '../../platform/session/RequestContext';

interface PreviewEntry {
  readonly window: Window;
  readonly controller: AbortController;
  readonly cleanup: () => void;
}

export class InvoicePreviewManager {
  private readonly previews = new Set<PreviewEntry>();
  private readonly unsubscribe: () => void;

  constructor(context: RequestContext) {
    this.unsubscribe = context.subscribe(() => this.closeAll());
  }

  register(previewWindow: Window, controller: AbortController, cleanup: () => void = () => {}) {
    const entry: PreviewEntry = { window: previewWindow, controller, cleanup };
    this.previews.add(entry);

    const dispose = () => {
      if (!this.previews.delete(entry)) return;
      controller.abort();
      cleanup();
    };

    previewWindow.addEventListener('pagehide', dispose, { once: true });

    return dispose;
  }

  closeAll() {
    for (const entry of [...this.previews]) {
      entry.controller.abort();
      entry.cleanup();
      if (!entry.window.closed) entry.window.close();
      this.previews.delete(entry);
    }
  }

  dispose() {
    this.closeAll();
    this.unsubscribe();
  }
}