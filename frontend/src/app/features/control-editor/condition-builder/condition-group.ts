import { Component, computed, inject, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { DEFINITION_LIMITS, EditorGroup, EditorNode, Logic } from '../../../core/definition/definition.model';
import { childPath, describePath, newGroup, newRule } from '../../../core/definition/definition-tree';
import { LOGIC_LABELS } from '../../../core/labels';
import { ConditionContext } from './condition-context';
import { ConditionRule } from './condition-rule';

/** A group of conditions joined by AND / OR. Recursive: a group may contain groups (up to the server's depth limit). */
@Component({
  selector: 'app-condition-group',
  imports: [ConditionRule, MatButtonModule, MatButtonToggleModule, MatIconModule, MatTooltipModule],
  template: `
    <section class="group" [class.nested]="depth() > 1" [class.or]="group().logic === 'OR'" [attr.aria-label]="location()">
      <header class="group-header">
        <mat-button-toggle-group
          [value]="group().logic"
          (change)="setLogic($event.value)"
          hideSingleSelectionIndicator
          [attr.aria-label]="'אופן החיבור בין התנאים — ' + location()"
        >
          <mat-button-toggle value="AND">כל התנאים (וגם)</mat-button-toggle>
          <mat-button-toggle value="OR">לפחות אחד (או)</mat-button-toggle>
        </mat-button-toggle-group>
        <span class="spacer"></span>
        @if (removable()) {
          <button mat-icon-button type="button" (click)="remove.emit()" [matTooltip]="'מחיקת הקבוצה'" aria-label="מחיקת הקבוצה">
            <mat-icon>delete_sweep</mat-icon>
          </button>
        }
      </header>

      @if (messages().length) {
        <ul class="inline-errors" role="alert">
          @for (m of messages(); track $index) {
            <li>{{ m }}</li>
          }
        </ul>
      }

      <div class="children">
        @for (child of group().children; track child.id; let i = $index, first = $first) {
          @if (!first) {
            <div class="connector">
              <button
                type="button"
                class="connector-toggle"
                (click)="toggleLogic()"
                [matTooltip]="toggleTooltip()"
                [attr.aria-label]="logicLabel() + ' — ' + toggleTooltip()"
              >
                {{ logicLabel() }}
                <mat-icon aria-hidden="true">swap_horiz</mat-icon>
              </button>
            </div>
          }
          @if (child.type === 'rule') {
            <app-condition-rule [rule]="child" [path]="childPath(i)" (ruleChange)="replaceChild(i, $event)" (remove)="removeChild(i)" />
          } @else {
            <app-condition-group
              [group]="child"
              [path]="childPath(i)"
              [depth]="depth() + 1"
              [removable]="true"
              (groupChange)="replaceChild(i, $event)"
              (remove)="removeChild(i)"
            />
          }
        } @empty {
          <p class="muted empty">אין תנאים בקבוצה.</p>
        }
      </div>

      <footer class="group-actions">
        <button mat-stroked-button type="button" (click)="addRule()">
          <mat-icon>add</mat-icon>
          הוספת תנאי
        </button>
        <button mat-button type="button" (click)="addGroup()" [disabled]="!canNest()">
          <mat-icon>account_tree</mat-icon>
          הוספת קבוצה
        </button>
      </footer>
    </section>
  `,
  styles: `
    .group {
      border: 1px solid var(--app-border);
      border-inline-start: 4px solid var(--app-and);
      border-radius: 12px;
      padding: 12px;
      background: var(--app-surface-alt);
    }
    .group.or { border-inline-start-color: var(--app-or); }
    .group.nested { background: var(--app-surface); }
    .group-header { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; margin-bottom: 10px; }
    .spacer { flex: 1; }
    .children { display: flex; flex-direction: column; }
    .connector { display: flex; align-items: center; padding: 4px 16px; }
    .connector-toggle {
      display: inline-flex; align-items: center; gap: 2px;
      font: inherit; font-size: 12px; font-weight: 600; padding: 2px 8px 2px 10px; border-radius: 10px;
      border: 1px solid transparent; cursor: pointer;
      background: var(--app-and-bg); color: var(--app-and-text);
    }
    .connector-toggle mat-icon { font-size: 14px; width: 14px; height: 14px; opacity: 0.7; }
    .connector-toggle:hover { border-color: currentColor; }
    .group.or > .children > .connector .connector-toggle { background: var(--app-or-bg); color: var(--app-or-text); }
    .group-actions { display: flex; gap: 8px; flex-wrap: wrap; margin-top: 10px; }
    .empty { margin: 8px 4px; }
  `,
})
export class ConditionGroup {
  private readonly ctx = inject(ConditionContext);

  readonly group = input.required<EditorGroup>();
  readonly path = input.required<string>();
  readonly depth = input(1);
  readonly removable = input(false);
  readonly groupChange = output<EditorGroup>();
  readonly remove = output<void>();

  protected readonly logicLabel = computed(() => LOGIC_LABELS[this.group().logic]);
  protected readonly canNest = computed(() => this.depth() < DEFINITION_LIMITS.maxDepth);
  protected readonly messages = computed(() => this.ctx.messagesAt(this.path(), true));
  protected readonly location = computed(() =>
    this.depth() === 1 ? 'הקבוצה הראשית' : `קבוצה — ${describePath(this.path())}`,
  );

  protected childPath(index: number): string {
    return childPath(this.path(), index);
  }

  protected readonly toggleTooltip = computed(() =>
    this.group().logic === 'AND'
      ? 'לחיצה תחליף ל"או" — מספיק שאחד מהתנאים בקבוצה יתקיים'
      : 'לחיצה תחליף ל"וגם" — כל התנאים בקבוצה חייבים להתקיים',
  );

  /** The connector between conditions switches the logic of the whole group. */
  protected toggleLogic(): void {
    this.setLogic(this.group().logic === 'AND' ? 'OR' : 'AND');
  }

  protected setLogic(logic: Logic): void {
    this.groupChange.emit({ ...this.group(), logic });
  }

  protected addRule(): void {
    this.setChildren([...this.group().children, newRule()]);
  }

  protected addGroup(): void {
    // a sub-group usually means the other logic ("A and (B or C)")
    this.setChildren([...this.group().children, newGroup(this.group().logic === 'AND' ? 'OR' : 'AND')]);
  }

  protected replaceChild(index: number, child: EditorNode): void {
    this.setChildren(this.group().children.map((c, i) => (i === index ? child : c)));
  }

  protected removeChild(index: number): void {
    this.setChildren(this.group().children.filter((_, i) => i !== index));
  }

  private setChildren(children: EditorNode[]): void {
    this.groupChange.emit({ ...this.group(), children });
  }
}
