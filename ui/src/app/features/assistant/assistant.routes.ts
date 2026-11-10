import { Routes } from '@angular/router';

/** Lazy-loaded feature boundary: can later move to an MFE remote without restructuring. */
export const assistantRoutes: Routes = [
  {
    path: '',
    title: 'Assistant · OneSpace',
    loadComponent: () => import('./components/chat-page/chat-page').then(m => m.ChatPage),
  },
];
