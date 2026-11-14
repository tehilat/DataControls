import { Service } from '@angular/core';
import { ControlSource, Severity } from './api/models';
import { ControlDefinition } from './definition/definition.model';

/** A ready-made control (e.g. an AI suggestion) to open in the editor for review before saving. */
export interface ControlSeed {
  name?: string;
  description?: string | null;
  severity?: Severity;
  source?: ControlSource;
  definition: ControlDefinition;
}

/**
 * Hands a definition from another screen (stage B: "create with AI") to the control editor.
 * The editor consumes it once when opened at /controls/new?draft=1.
 */
@Service()
export class ControlDraftService {
  private draft: ControlSeed | null = null;

  set(seed: ControlSeed): void {
    this.draft = structuredClone(seed);
  }

  take(): ControlSeed | null {
    const draft = this.draft;
    this.draft = null;
    return draft;
  }
}
