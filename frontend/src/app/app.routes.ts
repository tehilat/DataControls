import { Routes } from '@angular/router';

// Feature routes are lazy-loaded.
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'controls' },
  {
    path: 'controls',
    title: 'בקרות',
    loadComponent: () => import('./features/controls-list/controls-list-page').then((m) => m.ControlsListPage),
  },
  {
    path: 'controls/new',
    title: 'בקרה חדשה',
    loadComponent: () => import('./features/control-editor/control-editor-page').then((m) => m.ControlEditorPage),
  },
  {
    path: 'controls/ai',
    title: 'יצירת בקרה בעזרת AI',
    loadComponent: () => import('./features/ai-create/ai-create-page').then((m) => m.AiCreatePage),
  },
  {
    path: 'controls/:id/edit',
    title: 'עריכת בקרה',
    loadComponent: () => import('./features/control-editor/control-editor-page').then((m) => m.ControlEditorPage),
  },
  {
    path: 'runs',
    title: 'היסטוריית הרצות',
    loadComponent: () => import('./features/runs/run-history-page').then((m) => m.RunHistoryPage),
  },
  {
    path: 'runs/:runId',
    title: 'תוצאות הרצה',
    loadComponent: () => import('./features/runs/run-results-page').then((m) => m.RunResultsPage),
  },
  { path: '**', redirectTo: 'controls' },
];
